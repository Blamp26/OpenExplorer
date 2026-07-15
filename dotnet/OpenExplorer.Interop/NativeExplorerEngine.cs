using OpenExplorer.Contracts;

namespace OpenExplorer.Interop;

public sealed class NativeExplorerEngine : IExplorerEngine, IDiagnosticSnapshotFactory, ILocationSnapshotFactory, ILocationHierarchy
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

    public bool TryGetParent(ExplorerLocation location, out ExplorerLocation parent)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(location);
        EnsureFileLocation(location);
        string fullPath = GetAbsolutePath(location.Identifier);
        string root = Path.GetPathRoot(fullPath) ?? throw new InvalidOperationException("The file location has no filesystem root.");
        string trimmedPath = TrimTrailingSeparators(fullPath);
        if (string.Equals(trimmedPath, TrimTrailingSeparators(root), StringComparison.OrdinalIgnoreCase))
        {
            parent = null!;
            return false;
        }

        string? parentPath = Path.GetDirectoryName(trimmedPath);
        if (string.IsNullOrEmpty(parentPath))
        {
            parent = null!;
            return false;
        }

        parent = ExplorerLocation.File(parentPath);
        return true;
    }

    public ExplorerLocation ResolveChild(ExplorerLocation parent, ExplorerItem child)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(child);
        EnsureFileLocation(parent);
        if (child.Kind != ExplorerItemKind.Directory)
        {
            throw new InvalidOperationException("Only directory items can be opened as locations.");
        }
        if (string.IsNullOrEmpty(child.Name) || child.Name is "." or ".." || child.Name.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]) >= 0 || Path.IsPathRooted(child.Name))
        {
            throw new ArgumentException("The directory item name is not a safe direct child name.", nameof(child));
        }

        string parentPath = GetAbsolutePath(parent.Identifier);
        string childPath = Path.GetFullPath(Path.Combine(parentPath, child.Name));
        string? resolvedParent = Path.GetDirectoryName(TrimTrailingSeparators(childPath));
        if (resolvedParent is null || !string.Equals(TrimTrailingSeparators(resolvedParent), TrimTrailingSeparators(parentPath), StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("The directory item does not resolve to a direct child.", nameof(child));
        }
        return ExplorerLocation.File(childPath);
    }

    private static void EnsureFileLocation(ExplorerLocation location)
    {
        if (location.Scheme != ExplorerLocationScheme.File)
        {
            throw new NotSupportedException($"Location scheme {location.Scheme} is not implemented.");
        }
    }

    private static string GetAbsolutePath(string identifier)
    {
        if (!Path.IsPathFullyQualified(identifier))
        {
            throw new ArgumentException("A file location must use an absolute path.", nameof(identifier));
        }
        return Path.GetFullPath(identifier);
    }

    private static string TrimTrailingSeparators(string path)
    {
        string root = Path.GetPathRoot(path) ?? string.Empty;
        if (path.Length <= root.Length) return root;
        return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
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
