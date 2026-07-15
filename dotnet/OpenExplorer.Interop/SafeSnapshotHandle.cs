using Microsoft.Win32.SafeHandles;

namespace OpenExplorer.Interop;

internal sealed class SafeSnapshotHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    private SafeSnapshotHandle() : base(ownsHandle: true)
    {
    }

    internal SafeSnapshotHandle(nint handle) : base(ownsHandle: true)
    {
        SetHandle(handle);
    }

    protected override bool ReleaseHandle()
    {
        NativeMethods.SnapshotDestroy(handle);
        return true;
    }
}
