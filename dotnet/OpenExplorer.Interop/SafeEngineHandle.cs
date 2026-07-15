using Microsoft.Win32.SafeHandles;

namespace OpenExplorer.Interop;

internal sealed class SafeEngineHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    private SafeEngineHandle() : base(ownsHandle: true)
    {
    }

    internal SafeEngineHandle(nint handle) : base(ownsHandle: true)
    {
        SetHandle(handle);
    }

    protected override bool ReleaseHandle()
    {
        NativeMethods.EngineDestroy(handle);
        return true;
    }
}
