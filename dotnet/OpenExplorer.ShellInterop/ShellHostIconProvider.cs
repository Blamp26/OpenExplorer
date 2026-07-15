using System.Diagnostics;
using System.IO.Pipes;
using OpenExplorer.Application.Icons;
using OpenExplorer.Contracts;

namespace OpenExplorer.ShellInterop;

/// Infrastructure-only client for the isolated Rust ShellHost. Pixel payloads are
/// consumed at this boundary; UI/Application receive only icon state.
public sealed class ShellHostIconProvider : IExplorerIconProvider
{
    private const int MaxBatch = 64;
    private const int MaxFrame = 1024 * 1024;
    private const int MaxPath = 32 * 1024;
    private const int MaxPixels = 32 * 32 * 4;
    private readonly string executablePath;
    private readonly object gate = new();
    private Process? process;
    private string? pipeName;
    private int failures;
    private bool disposed;

    public ShellHostIconProvider(string? executablePath = null)
    {
        this.executablePath = executablePath ?? Path.Combine(AppContext.BaseDirectory, "open-explorer-shell-host.exe");
    }

    public async ValueTask<IReadOnlyList<ExplorerIconResult>> RequestBatchAsync(
        IReadOnlyList<ExplorerIconRequest> requests, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(requests);
        if (requests.Count == 0) return Array.Empty<ExplorerIconResult>();
        if (requests.Count > MaxBatch) throw new ArgumentOutOfRangeException(nameof(requests));
        ObjectDisposedException.ThrowIf(disposed, this);

        for (int attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                if (!File.Exists(executablePath)) return Generic(requests);
                NamedPipeClientStream pipe = await ConnectAsync(cancellationToken).ConfigureAwait(false);
                await using (pipe.ConfigureAwait(false))
                {
                    byte[] request = EncodeRequest(requests);
                    await pipe.WriteAsync(request, cancellationToken).ConfigureAwait(false);
                    await pipe.FlushAsync(cancellationToken).ConfigureAwait(false);
                    byte[] frame = await ReadFrameAsync(pipe, cancellationToken).ConfigureAwait(false);
                    return DecodeResponse(frame, requests);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch
            {
                RestartHost();
                if (++failures >= 2) return Generic(requests);
                await Task.Delay(TimeSpan.FromMilliseconds(50 * failures), cancellationToken).ConfigureAwait(false);
            }
        }
        return Generic(requests);
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
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WorkingDirectory = AppContext.BaseDirectory,
                });
                pipeName = name;
            }
            name = pipeName!;
        }
        var pipe = new NamedPipeClientStream(".", $"openexplorer-shell-{name}", PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(1000, token).ConfigureAwait(false);
        return pipe;
    }

    private static byte[] EncodeRequest(IReadOnlyList<ExplorerIconRequest> requests)
    {
        using var body = new MemoryStream();
        using var writer = new BinaryWriter(body, System.Text.Encoding.UTF8, leaveOpen: true);
        writer.Write((ushort)1); writer.Write((byte)1); writer.Write((ushort)requests.Count);
        ulong id = 1;
        foreach (ExplorerIconRequest request in requests)
        {
            string path = Path.GetFullPath(Path.Combine(request.Location.Identifier, request.Name));
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(path);
            if (bytes.Length == 0 || bytes.Length > MaxPath) throw new InvalidDataException("Shell icon path is too long.");
            writer.Write(id++); writer.Write(request.ItemKind == ExplorerItemKind.Directory ? 0x10u : 0x80u);
            writer.Write((uint)bytes.Length); writer.Write(bytes);
        }
        byte[] payload = body.ToArray();
        if (payload.Length > MaxFrame) throw new InvalidDataException("Shell icon request is too large.");
        using var frame = new MemoryStream();
        using var frameWriter = new BinaryWriter(frame, System.Text.Encoding.UTF8, leaveOpen: true);
        frameWriter.Write((uint)payload.Length); frameWriter.Write(payload);
        return frame.ToArray();
    }

    private static async Task<byte[]> ReadFrameAsync(Stream stream, CancellationToken token)
    {
        byte[] header = await ReadExactAsync(stream, 4, token).ConfigureAwait(false);
        int length = checked((int)BitConverter.ToUInt32(header));
        if (length <= 0 || length > MaxFrame) throw new InvalidDataException("Malformed ShellHost frame length.");
        return await ReadExactAsync(stream, length, token).ConfigureAwait(false);
    }

    private static async Task<byte[]> ReadExactAsync(Stream stream, int count, CancellationToken token)
    {
        byte[] buffer = new byte[count];
        int offset = 0;
        while (offset < count)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(offset, count - offset), token).ConfigureAwait(false);
            if (read == 0) throw new EndOfStreamException();
            offset += read;
        }
        return buffer;
    }

    private static IReadOnlyList<ExplorerIconResult> DecodeResponse(byte[] payload, IReadOnlyList<ExplorerIconRequest> requests)
    {
        int at = 0;
        ushort version = ReadU16(payload, ref at);
        byte kind = ReadByte(payload, ref at);
        ushort count = ReadU16(payload, ref at);
        if (version != 1 || kind != 2 || count > MaxBatch) throw new InvalidDataException("Malformed ShellHost response.");
        var requestById = requests.Select((request, index) => (Id: (ulong)index + 1, Request: request)).ToDictionary(x => x.Id, x => x.Request);
        var seen = new HashSet<ulong>();
        var result = new List<ExplorerIconResult>(requests.Count);
        for (int i = 0; i < count; i++)
        {
            ulong id = ReadU64(payload, ref at);
            if (!requestById.TryGetValue(id, out ExplorerIconRequest? request) || !seen.Add(id)) throw new InvalidDataException("Unexpected ShellHost response identity.");
            byte status = ReadByte(payload, ref at); int length = checked((int)ReadU32(payload, ref at));
            if (length > MaxPixels) throw new InvalidDataException("Oversized ShellHost icon payload.");
            Skip(payload, ref at, length);
            bool folder = request.ItemKind == ExplorerItemKind.Directory;
            result.Add(new ExplorerIconResult(request.ItemId, status == 0 ? folder ? ExplorerIconKind.ShellFolder : ExplorerIconKind.ShellFile : folder ? ExplorerIconKind.GenericFolder : ExplorerIconKind.GenericFile, request.CacheKey));
        }
        foreach ((ulong id, ExplorerIconRequest request) in requestById)
            if (!seen.Contains(id)) result.Add(new ExplorerIconResult(request.ItemId, request.ItemKind == ExplorerItemKind.Directory ? ExplorerIconKind.GenericFolder : ExplorerIconKind.GenericFile, request.CacheKey));
        if (at != payload.Length) throw new InvalidDataException("Trailing ShellHost response data.");
        return result;
    }

    private static IReadOnlyList<ExplorerIconResult> Generic(IReadOnlyList<ExplorerIconRequest> requests) => requests.Select(request => new ExplorerIconResult(request.ItemId, request.ItemKind == ExplorerItemKind.Directory ? ExplorerIconKind.GenericFolder : ExplorerIconKind.GenericFile, request.CacheKey)).ToArray();
    private static ushort ReadU16(byte[] b, ref int a) { Skip(b, ref a, 2); return BitConverter.ToUInt16(b, a - 2); }
    private static uint ReadU32(byte[] b, ref int a) { Skip(b, ref a, 4); return BitConverter.ToUInt32(b, a - 4); }
    private static ulong ReadU64(byte[] b, ref int a) { Skip(b, ref a, 8); return BitConverter.ToUInt64(b, a - 8); }
    private static byte ReadByte(byte[] b, ref int a) { Skip(b, ref a, 1); return b[a - 1]; }
    private static void Skip(byte[] b, ref int a, int count) { if (count < 0 || count > b.Length - a) throw new InvalidDataException("Truncated ShellHost response."); a += count; }

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
