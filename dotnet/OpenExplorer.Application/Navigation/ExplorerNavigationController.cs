using OpenExplorer.Application.Diagnostics;
using OpenExplorer.Contracts;

namespace OpenExplorer.Application.Navigation;

public sealed class ExplorerNavigationController : IDisposable
{
    private readonly ILocationSnapshotFactory snapshotFactory;
    private readonly ILocationHierarchy hierarchy;
    private readonly List<ExplorerLocation> backHistory = [];
    private readonly List<ExplorerLocation> forwardHistory = [];
    private SnapshotFileItemList? currentItems;
    private ExplorerLocation? currentLocation;
    private string? errorMessage;
    private long generation;
    private bool isBusy;
    private bool disposed;

    public ExplorerNavigationController(ILocationSnapshotFactory snapshotFactory, ILocationHierarchy hierarchy)
    {
        this.snapshotFactory = snapshotFactory ?? throw new ArgumentNullException(nameof(snapshotFactory));
        this.hierarchy = hierarchy ?? throw new ArgumentNullException(nameof(hierarchy));
    }

    public event EventHandler? StateChanged;

    public ExplorerLocation? CurrentLocation { get { ThrowIfDisposed(); return currentLocation; } }

    public SnapshotFileItemList? CurrentItems { get { ThrowIfDisposed(); return currentItems; } }

    public bool CanGoBack { get { ThrowIfDisposed(); return backHistory.Count > 0; } }

    public bool CanGoForward { get { ThrowIfDisposed(); return forwardHistory.Count > 0; } }

    public bool CanGoUp
    {
        get
        {
            ThrowIfDisposed();
            return currentLocation is not null && TryGetParent(currentLocation, out _);
        }
    }

    public bool IsBusy { get { ThrowIfDisposed(); return isBusy; } }

    public string? ErrorMessage { get { ThrowIfDisposed(); return errorMessage; } }

    public Task InitializeAsync(ExplorerLocation location)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(location);
        if (currentLocation is not null || currentItems is not null)
        {
            throw new InvalidOperationException("The navigation controller has already been initialized.");
        }
        return StartOpenAsync(location, NavigationKind.Initialize);
    }

    public Task NavigateToAsync(ExplorerLocation location)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(location);
        return StartOpenAsync(location, NavigationKind.Normal);
    }

    public Task NavigateIntoAsync(ExplorerItem child)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(child);
        if (child.Kind != ExplorerItemKind.Directory || currentLocation is null)
        {
            return Task.CompletedTask;
        }

        long request = BeginRequest();
        try
        {
            ExplorerLocation location = hierarchy.ResolveChild(currentLocation, child);
            return OpenAndApplyAsync(location, NavigationKind.Normal, request);
        }
        catch (Exception exception)
        {
            Fail(request, exception);
            return Task.CompletedTask;
        }
    }

    public Task GoBackAsync()
    {
        ThrowIfDisposed();
        if (backHistory.Count == 0)
        {
            return Task.CompletedTask;
        }
        long request = BeginRequest();
        return OpenAndApplyAsync(backHistory[^1], NavigationKind.Back, request);
    }

    public Task GoForwardAsync()
    {
        ThrowIfDisposed();
        if (forwardHistory.Count == 0)
        {
            return Task.CompletedTask;
        }
        long request = BeginRequest();
        return OpenAndApplyAsync(forwardHistory[^1], NavigationKind.Forward, request);
    }

    public Task GoUpAsync()
    {
        ThrowIfDisposed();
        if (currentLocation is null || !TryGetParent(currentLocation, out ExplorerLocation parent))
        {
            return Task.CompletedTask;
        }
        long request = BeginRequest();
        return OpenAndApplyAsync(parent, NavigationKind.Normal, request);
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        generation++;
        currentItems?.Dispose();
        currentItems = null;
        backHistory.Clear();
        forwardHistory.Clear();
        isBusy = false;
        GC.SuppressFinalize(this);
    }

    private Task StartOpenAsync(ExplorerLocation location, NavigationKind kind)
    {
        long request = BeginRequest();
        return OpenAndApplyAsync(location, kind, request);
    }

    private async Task OpenAndApplyAsync(ExplorerLocation location, NavigationKind kind, long request)
    {
        IExplorerSnapshot? snapshot = null;
        SnapshotFileItemList? replacement = null;
        try
        {
            snapshot = await Task.Run(() => snapshotFactory.OpenSnapshot(location)).ConfigureAwait(true);
            replacement = new SnapshotFileItemList(snapshot);
            snapshot = null;

            if (IsStale(request))
            {
                replacement.Dispose();
                return;
            }

            SnapshotFileItemList? oldItems = currentItems;
            ExplorerLocation? oldLocation = currentLocation;
            switch (kind)
            {
                case NavigationKind.Normal:
                    if (oldLocation is not null) backHistory.Add(oldLocation);
                    forwardHistory.Clear();
                    break;
                case NavigationKind.Back:
                    backHistory.RemoveAt(backHistory.Count - 1);
                    if (oldLocation is not null) forwardHistory.Add(oldLocation);
                    break;
                case NavigationKind.Forward:
                    forwardHistory.RemoveAt(forwardHistory.Count - 1);
                    if (oldLocation is not null) backHistory.Add(oldLocation);
                    break;
            }

            currentLocation = location;
            currentItems = replacement;
            replacement = null;
            errorMessage = null;
            isBusy = false;
            StateChanged?.Invoke(this, EventArgs.Empty);
            oldItems?.Dispose();
        }
        catch (Exception exception)
        {
            replacement?.Dispose();
            snapshot?.Dispose();
            if (!IsStale(request)) Fail(request, exception);
        }
    }

    private long BeginRequest()
    {
        long request = ++generation;
        isBusy = true;
        errorMessage = null;
        StateChanged?.Invoke(this, EventArgs.Empty);
        return request;
    }

    private void Fail(long request, Exception exception)
    {
        if (request != generation || disposed) return;
        errorMessage = exception.Message;
        isBusy = false;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private bool IsStale(long request) => disposed || request != generation;

    private bool TryGetParent(ExplorerLocation location, out ExplorerLocation parent)
    {
        try { return hierarchy.TryGetParent(location, out parent!); }
        catch (NotSupportedException) { parent = null!; return false; }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(disposed, this);

    private enum NavigationKind { Initialize, Normal, Back, Forward }
}
