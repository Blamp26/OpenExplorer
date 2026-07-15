using System.Runtime.InteropServices;

namespace FastExplorer.Interop;

internal static partial class NativeMethods
{
    [LibraryImport("fast_explorer_ffi", EntryPoint = "fe_api_version")]
    internal static partial uint ApiVersion();

    [LibraryImport("fast_explorer_ffi", EntryPoint = "fe_engine_create")]
    internal static partial nint EngineCreate();

    [LibraryImport("fast_explorer_ffi", EntryPoint = "fe_engine_destroy")]
    internal static partial void EngineDestroy(nint handle);
}
