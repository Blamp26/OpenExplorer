using FastExplorer.Contracts;

namespace FastExplorer.Interop;

public sealed class NativeExplorerEngine : IExplorerEngine
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
