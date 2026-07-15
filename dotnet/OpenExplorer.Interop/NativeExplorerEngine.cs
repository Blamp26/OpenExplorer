using OpenExplorer.Contracts;

namespace OpenExplorer.Interop;

public sealed class NativeExplorerEngine : IExplorerEngine, IDiagnosticSnapshotFactory, ILocationSnapshotFactory, ILocationHierarchy, IExplorerSnapshotViewFactory, ILocationAddressResolver
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

    public ExplorerLocation ParseAddress(string input)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (input is null) throw new ArgumentNullException(nameof(input));
        string value = input.Trim();
        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"') value = value[1..^1].Trim();
        if (value.Length == 0 || value.Contains('"')) throw new ArgumentException("Enter an absolute Windows folder path.", nameof(input));
        value = Environment.ExpandEnvironmentVariables(value).Trim();
        if (!Path.IsPathFullyQualified(value)) throw new ArgumentException("Enter an absolute Windows folder path.", nameof(input));
        try
        {
            string fullPath = Path.GetFullPath(value);
            if (string.IsNullOrWhiteSpace(Path.GetPathRoot(fullPath))) throw new ArgumentException("Enter an absolute Windows folder path.", nameof(input));
            return ExplorerLocation.File(fullPath);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            throw new ArgumentException("Enter a valid absolute Windows folder path.", nameof(input), exception);
        }
    }

    public IReadOnlyList<ExplorerBreadcrumb> GetBreadcrumbs(ExplorerLocation location)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureFileLocation(location);
        string path = GetAbsolutePath(location.Identifier);
        string root = Path.GetPathRoot(path) ?? throw new InvalidOperationException("The file location has no filesystem root.");
        var result = new List<ExplorerBreadcrumb>();
        string trimmed = TrimTrailingSeparators(path);
        string rootTrimmed = TrimTrailingSeparators(root);
        if (root.StartsWith("\\\\", StringComparison.Ordinal))
        {
            // Path.GetPathRoot already contains the server and share. Keep that
            // pair as one usable ancestor; navigating to only \\server is not a
            // directory location and must never become a breadcrumb target.
            result.Add(new ExplorerBreadcrumb(rootTrimmed, ExplorerLocation.File(rootTrimmed + "\\"), trimmed.Equals(rootTrimmed, StringComparison.OrdinalIgnoreCase)));
            string uncRemainder = trimmed.Length > rootTrimmed.Length ? trimmed[rootTrimmed.Length..] : string.Empty;
            string[] parts = uncRemainder.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);
            string current = rootTrimmed;
            for (int i = 0; i < parts.Length; i++)
            {
                current = current.TrimEnd('\\') + "\\" + parts[i];
                result.Add(new ExplorerBreadcrumb(parts[i], ExplorerLocation.File(current), i == parts.Length - 1));
            }
            return result;
        }

        result.Add(new ExplorerBreadcrumb(root, ExplorerLocation.File(root), string.Equals(trimmed, rootTrimmed, StringComparison.OrdinalIgnoreCase)));
        string remainder = trimmed.Length > root.Length ? trimmed[root.Length..] : string.Empty;
        string currentPath = root;
        string[] segments = remainder.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < segments.Length; i++)
        {
            currentPath = currentPath.TrimEnd('\\') + "\\" + segments[i];
            result.Add(new ExplorerBreadcrumb(segments[i], ExplorerLocation.File(currentPath), i == segments.Length - 1));
        }
        return result;
    }

    public IExplorerSnapshot CreateSortedView(IExplorerSnapshot source, ExplorerSortOptions options)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(options);
        if (source is not NativeExplorerSnapshot nativeSource)
        {
            throw new NotSupportedException("Sorted native views require a snapshot created by this native engine.");
        }

        uint field = options.Field switch
        {
            ExplorerSortField.Name => 1u,
            ExplorerSortField.DateModified => 2u,
            ExplorerSortField.Type => 3u,
            ExplorerSortField.Size => 4u,
            _ => throw new ArgumentOutOfRangeException(nameof(options)),
        };
        uint direction = options.Direction switch
        {
            ExplorerSortDirection.Ascending => 1u,
            ExplorerSortDirection.Descending => 2u,
            _ => throw new ArgumentOutOfRangeException(nameof(options)),
        };
        uint flags = options.FoldersFirst ? 1u : 0u;
        unsafe
        {
            nint view = 0;
            NativeStatusExtensions.ThrowIfFailed(
                NativeMethods.CreateSortedView(nativeSource.GetHandle(), field, direction, flags, &view),
                "fe_snapshot_create_sorted_view");
            if (view == 0) throw new NativeInteropException("fe_snapshot_create_sorted_view returned a null handle.");
            return new NativeExplorerSnapshot(new SafeSnapshotHandle(view));
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
