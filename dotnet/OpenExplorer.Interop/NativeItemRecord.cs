using System.Runtime.InteropServices;

namespace OpenExplorer.Interop;

[StructLayout(LayoutKind.Sequential)]
internal struct NativeItemRecord
{
    internal ulong ItemId;
    internal long ModifiedUnixMs;
    internal ulong Size;
    internal uint NameOffset;
    internal uint NameLength;
    internal uint Kind;
    internal uint Flags;
}
