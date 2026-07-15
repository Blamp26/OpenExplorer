using OpenExplorer.Application.Diagnostics;
using OpenExplorer.Application.Selection;
using OpenExplorer.Contracts;

namespace OpenExplorer.Application.Navigation;

public sealed class ExplorerNavigationController : IDisposable
{
    private readonly ILocationSnapshotFactory snapshotFactory;
    private readonly ILocationHierarchy hierarchy;
    private readonly IExplorerSnapshotViewFactory viewFactory;
    private readonly List<ExplorerLocation> backHistory = [];
    private readonly List<ExplorerLocation> forwardHistory = [];
    private readonly HashSet<IExplorerSnapshot> inUseSnapshots = [];
    private readonly HashSet<IExplorerSnapshot> pendingSnapshotDisposals = [];
    private SnapshotFileItemList? currentItems;
    private IExplorerSnapshot? currentBaseSnapshot;
    private ExplorerLocation? currentLocation;
    private ExplorerSortOptions currentSortOptions = ExplorerSortOptions.Default;
    private string? errorMessage;
    private long generation;
    private bool isBusy;
    private bool disposed;

    public ExplorerNavigationController(
        ILocationSnapshotFactory snapshotFactory,
        ILocationHierarchy hierarchy,
        IExplorerSnapshotViewFactory viewFactory)
    {
        this.snapshotFactory = snapshotFactory ?? throw new ArgumentNullException(nameof(snapshotFactory));
        this.hierarchy = hierarchy ?? throw new ArgumentNullException(nameof(hierarchy));
        this.viewFactory = viewFactory ?? throw new ArgumentNullException(nameof(viewFactory));
    }

    public event EventHandler? StateChanged;

    public ExplorerSelectionModel Selection { get; } = new();

    public ExplorerLocation? CurrentLocation { get { ThrowIfDisposed(); return currentLocation; } }
    public SnapshotFileItemList? CurrentItems { get { ThrowIfDisposed(); return currentItems; } }
    public ExplorerSortOptions CurrentSortOptions { get { ThrowIfDisposed(); return currentSortOptions; } }
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
        if (currentLocation is not null || currentItems is not null || currentBaseSnapshot is not null)
        {
            throw new InvalidOperationException("The navigation controller has already been initialized.");
        }
        return StartNavigationAsync(location, NavigationKind.Initialize);
    }

    public Task NavigateToAsync(ExplorerLocation location)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(location);
        return StartNavigationAsync(location, NavigationKind.Normal);
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
            return OpenAndApplyNavigationAsync(location, NavigationKind.Normal, request);
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
        if (backHistory.Count == 0) return Task.CompletedTask;
        long request = BeginRequest();
        return OpenAndApplyNavigationAsync(backHistory[^1], NavigationKind.Back, request);
    }

    public Task GoForwardAsync()
    {
        ThrowIfDisposed();
        if (forwardHistory.Count == 0) return Task.CompletedTask;
        long request = BeginRequest();
        return OpenAndApplyNavigationAsync(forwardHistory[^1], NavigationKind.Forward, request);
    }

    public Task GoUpAsync()
    {
        ThrowIfDisposed();
        if (currentLocation is null || !TryGetParent(currentLocation, out ExplorerLocation parent)) return Task.CompletedTask;
        long request = BeginRequest();
        return OpenAndApplyNavigationAsync(parent, NavigationKind.Normal, request);
    }

    public Task ApplySortAsync(ExplorerSortOptions options)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(options);
        if (currentBaseSnapshot is null || currentItems is null || options == currentSortOptions)
        {
            return Task.CompletedTask;
        }

        long request = BeginRequest();
        IExplorerSnapshot baseSnapshot = currentBaseSnapshot;
        return OpenAndApplySortAsync(baseSnapshot, options, request);
    }

    public Task RefreshAsync()
    {
        ThrowIfDisposed();
        if (currentLocation is null || currentBaseSnapshot is null || currentItems is null)
        {
            return Task.CompletedTask;
        }

        long request = BeginRequest();
        ExplorerLocation location = currentLocation;
        ExplorerSortOptions sortOptions = currentSortOptions;
        return OpenAndApplyRefreshAsync(location, sortOptions, request);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        generation++;
        currentItems?.Dispose();
        currentItems = null;
        IExplorerSnapshot? baseSnapshot = currentBaseSnapshot;
        currentBaseSnapshot = null;
        DisposeOwnedSnapshot(baseSnapshot);
        backHistory.Clear();
        forwardHistory.Clear();
        Selection.Dispose();
        isBusy = false;
        GC.SuppressFinalize(this);
    }

    private Task StartNavigationAsync(ExplorerLocation location, NavigationKind kind)
    {
        long request = BeginRequest();
        ExplorerSortOptions sortOptions = currentSortOptions;
        return OpenAndApplyNavigationAsync(location, kind, request, sortOptions);
    }

    private Task OpenAndApplyNavigationAsync(ExplorerLocation location, NavigationKind kind, long request)
        => OpenAndApplyNavigationAsync(location, kind, request, currentSortOptions);

    private async Task OpenAndApplyNavigationAsync(ExplorerLocation location, NavigationKind kind, long request, ExplorerSortOptions sortOptions)
    {
        IExplorerSnapshot? baseSnapshot = null;
        SnapshotFileItemList? replacement = null;
        try
        {
            OpenedSnapshots opened = await Task.Run(() => OpenAndSort(location, sortOptions)).ConfigureAwait(true);
            baseSnapshot = opened.BaseSnapshot;
            replacement = new SnapshotFileItemList(opened.SortedView);

            if (IsStale(request))
            {
                replacement.Dispose();
                baseSnapshot.Dispose();
                return;
            }

            SnapshotFileItemList? oldItems = currentItems;
            IExplorerSnapshot? oldBase = currentBaseSnapshot;
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
            currentBaseSnapshot = baseSnapshot;
            baseSnapshot = null;
            currentItems = replacement;
            replacement = null;
            currentSortOptions = sortOptions;
            Selection.SetLogicalItemCount(currentItems.LogicalItemCount);
            Selection.Clear();
            errorMessage = null;
            isBusy = false;
            StateChanged?.Invoke(this, EventArgs.Empty);
            oldItems?.Dispose();
            DisposeOwnedSnapshot(oldBase);
        }
        catch (Exception exception)
        {
            replacement?.Dispose();
            baseSnapshot?.Dispose();
            if (!IsStale(request)) Fail(request, exception);
        }
    }

    private async Task OpenAndApplySortAsync(IExplorerSnapshot baseSnapshot, ExplorerSortOptions options, long request)
    {
        SnapshotFileItemList? replacement = null;
        IExplorerSnapshot? sortedView = null;
        try
        {
            AcquireSnapshot(baseSnapshot);
            try
            {
                sortedView = await Task.Run(() => viewFactory.CreateSortedView(baseSnapshot, options)).ConfigureAwait(true);
            }
            finally
            {
                ReleaseSnapshot(baseSnapshot);
            }
            replacement = new SnapshotFileItemList(sortedView ?? throw new InvalidOperationException("The snapshot view factory returned null."));
            sortedView = null;
            if (IsStale(request))
            {
                replacement.Dispose();
                return;
            }

            SnapshotFileItemList? oldItems = currentItems;
            currentItems = replacement;
            replacement = null;
            currentSortOptions = options;
            errorMessage = null;
            isBusy = false;
            StateChanged?.Invoke(this, EventArgs.Empty);
            oldItems?.Dispose();
        }
        catch (Exception exception)
        {
            replacement?.Dispose();
            sortedView?.Dispose();
            if (!IsStale(request)) Fail(request, exception);
        }
    }

    private async Task OpenAndApplyRefreshAsync(ExplorerLocation location, ExplorerSortOptions sortOptions, long request)
    {
        IExplorerSnapshot? baseSnapshot = null;
        SnapshotFileItemList? replacement = null;
        try
        {
            OpenedSnapshots opened = await Task.Run(() => OpenAndSort(location, sortOptions)).ConfigureAwait(true);
            baseSnapshot = opened.BaseSnapshot;
            replacement = new SnapshotFileItemList(opened.SortedView);

            if (IsStale(request))
            {
                replacement.Dispose();
                baseSnapshot.Dispose();
                return;
            }

            // Reconcile before publishing. It performs only native ID lookups and
            // cannot alter the current view if opening or reconciliation fails.
            Selection.Reconcile(opened.SortedView);

            SnapshotFileItemList? oldItems = currentItems;
            IExplorerSnapshot? oldBase = currentBaseSnapshot;
            currentBaseSnapshot = baseSnapshot;
            baseSnapshot = null;
            currentItems = replacement;
            replacement = null;
            currentSortOptions = sortOptions;
            errorMessage = null;
            isBusy = false;
            StateChanged?.Invoke(this, EventArgs.Empty);
            oldItems?.Dispose();
            DisposeOwnedSnapshot(oldBase);
        }
        catch (Exception exception)
        {
            replacement?.Dispose();
            baseSnapshot?.Dispose();
            if (!IsStale(request)) Fail(request, exception);
        }
    }

    private OpenedSnapshots OpenAndSort(ExplorerLocation location, ExplorerSortOptions options)
    {
        IExplorerSnapshot baseSnapshot = snapshotFactory.OpenSnapshot(location);
        try
        {
            IExplorerSnapshot sortedView = viewFactory.CreateSortedView(baseSnapshot, options);
            return new OpenedSnapshots(baseSnapshot, sortedView);
        }
        catch
        {
            baseSnapshot.Dispose();
            throw;
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

    private void AcquireSnapshot(IExplorerSnapshot snapshot) => inUseSnapshots.Add(snapshot);

    private void ReleaseSnapshot(IExplorerSnapshot snapshot)
    {
        if (!inUseSnapshots.Remove(snapshot)) return;
        if (pendingSnapshotDisposals.Remove(snapshot)) snapshot.Dispose();
    }

    private void DisposeOwnedSnapshot(IExplorerSnapshot? snapshot)
    {
        if (snapshot is null) return;
        if (inUseSnapshots.Contains(snapshot)) pendingSnapshotDisposals.Add(snapshot);
        else snapshot.Dispose();
    }

    private bool TryGetParent(ExplorerLocation location, out ExplorerLocation parent)
    {
        try { return hierarchy.TryGetParent(location, out parent!); }
        catch (NotSupportedException) { parent = null!; return false; }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(disposed, this);

    private enum NavigationKind { Initialize, Normal, Back, Forward }

    private sealed record OpenedSnapshots(IExplorerSnapshot BaseSnapshot, IExplorerSnapshot SortedView);
}
