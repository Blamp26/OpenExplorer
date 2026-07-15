using System.Buffers;
using System.Text;
using OpenExplorer.Contracts;

namespace OpenExplorer.Interop;

public sealed class NativeExplorerSnapshot : IExplorerSnapshot
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private readonly SafeSnapshotHandle handle;
    private bool disposed;

    internal NativeExplorerSnapshot(SafeSnapshotHandle handle)
    {
        this.handle = handle;
    }

    public ulong Count
    {
        get
        {
            ThrowIfDisposed();
            NativeStatusExtensions.ThrowIfFailed(NativeMethods.SnapshotCount(handle.DangerousGetHandle(), out ulong count), "fe_snapshot_count");
            return count;
        }
    }

    public unsafe ExplorerItemBatch GetRange(ulong start, uint count)
    {
        ThrowIfDisposed();
        NativeStatusExtensions.ThrowIfFailed(
            NativeMethods.SnapshotGetRangeRequirements(handle.DangerousGetHandle(), start, count, out uint actualCount, out uint utf8Bytes),
            "fe_snapshot_get_range_requirements");

        if (actualCount == 0)
        {
            return new ExplorerItemBatch(start, Array.Empty<ExplorerItem>());
        }

        NativeItemRecord[] records = ArrayPool<NativeItemRecord>.Shared.Rent((int)actualCount);
        byte[] text = ArrayPool<byte>.Shared.Rent((int)Math.Max(utf8Bytes, 1));
        try
        {
            uint writtenCount;
            uint writtenBytes;
            fixed (NativeItemRecord* recordPointer = records)
            fixed (byte* textPointer = text)
            {
                NativeStatusExtensions.ThrowIfFailed(
                    NativeMethods.SnapshotGetRange(
                        handle.DangerousGetHandle(),
                        start,
                        count,
                        recordPointer,
                        actualCount,
                        textPointer,
                        utf8Bytes,
                        out writtenCount,
                        out writtenBytes),
                    "fe_snapshot_get_range");
            }

            if (writtenCount != actualCount || writtenBytes != utf8Bytes)
            {
                throw new NativeInteropException("fe_snapshot_get_range returned inconsistent output lengths.");
            }

            var items = new ExplorerItem[writtenCount];
            for (int index = 0; index < writtenCount; index++)
            {
                items[index] = ConvertRecord(records[index], text, writtenBytes);
            }
            return new ExplorerItemBatch(start, items);
        }
        finally
        {
            ArrayPool<NativeItemRecord>.Shared.Return(records, clearArray: false);
            ArrayPool<byte>.Shared.Return(text, clearArray: false);
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        handle.Dispose();
        GC.SuppressFinalize(this);
    }

    private static ExplorerItem ConvertRecord(NativeItemRecord record, byte[] text, uint textLength)
    {
        const uint sizePresent = 1;
        if ((record.Flags & ~sizePresent) != 0 || (record.Flags & sizePresent) == 0 && record.Size != 0)
        {
            throw new NativeInteropException("fe_snapshot_get_range returned invalid item flags.");
        }

        ExplorerItemKind kind = record.Kind switch
        {
            1 => ExplorerItemKind.File,
            2 => ExplorerItemKind.Directory,
            _ => throw new NativeInteropException($"fe_snapshot_get_range returned unknown item kind {record.Kind}."),
        };
        ulong end = checked((ulong)record.NameOffset + record.NameLength);
        if (end > textLength || end > (ulong)text.Length)
        {
            throw new NativeInteropException("fe_snapshot_get_range returned an out-of-bounds name span.");
        }

        string name;
        try
        {
            name = StrictUtf8.GetString(text, (int)record.NameOffset, (int)record.NameLength);
        }
        catch (Exception exception) when (exception is DecoderFallbackException or ArgumentException or OverflowException)
        {
            throw new NativeInteropException("fe_snapshot_get_range returned invalid UTF-8.", exception);
        }

        DateTimeOffset modified;
        try
        {
            modified = DateTimeOffset.FromUnixTimeMilliseconds(record.ModifiedUnixMs);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            throw new NativeInteropException("fe_snapshot_get_range returned an invalid timestamp.", exception);
        }

        return new ExplorerItem(record.ItemId, name, modified, (record.Flags & sizePresent) != 0 ? record.Size : null, kind);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
    }
}
