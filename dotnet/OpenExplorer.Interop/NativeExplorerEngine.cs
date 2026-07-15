using OpenExplorer.Contracts;

namespace OpenExplorer.Interop;

public sealed class NativeExplorerEngine : IExplorerEngine, IDiagnosticSnapshotFactory, ILocationSnapshotFactory
{
    private readonly SafeEngineHandle _handle;
    private bool _disposed;

    public NativeExplorerEngine()
    {
        var nativeHandle = NativeMethods.EngineCreate();
        if (nativeHandle == 0)
        {
            throw new NativeInteropException("The native explorer engine could not be created.");
        }

        _handle = new SafeEngineHandle(nativeHandle);
        try
        {
            ApiVersion = NativeMethods.ApiVersion();
        }
        catch
        {
            _handle.Dispose();
            throw;
        }
    }

    public uint ApiVersion { get; }

    public IExplorerSnapshot CreateSyntheticSnapshot(ulong itemCount)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (itemCount == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(itemCount));
        }

        unsafe
        {
            nint snapshot = 0;
            NativeStatusExtensions.ThrowIfFailed(
                NativeMethods.CreateSyntheticSnapshot(_handle.DangerousGetHandle(), itemCount, &snapshot),
                "fe_engine_create_synthetic_snapshot");
            if (snapshot == 0)
            {
                throw new NativeInteropException("fe_engine_create_synthetic_snapshot returned a null handle.");
            }
            return new NativeExplorerSnapshot(new SafeSnapshotHandle(snapshot));
        }
    }

    public IExplorerSnapshot OpenSnapshot(ExplorerLocation location)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(location);
        if (location.Scheme != ExplorerLocationScheme.File)
        {
            throw new NotSupportedException($"Location scheme {location.Scheme} is not implemented.");
        }

        byte[] path = System.Text.Encoding.UTF8.GetBytes(location.Identifier);
        unsafe
        {
            nint snapshot = 0;
            fixed (byte* pathPointer = path)
            {
                NativeStatusExtensions.ThrowIfFailed(
                    NativeMethods.OpenLocalDirectorySnapshot(_handle.DangerousGetHandle(), pathPointer, checked((uint)path.Length), &snapshot),
                    "fe_engine_open_local_directory_snapshot");
            }
            if (snapshot == 0) throw new NativeInteropException("fe_engine_open_local_directory_snapshot returned a null handle.");
            return new NativeExplorerSnapshot(new SafeSnapshotHandle(snapshot));
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _handle.Dispose();
        GC.SuppressFinalize(this);
    }
}
