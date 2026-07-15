using System.Diagnostics;
using OpenExplorer.Application.Diagnostics;
using OpenExplorer.Application.Navigation;
using OpenExplorer.Contracts;
using OpenExplorer.Interop;

try
{
    RunNativeSortingChecks();
    await RunControllerSortingChecksAsync();
    Console.WriteLine("Sorting model: native views and controller sort transitions passed");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"Sorting smoke test failed: {exception.Message}");
    return 1;
}

static void RunNativeSortingChecks()
{
    string root = Path.Combine(Path.GetTempPath(), $"OpenExplorer-Sorting-{Environment.ProcessId}-{Stopwatch.GetTimestamp()}");
    try
    {
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.Combine(root, "Archive"));
        Directory.CreateDirectory(Path.Combine(root, "Zulu"));
        Directory.SetLastWriteTimeUtc(Path.Combine(root, "Archive"), new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        Directory.SetLastWriteTimeUtc(Path.Combine(root, "Zulu"), new DateTime(2025, 1, 2, 0, 0, 0, DateTimeKind.Utc));
        WriteFile(root, "alpha.txt", 4);
        WriteFile(root, "Bravo.TXT", 8);
        WriteFile(root, "file2.txt", 2);
        WriteFile(root, "file10.txt", 10);
        WriteFile(root, "extensionless", 1);
        WriteFile(root, "zero.bin", 0);
        WriteFile(root, "large.bin", 30);
        WriteFile(root, "older.pdf", 6);
        WriteFile(root, "newer.jpg", 7);
        WriteFile(root, "Δelta.txt", 5);
        foreach (string name in new[] { "alpha.txt", "Bravo.TXT", "file2.txt", "file10.txt", "extensionless", "zero.bin", "large.bin", "older.pdf", "newer.jpg", "Δelta.txt" })
        {
            File.SetLastWriteTimeUtc(Path.Combine(root, name), new DateTime(2021, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        }
        File.SetLastWriteTimeUtc(Path.Combine(root, "older.pdf"), new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        File.SetLastWriteTimeUtc(Path.Combine(root, "newer.jpg"), new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        using var engine = new NativeExplorerEngine();
        Assert(engine.ApiVersion == 5, $"Expected API version 5, got {engine.ApiVersion}.");
        using IExplorerSnapshot source = engine.OpenSnapshot(ExplorerLocation.File(Path.GetFullPath(root)));
        ExplorerItem[] original = source.GetRange(0, 4096).Items.ToArray();
        Assert(original.Length == 12, "The sorting fixture did not contain the expected entries.");

        using IExplorerSnapshot nameAsc = engine.CreateSortedView(source, new ExplorerSortOptions(ExplorerSortField.Name, ExplorerSortDirection.Ascending, true));
        using IExplorerSnapshot nameDesc = engine.CreateSortedView(source, new ExplorerSortOptions(ExplorerSortField.Name, ExplorerSortDirection.Descending, true));
        using IExplorerSnapshot dateAsc = engine.CreateSortedView(source, new ExplorerSortOptions(ExplorerSortField.DateModified, ExplorerSortDirection.Ascending, false));
        using IExplorerSnapshot dateDesc = engine.CreateSortedView(source, new ExplorerSortOptions(ExplorerSortField.DateModified, ExplorerSortDirection.Descending, false));
        using IExplorerSnapshot typeAsc = engine.CreateSortedView(source, new ExplorerSortOptions(ExplorerSortField.Type, ExplorerSortDirection.Ascending, false));
        using IExplorerSnapshot typeDesc = engine.CreateSortedView(source, new ExplorerSortOptions(ExplorerSortField.Type, ExplorerSortDirection.Descending, false));
        using IExplorerSnapshot sizeAsc = engine.CreateSortedView(source, new ExplorerSortOptions(ExplorerSortField.Size, ExplorerSortDirection.Ascending, false));
        using IExplorerSnapshot sizeDesc = engine.CreateSortedView(source, new ExplorerSortOptions(ExplorerSortField.Size, ExplorerSortDirection.Descending, false));

        Assert(Name(nameAsc)[0] == "Archive" && Name(nameAsc)[1] == "Zulu", "Folders-first ascending name order failed.");
        Assert(Name(nameDesc)[0] == "Zulu" && Name(nameDesc)[1] == "Archive", "Folders-first descending name order failed.");
        Assert(Name(dateAsc)[0] == "older.pdf" && Name(dateDesc)[0] == "Zulu", $"Date ordering failed: asc={string.Join(",", Name(dateAsc))}; desc={string.Join(",", Name(dateDesc))}.");
        Assert(Name(typeAsc)[0] == "extensionless" && Name(typeDesc)[0].EndsWith(".txt", StringComparison.OrdinalIgnoreCase), "Type ordering failed.");
        Assert(Name(sizeAsc)[0] == "zero.bin" && Name(sizeDesc)[0] == "large.bin", "Size ordering failed.");
        Assert(Name(sizeAsc)[^1] == "Zulu" && Name(sizeDesc)[^1] == "Zulu", "Missing-size placement failed.");
        Assert(source.GetRange(0, 4096).Items.SequenceEqual(original), "The source snapshot was mutated.");

        ExplorerItem unicode = original.Single(item => item.Name == "Δelta.txt");
        Assert(Name(nameAsc).Contains(unicode.Name), "Unicode name was not preserved.");
        source.Dispose();
        Assert(nameAsc.GetRange(0, 1).Items[0].Name == "Archive", "Sorted view did not survive source disposal.");
        engine.Dispose();
        Assert(nameDesc.GetRange(0, 1).Items[0].Name == "Zulu", "Sorted view did not survive engine disposal.");
    }
    finally
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
}

static async Task RunControllerSortingChecksAsync()
{
    var factory = new TrackingFactory();
    using var controller = new ExplorerNavigationController(factory, factory, factory);
    ExplorerLocation a = ExplorerLocation.File("A");
    ExplorerLocation b = ExplorerLocation.File("B");
    await controller.InitializeAsync(a);
    Assert(factory.OpenCount == 1 && factory.ViewCount == 1, "Initialization did not create one base and one view.");
    Assert(controller.CurrentSortOptions == ExplorerSortOptions.Default, "The default sort policy was incorrect.");

    await controller.ApplySortAsync(new ExplorerSortOptions(ExplorerSortField.Size, ExplorerSortDirection.Descending, false));
    Assert(factory.OpenCount == 1 && factory.ViewCount == 2, "Sort reopened the location or did not create one view.");
    Assert(controller.CurrentLocation == a && !controller.CanGoBack, "Sort changed location history.");
    Assert(factory.LastDisposedViewCount == 1, "The old sorted view was not disposed exactly once.");

    await controller.NavigateToAsync(b);
    Assert(controller.CanGoBack, "Navigation history was not established.");
    int opensBeforeNoOp = factory.OpenCount;
    int viewsBeforeNoOp = factory.ViewCount;
    await controller.ApplySortAsync(controller.CurrentSortOptions);
    Assert(factory.OpenCount == opensBeforeNoOp && factory.ViewCount == viewsBeforeNoOp, "Same-options sort was not a no-op.");

    factory.FailSort = true;
    ExplorerSortOptions accepted = controller.CurrentSortOptions;
    await controller.ApplySortAsync(new ExplorerSortOptions(ExplorerSortField.Type, ExplorerSortDirection.Ascending, true));
    Assert(controller.CurrentSortOptions == accepted && controller.CanGoBack && controller.ErrorMessage is not null, "Failed sort changed accepted state.");
    factory.FailSort = false;

    var blocking = new BlockingViewFactory();
    using var staleController = new ExplorerNavigationController(blocking, blocking, blocking);
    await staleController.InitializeAsync(a);
    Task stale = staleController.ApplySortAsync(new ExplorerSortOptions(ExplorerSortField.DateModified, ExplorerSortDirection.Ascending, true));
    blocking.Started.Wait();
    Task latest = staleController.ApplySortAsync(new ExplorerSortOptions(ExplorerSortField.Type, ExplorerSortDirection.Ascending, true));
    await latest;
    blocking.Gate.Set();
    await stale;
    Assert(staleController.CurrentSortOptions.Field == ExplorerSortField.Type && !staleController.IsBusy, "Latest sort did not win.");
    Assert(blocking.StaleView.DisposeCount == 1, "The stale sorted view was not disposed exactly once.");

    var crossOperation = new BlockingViewFactory();
    using var crossController = new ExplorerNavigationController(crossOperation, crossOperation, crossOperation);
    await crossController.InitializeAsync(a);
    Task blockedSort = crossController.ApplySortAsync(new ExplorerSortOptions(ExplorerSortField.DateModified, ExplorerSortDirection.Ascending, true));
    crossOperation.Started.Wait();
    await crossController.NavigateToAsync(b);
    Assert(crossController.CurrentLocation == b, "Newer navigation did not replace a blocked sort.");
    crossOperation.Gate.Set();
    await blockedSort;
    Assert(crossController.CurrentLocation == b && crossOperation.StaleView.DisposeCount == 1, "Stale sort changed newer navigation state.");
}

static void WriteFile(string root, string name, int length) => File.WriteAllBytes(Path.Combine(root, name), new byte[length]);
static string[] Name(IExplorerSnapshot snapshot) => snapshot.GetRange(0, 4096).Items.Select(item => item.Name).ToArray();
static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

file sealed class TrackingFactory : ILocationSnapshotFactory, ILocationHierarchy, IExplorerSnapshotViewFactory
{
    public int OpenCount { get; private set; }
    public int ViewCount { get; private set; }
    public bool FailSort { get; set; }
    public int LastDisposedViewCount { get; private set; }

    public IExplorerSnapshot OpenSnapshot(ExplorerLocation location) { OpenCount++; return new TrackingSnapshot(location.Identifier, isView: false, this); }
    public IExplorerSnapshot CreateSortedView(IExplorerSnapshot source, ExplorerSortOptions options)
    {
        if (FailSort) throw new InvalidOperationException("Synthetic sort failure.");
        ViewCount++;
        return new TrackingSnapshot(((TrackingSnapshot)source).Id, isView: true, this);
    }
    public bool TryGetParent(ExplorerLocation location, out ExplorerLocation parent) { parent = null!; return false; }
    public ExplorerLocation ResolveChild(ExplorerLocation parent, ExplorerItem child) => ExplorerLocation.File(child.Name);
    internal void ViewDisposed() => LastDisposedViewCount++;
}

file sealed class BlockingViewFactory : ILocationSnapshotFactory, ILocationHierarchy, IExplorerSnapshotViewFactory
{
    public ManualResetEventSlim Started { get; } = new(false);
    public ManualResetEventSlim Gate { get; } = new(false);
    public TrackingSnapshot StaleView { get; private set; } = null!;
    public IExplorerSnapshot OpenSnapshot(ExplorerLocation location) => new TrackingSnapshot(location.Identifier, isView: false, this);
    public IExplorerSnapshot CreateSortedView(IExplorerSnapshot source, ExplorerSortOptions options)
    {
        if (options.Field == ExplorerSortField.DateModified)
        {
            Started.Set();
            Gate.Wait();
            StaleView = new TrackingSnapshot("stale", isView: true, this);
            return StaleView;
        }
        return new TrackingSnapshot("latest", isView: true, this);
    }
    public bool TryGetParent(ExplorerLocation location, out ExplorerLocation parent) { parent = null!; return false; }
    public ExplorerLocation ResolveChild(ExplorerLocation parent, ExplorerItem child) => ExplorerLocation.File(child.Name);
    internal void ViewDisposed() { }
}

file sealed class TrackingSnapshot : IExplorerSnapshot
{
    private readonly bool isView;
    private readonly object owner;
    private bool disposed;
    public TrackingSnapshot(string id, bool isView, object owner) { Id = id; this.isView = isView; this.owner = owner; }
    public string Id { get; }
    public int DisposeCount { get; private set; }
    public ulong Count => 1;
    public bool TryGetIndexByItemId(ulong itemId, out ulong index)
    {
        index = 0;
        return itemId == 1;
    }
    public ExplorerItemBatch GetRange(ulong start, uint count)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        return new ExplorerItemBatch(start, [new ExplorerItem(1, Id, DateTimeOffset.UnixEpoch, null, ExplorerItemKind.Directory)]);
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        DisposeCount++;
        if (isView && owner is TrackingFactory tracking) tracking.ViewDisposed();
    }
}
