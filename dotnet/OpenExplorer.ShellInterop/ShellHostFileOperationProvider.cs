using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using OpenExplorer.Contracts;

namespace OpenExplorer.ShellInterop;

/// <summary>Windows-only infrastructure adapter for the isolated ShellHost mutation API.</summary>
public sealed class ShellHostFileOperationProvider : IExplorerFileOperationProvider, IAsyncDisposable
{
    private const int MaxFrame = 1024 * 1024;
    private const int MaxPath = 32 * 1024;
    private const int MaxName = 255;
    private readonly string executablePath;
    private readonly object gate = new();
    private Process? process;
    private string? pipeName;
    private long requestId;
    private bool disposed;

    public ShellHostFileOperationProvider(string? executablePath = null) =>
        this.executablePath = executablePath ?? Path.Combine(AppContext.BaseDirectory, "open-explorer-shell-host.exe");

    public async Task<ExplorerFileOperationResult> ExecuteAsync(ExplorerFileOperationRequest request, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(request);
        if (request.Location.Scheme != ExplorerLocationScheme.File)
            return Failed("This operation is supported only for local filesystem locations.");
        if (!File.Exists(executablePath)) return Failed("File operations are unavailable.");

        for (int attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                await using NamedPipeClientStream pipe = await ConnectAsync(cancellationToken).ConfigureAwait(false);
                byte[] encoded = Encode(request);
                await pipe.WriteAsync(encoded, cancellationToken).ConfigureAwait(false);
                await pipe.FlushAsync(cancellationToken).ConfigureAwait(false);
                return Decode(await ReadFrameAsync(pipe, cancellationToken).ConfigureAwait(false));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return new(ExplorerFileOperationStatus.Cancelled, Array.Empty<ExplorerFileOperationFailure>(), false);
            }
            catch when (attempt == 0)
            {
                RestartHost();
                await Task.Delay(50, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                RestartHost();
                return Failed(exception.Message);
            }
        }
        return Failed("ShellHost did not complete the operation.");
    }

    private async Task<NamedPipeClientStream> ConnectAsync(CancellationToken token)
    {
        string name;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (process is null || process.HasExited)
            {
                name = $"{Environment.ProcessId}-{Guid.NewGuid():N}";
                process = Process.Start(new ProcessStartInfo(executablePath, $"--pipe {name}")
                {
                    CreateNoWindow = true, UseShellExecute = false, WorkingDirectory = AppContext.BaseDirectory,
                });
                pipeName = name;
            }
            name = pipeName!;
        }
        NamedPipeClientStream pipe = new(".", $"openexplorer-shell-{name}", PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(1000, token).ConfigureAwait(false);
        return pipe;
    }

    private byte[] Encode(ExplorerFileOperationRequest request)
    {
        ulong id = unchecked((ulong)Interlocked.Increment(ref requestId));
        using MemoryStream body = new();
        using (BinaryWriter writer = new(body, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write((ushort)1); writer.Write((byte)4); writer.Write((ushort)request.Items.Count);
            writer.Write(id); writer.Write(request.Kind switch
            {
                ExplorerFileOperationKind.Rename => (byte)1,
                ExplorerFileOperationKind.CreateFolder => (byte)2,
                ExplorerFileOperationKind.RecycleBinDelete => (byte)3,
                _ => throw new InvalidDataException("Unsupported file operation.")
            });
            WriteText32(writer, request.Location.Identifier, "location");
            WriteText16(writer, request.DesiredName);
            foreach (ExplorerFileOperationItem item in request.Items)
            {
                writer.Write(item.ItemId);
                string path = Path.GetFullPath(Path.Combine(request.Location.Identifier, item.Name));
                WriteText32(writer, path, "path");
            }
        }
        byte[] payload = body.ToArray();
        if (payload.Length > MaxFrame) throw new InvalidDataException("Operation request is too large.");
        using MemoryStream frame = new();
        using BinaryWriter frameWriter = new(frame, Encoding.UTF8, leaveOpen: true);
        frameWriter.Write((uint)payload.Length); frameWriter.Write(payload);
        return frame.ToArray();
    }

    private static ExplorerFileOperationResult Decode(byte[] payload)
    {
        using BinaryReader reader = new(new MemoryStream(payload), Encoding.UTF8, leaveOpen: false);
        if (reader.ReadUInt16() != 1 || reader.ReadByte() != 5) throw new InvalidDataException("Malformed ShellHost response.");
        int count = reader.ReadUInt16();
        if (count > 64) throw new InvalidDataException("Too many operation failures.");
        _ = reader.ReadUInt64();
        ExplorerFileOperationStatus status = reader.ReadByte() switch
        {
            0 => ExplorerFileOperationStatus.Succeeded,
            1 => ExplorerFileOperationStatus.Cancelled,
            2 => ExplorerFileOperationStatus.Failed,
            3 => ExplorerFileOperationStatus.Partial,
            _ => throw new InvalidDataException("Malformed operation status.")
        };
        string? createdName = ReadText16(reader);
        ulong? createdItemId = reader.ReadByte() == 0 ? null : reader.ReadUInt64();
        List<ExplorerFileOperationFailure> failures = new(count);
        for (int i = 0; i < count; i++)
        {
            ulong? itemId = reader.ReadByte() == 0 ? null : reader.ReadUInt64();
            string? name = ReadText16(reader);
            string message = ReadRequiredText16(reader);
            failures.Add(new(itemId, name, message));
        }
        if (reader.BaseStream.Position != reader.BaseStream.Length) throw new InvalidDataException("Trailing ShellHost response data.");
        return new(status, failures, status is ExplorerFileOperationStatus.Succeeded or ExplorerFileOperationStatus.Partial, createdName, createdItemId);
    }

    private static void WriteText16(BinaryWriter writer, string? value)
    {
        if (value is null) { writer.Write((ushort)0); return; }
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        if (bytes.Length > MaxName) throw new ArgumentException("The name is too long.", nameof(value));
        writer.Write((ushort)bytes.Length); writer.Write(bytes);
    }

    private static void WriteText32(BinaryWriter writer, string value, string label)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        if (bytes.Length == 0 || bytes.Length > MaxPath) throw new ArgumentException($"The {label} is invalid.", label);
        writer.Write((uint)bytes.Length); writer.Write(bytes);
    }

    private static string? ReadText16(BinaryReader reader)
    {
        int length = reader.ReadUInt16();
        if (length > MaxName) throw new InvalidDataException("Oversized ShellHost text.");
        if (length == 0) return null;
        byte[] bytes = reader.ReadBytes(length);
        if (bytes.Length != length) throw new InvalidDataException("Truncated ShellHost text.");
        string value = Encoding.UTF8.GetString(bytes);
        return value.Length == 0 ? throw new InvalidDataException("Malformed ShellHost text.") : value;
    }

    private static string ReadRequiredText16(BinaryReader reader) => ReadText16(reader) ?? throw new InvalidDataException("Missing ShellHost failure message.");

    private static async Task<byte[]> ReadFrameAsync(Stream stream, CancellationToken token)
    {
        byte[] header = await ReadExactAsync(stream, 4, token).ConfigureAwait(false);
        int length = checked((int)BitConverter.ToUInt32(header));
        if (length <= 0 || length > MaxFrame) throw new InvalidDataException("Malformed ShellHost frame length.");
        return await ReadExactAsync(stream, length, token).ConfigureAwait(false);
    }

    private static async Task<byte[]> ReadExactAsync(Stream stream, int count, CancellationToken token)
    {
        byte[] buffer = new byte[count]; int offset = 0;
        while (offset < count)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(offset, count - offset), token).ConfigureAwait(false);
            if (read == 0) throw new EndOfStreamException();
            offset += read;
        }
        return buffer;
    }

    private static ExplorerFileOperationResult Failed(string message) => new(ExplorerFileOperationStatus.Failed, [new(null, null, message)], false);

    private void RestartHost()
    {
        lock (gate)
        {
            try { if (process is { HasExited: false }) process.Kill(entireProcessTree: true); } catch { }
            process?.Dispose(); process = null; pipeName = null;
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (gate) { if (disposed) return ValueTask.CompletedTask; disposed = true; }
        RestartHost();
        return ValueTask.CompletedTask;
    }
}
