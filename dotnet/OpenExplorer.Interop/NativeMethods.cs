using System.Runtime.InteropServices;

namespace OpenExplorer.Interop;

internal static partial class NativeMethods
{
    [LibraryImport("open_explorer_ffi", EntryPoint = "fe_api_version")]
    internal static partial uint ApiVersion();

    [LibraryImport("open_explorer_ffi", EntryPoint = "fe_engine_create")]
    internal static partial nint EngineCreate();

    [LibraryImport("open_explorer_ffi", EntryPoint = "fe_engine_destroy")]
    internal static partial void EngineDestroy(nint handle);

    [LibraryImport("open_explorer_ffi", EntryPoint = "fe_engine_create_synthetic_snapshot")]
    internal static unsafe partial uint CreateSyntheticSnapshot(nint engine, ulong itemCount, nint* output);

    [LibraryImport("open_explorer_ffi", EntryPoint = "fe_engine_open_local_directory_snapshot")]
    internal static unsafe partial uint OpenLocalDirectorySnapshot(nint engine, byte* path, uint pathLength, nint* output);

    [LibraryImport("open_explorer_ffi", EntryPoint = "fe_snapshot_count")]
    internal static partial uint SnapshotCount(nint snapshot, out ulong count);

    [LibraryImport("open_explorer_ffi", EntryPoint = "fe_snapshot_get_range_requirements")]
    internal static partial uint SnapshotGetRangeRequirements(
        nint snapshot,
        ulong start,
        uint requestedCount,
        out uint actualCount,
        out uint utf8Bytes);

    [LibraryImport("open_explorer_ffi", EntryPoint = "fe_snapshot_get_range")]
    internal static unsafe partial uint SnapshotGetRange(
        nint snapshot,
        ulong start,
        uint requestedCount,
        NativeItemRecord* records,
        uint recordCapacity,
        byte* utf8Buffer,
        uint utf8Capacity,
        out uint actualCount,
        out uint utf8Bytes);

    [LibraryImport("open_explorer_ffi", EntryPoint = "fe_snapshot_destroy")]
    internal static partial void SnapshotDestroy(nint snapshot);
}
