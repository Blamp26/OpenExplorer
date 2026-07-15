using System.Diagnostics;
using OpenExplorer.Application.Diagnostics;
using OpenExplorer.Application.Navigation;
using OpenExplorer.Contracts;
using OpenExplorer.Interop;

try
{
    await RunFakeNavigationChecksAsync();
    await RunStaleRequestCheckAsync();
    await RunStaleAddressRequestCheckAsync();
    await RunRefreshChecksAsync();
    await RunAddressAndBreadcrumbChecksAsync();
    await RunRealProviderCheckAsync();
    Console.WriteLine("Navigation model: history, stale requests, and local folder transitions passed");
    Console.WriteLine("Refresh smoke: F5/toolbar refresh keeps valid contents and state");
    Console.WriteLine("Navigation shell smoke: address submission, cancel, and breadcrumbs passed");
    return 0;
}

catch (Exception exception)
{
    Console.Error.WriteLine($"Navigation smoke test failed: {exception.Message}");
    return 1;
}

static async Task RunAddressAndBreadcrumbChecksAsync()
{
    var factory = new FakeFactory();
    var hierarchy = new FakeHierarchy();
    var resolver = new FakeAddressResolver();
    using var controller = new ExplorerNavigationController(factory, hierarchy, factory, resolver);
    ExplorerLocation a = ExplorerLocation.File("A");
    await controller.InitializeAsync(a);
    await controller.NavigateAddressAsync("  \"B\"  ");
    Assert(controller.CurrentLocation == ExplorerLocation.File("B") && controller.CanGoBack, "Address submission did not navigate normally.");
    int opens = factory.OpenCount;
    await controller.NavigateAddressAsync("B");
    Assert(factory.OpenCount == opens && controller.CanGoBack, "Re-entering the current address duplicated history or opened a snapshot.");
    await controller.NavigateAddressAsync("missing");
    Assert(controller.CurrentLocation == ExplorerLocation.File("B") && controller.CanGoBack && controller.ErrorMessage is not null, "Invalid address changed accepted state.");
    Assert(controller.CurrentBreadcrumbs.Count == 2 && controller.CurrentBreadcrumbs[^1].IsCurrent, "Accepted breadcrumbs were not generated.");
    await controller.NavigateAddressAsync("C");
    Assert(controller.CurrentLocation == ExplorerLocation.File("C"), "A later address did not supersede the previous state.");
}

static async Task RunFakeNavigationChecksAsync()
{
    var factory = new FakeFactory();
    var hierarchy = new FakeHierarchy();
    using var controller = new ExplorerNavigationController(factory, hierarchy, factory);
    ExplorerLocation a = ExplorerLocation.File("A");
    ExplorerLocation b = ExplorerLocation.File("B");
    ExplorerLocation c = ExplorerLocation.File("C");
    await controller.InitializeAsync(a);
    Assert(factory.OpenCount == 1 && controller.CurrentLocation == a && !controller.CanGoBack && !controller.CanGoForward, "Initialization state was incorrect.");

    await controller.NavigateIntoAsync(new ExplorerItem(2, "B", DateTimeOffset.UnixEpoch, null, ExplorerItemKind.Directory));
    Assert(controller.CurrentLocation == b && controller.CanGoBack && !controller.CanGoForward, "Child navigation history was incorrect.");
    await controller.GoBackAsync();
    Assert(controller.CurrentLocation == a && controller.CanGoForward, "Back did not restore the prior location.");
    await controller.GoForwardAsync();
    Assert(controller.CurrentLocation == b && controller.CanGoBack, "Forward did not restore the next location.");
    await controller.GoUpAsync();
    Assert(controller.CurrentLocation == a, "Up did not open the parent location.");
    await controller.GoUpAsync();
    Assert(controller.CurrentLocation == a, "Up at the root was not a no-op.");
    await controller.NavigateIntoAsync(new ExplorerItem(3, "file.txt", DateTimeOffset.UnixEpoch, 1, ExplorerItemKind.File));
    Assert(controller.CurrentLocation == a, "File activation was not a no-op.");

    factory.Failures.Add("B");
    await controller.NavigateIntoAsync(new ExplorerItem(2, "B", DateTimeOffset.UnixEpoch, null, ExplorerItemKind.Directory));
    Assert(controller.CurrentLocation == a && controller.ErrorMessage is not null && controller.CanGoBack, "Failed navigation changed state or history.");
    factory.Failures.Clear();
    await controller.NavigateToAsync(b);
    await controller.NavigateToAsync(c);
    await controller.GoBackAsync();
    Assert(controller.CurrentLocation == b && controller.CanGoForward, "Back setup failed.");
    factory.Failures.Add("C");
    await controller.GoForwardAsync();
    Assert(controller.CurrentLocation == b && controller.CanGoForward, "Failed forward changed history.");
    factory.Failures.Clear();
    await controller.NavigateToAsync(a);
    Assert(!controller.CanGoForward, "New navigation did not clear forward history.");

    using var disposedController = new ExplorerNavigationController(factory, hierarchy, factory);
    await disposedController.InitializeAsync(a);
    FakeSnapshot active = factory.LastCreated!;
    disposedController.Dispose();
    disposedController.Dispose();
    Assert(active.DisposeCount == 1, "Controller disposal was not idempotent.");
    AssertThrows<ObjectDisposedException>(() => _ = disposedController.CurrentLocation);
}

static async Task RunStaleRequestCheckAsync()
{
    var factory = new BlockingFactory();
    var hierarchy = new FakeHierarchy();
    using var controller = new ExplorerNavigationController(factory, hierarchy, factory);
    ExplorerLocation a = ExplorerLocation.File("A");
    ExplorerLocation b = ExplorerLocation.File("B");
    ExplorerLocation c = ExplorerLocation.File("C");
    await controller.InitializeAsync(a);

    Task bTask = controller.NavigateToAsync(b);
    factory.BStarted.Wait();
    Task cTask = controller.NavigateToAsync(c);
    factory.CStarted.Wait();
    factory.CSnapshot = new FakeSnapshot(c.Identifier);
    factory.CGate.Set();
    await cTask;
    Assert(controller.CurrentLocation == c && controller.IsBusy == false, "The latest navigation did not win.");
    factory.BSnapshot = new FakeSnapshot(b.Identifier);
    factory.BGate.Set();
    await bTask;
    Assert(controller.CurrentLocation == c && factory.BSnapshot.DisposeCount == 1 && controller.CanGoBack, "The stale navigation was not disposed or changed history.");
}

static async Task RunStaleAddressRequestCheckAsync()
{
    var factory = new BlockingFactory();
    var hierarchy = new FakeHierarchy();
    var resolver = new FakeAddressResolver();
    using var controller = new ExplorerNavigationController(factory, hierarchy, factory, resolver);
    await controller.InitializeAsync(ExplorerLocation.File("A"));

    Task stale = controller.NavigateAddressAsync("B");
    factory.BStarted.Wait();
    Task accepted = controller.NavigateAddressAsync("C");
    factory.CStarted.Wait();
    factory.CSnapshot = new FakeSnapshot("C");
    factory.CGate.Set();
    await accepted;
    factory.BSnapshot = new FakeSnapshot("B");
    factory.BGate.Set();
    await stale;
    Assert(controller.CurrentLocation == ExplorerLocation.File("C") && controller.CanGoBack, "Stale address navigation changed the accepted location.");
}

static async Task RunRealProviderCheckAsync()
{
    string root = Path.Combine(Path.GetTempPath(), $"OpenExplorer-Navigation-{Environment.ProcessId}-{Stopwatch.GetTimestamp()}");
    try
    {
        Directory.CreateDirectory(Path.Combine(root, "Child", "Grandchild"));
        File.WriteAllText(Path.Combine(root, "root.txt"), "root");
        File.WriteAllText(Path.Combine(root, "Child", "inside.txt"), "inside");

        using var engine = new NativeExplorerEngine();
        ExplorerLocation expanded = engine.ParseAddress("  \"%USERPROFILE%\"  ");
        Assert(Path.IsPathFullyQualified(expanded.Identifier), "Environment expansion did not produce an absolute location.");
        IReadOnlyList<ExplorerBreadcrumb> unc = engine.GetBreadcrumbs(ExplorerLocation.File(@"\\server\share\folder"));
        Assert(unc.Count == 2 && unc[0].Label.Equals(@"\\server\share", StringComparison.OrdinalIgnoreCase) && unc[0].Location.Identifier.EndsWith(@"share\", StringComparison.OrdinalIgnoreCase), "UNC share-root breadcrumbs were not preserved as one ancestor.");
        ILocationSnapshotFactory factory = engine;
        ILocationHierarchy hierarchy = engine;
        using var controller = new ExplorerNavigationController(factory, hierarchy, engine);
        await controller.InitializeAsync(ExplorerLocation.File(Path.GetFullPath(root)));
        SnapshotFileItem child = FindItem(controller.CurrentItems!, "Child");
        await controller.NavigateIntoAsync(child.SourceItem);
        Assert(controller.CurrentLocation!.Identifier.EndsWith("Child", StringComparison.OrdinalIgnoreCase), "Real child navigation failed.");
        FindItem(controller.CurrentItems!, "inside.txt");
        FindItem(controller.CurrentItems!, "Grandchild");
        await controller.GoUpAsync();
        Assert(controller.CurrentLocation!.Identifier.Equals(Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase), "Real Up navigation failed.");
        child = FindItem(controller.CurrentItems!, "Child");
        await controller.NavigateIntoAsync(child.SourceItem);
        await controller.GoBackAsync();
        Assert(controller.CurrentLocation!.Identifier.Equals(Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase), "Real Back navigation failed.");
        await controller.GoForwardAsync();
        Assert(controller.CurrentLocation!.Identifier.EndsWith("Child", StringComparison.OrdinalIgnoreCase), "Real Forward navigation failed.");
    }
    finally
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
}

static async Task RunRefreshChecksAsync()
{
    var factory = new RefreshFactory();
    var hierarchy = new FakeHierarchy();
    using var controller = new ExplorerNavigationController(factory, hierarchy, factory);
    ExplorerLocation a = ExplorerLocation.File("A");
    ExplorerLocation b = ExplorerLocation.File("B");
    await controller.InitializeAsync(a);
    await controller.NavigateToAsync(b);
    ExplorerItem first = controller.CurrentItems!.GetSourceItem(0);
    ExplorerItem second = controller.CurrentItems.GetSourceItem(1);
    controller.Selection.SelectSingle(first);
    controller.Selection.Toggle(second);
    ulong anchor = controller.Selection.AnchorItemId!.Value;
    ulong focused = controller.Selection.FocusedItemId!.Value;
    ExplorerSortOptions sort = new(ExplorerSortField.Size, ExplorerSortDirection.Descending, false);
    await controller.ApplySortAsync(sort);

    factory.NextItems = [first.ItemId, second.ItemId, 99];
    await controller.RefreshAsync();
    Assert(controller.CurrentLocation == b && controller.CanGoBack && !controller.CanGoForward, "Refresh changed location or history.");
    Assert(controller.CurrentSortOptions == sort, "Refresh changed the active sort policy.");
    Assert(controller.Selection.IsSelected(first.ItemId) && controller.Selection.IsSelected(second.ItemId), "Refresh did not preserve valid selection.");
    Assert(controller.Selection.AnchorItemId == anchor && controller.Selection.FocusedItemId == focused, "Refresh did not preserve focus and anchor.");

    controller.Selection.SelectAll(controller.CurrentItems!.LogicalItemCount);
    ulong exceptionId = controller.CurrentItems.GetSourceItem(1).ItemId;
    controller.Selection.Toggle(controller.CurrentItems.GetSourceItem(1));
    factory.NextItems = [first.ItemId, exceptionId];
    await controller.RefreshAsync();
    Assert(controller.Selection.IsAllSelected && !controller.Selection.IsSelected(exceptionId), "Refresh materialized or lost inverted Select All.");

    SnapshotFileItemList oldItems = controller.CurrentItems!;
    factory.Fail = true;
    await controller.RefreshAsync();
    Assert(ReferenceEquals(oldItems, controller.CurrentItems) && controller.ErrorMessage is not null, "Failed refresh replaced valid contents.");
    factory.Fail = false;

    factory.BlockNext = true;
    Task stale = controller.RefreshAsync();
    factory.Started.Wait();
    factory.NextItems = [777];
    await controller.RefreshAsync();
    factory.Gate.Set();
    await stale;
    Assert(controller.CurrentItems!.GetSourceItem(0).ItemId == 777, "Stale refresh changed the accepted refresh result.");
}

static SnapshotFileItem FindItem(SnapshotFileItemList items, string name)
{
    for (int index = 0; index < items.Count; index++)
    {
        if (items[index].Name == name) return items[index];
    }
    throw new InvalidOperationException($"The expected item '{name}' was not found.");
}

static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

static void AssertThrows<TException>(Action action) where TException : Exception
{
    try { action(); }
    catch (TException) { return; }
    throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
}

file sealed class FakeFactory : ILocationSnapshotFactory, IExplorerSnapshotViewFactory
{
    public int OpenCount { get; private set; }
    public HashSet<string> Failures { get; } = [];
    public FakeSnapshot? LastCreated { get; private set; }

    public IExplorerSnapshot OpenSnapshot(ExplorerLocation location)
    {
        OpenCount++;
        if (Failures.Contains(location.Identifier)) throw new InvalidOperationException($"Cannot open {location.Identifier}.");
        LastCreated = new FakeSnapshot(location.Identifier);
        return LastCreated;
    }

    public IExplorerSnapshot CreateSortedView(IExplorerSnapshot source, ExplorerSortOptions options)
        => new FakeSnapshot(((FakeSnapshot)source).Id);
}

file sealed class FakeAddressResolver : ILocationAddressResolver
{
    public ExplorerLocation ParseAddress(string input)
    {
        string value = input.Trim().Trim('"').Trim();
        if (value is not ("A" or "B" or "C")) throw new ArgumentException("Invalid address.");
        return ExplorerLocation.File(value);
    }

    public IReadOnlyList<ExplorerBreadcrumb> GetBreadcrumbs(ExplorerLocation location) =>
        [new("Root", ExplorerLocation.File("A"), location.Identifier == "A"), new(location.Identifier, location, true)];
}

file sealed class BlockingFactory : ILocationSnapshotFactory, IExplorerSnapshotViewFactory
{
    public ManualResetEventSlim BStarted { get; } = new(false);
    public ManualResetEventSlim CStarted { get; } = new(false);
    public ManualResetEventSlim BGate { get; } = new(false);
    public ManualResetEventSlim CGate { get; } = new(false);
    public FakeSnapshot? BSnapshot { get; set; }
    public FakeSnapshot? CSnapshot { get; set; }

    public IExplorerSnapshot OpenSnapshot(ExplorerLocation location)
    {
        if (location.Identifier == "B") { BStarted.Set(); BGate.Wait(); return BSnapshot ?? new FakeSnapshot("B"); }
        if (location.Identifier == "C") { CStarted.Set(); CGate.Wait(); return CSnapshot ?? new FakeSnapshot("C"); }
        return new FakeSnapshot(location.Identifier);
    }

    public IExplorerSnapshot CreateSortedView(IExplorerSnapshot source, ExplorerSortOptions options)
        => new FakeSnapshot(((FakeSnapshot)source).Id);
}

file sealed class RefreshFactory : ILocationSnapshotFactory, IExplorerSnapshotViewFactory
{
    public ulong[] NextItems { get; set; } = [1, 2, 3];
    public bool Fail { get; set; }
    public bool BlockNext { get; set; }
    public ManualResetEventSlim Started { get; } = new(false);
    public ManualResetEventSlim Gate { get; } = new(false);

    public IExplorerSnapshot OpenSnapshot(ExplorerLocation location)
    {
        if (Fail) throw new InvalidOperationException("Refresh failure.");
        if (BlockNext)
        {
            BlockNext = false;
            Started.Set();
            Gate.Wait();
        }
        return new RefreshSnapshot(NextItems);
    }

    public IExplorerSnapshot CreateSortedView(IExplorerSnapshot source, ExplorerSortOptions options)
        => new RefreshSnapshot(((RefreshSnapshot)source).Ids);
}

file sealed class RefreshSnapshot : IExplorerSnapshot
{
    private readonly ExplorerItem[] items;
    private bool disposed;

    public RefreshSnapshot(IEnumerable<ulong> ids)
    {
        Ids = ids.ToArray();
        items = Ids.Select(id => new ExplorerItem(id, $"item-{id}", DateTimeOffset.UnixEpoch, 1, ExplorerItemKind.File)).ToArray();
    }

    public ulong[] Ids { get; }
    public ulong Count => (ulong)items.Length;

    public bool TryGetIndexByItemId(ulong itemId, out ulong index)
    {
        int found = Array.FindIndex(items, item => item.ItemId == itemId);
        index = found < 0 ? 0UL : (ulong)found;
        return found >= 0;
    }

    public ExplorerItemBatch GetRange(ulong start, uint count)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        return new ExplorerItemBatch(start, items.Skip((int)start).Take((int)count).ToArray());
    }

    public void Dispose() => disposed = true;
}

file sealed class FakeHierarchy : ILocationHierarchy
{
    public bool TryGetParent(ExplorerLocation location, out ExplorerLocation parent)
    {
        if (location.Identifier == "B" || location.Identifier == "C") { parent = ExplorerLocation.File("A"); return true; }
        parent = null!;
        return false;
    }

    public ExplorerLocation ResolveChild(ExplorerLocation parent, ExplorerItem child) => ExplorerLocation.File(child.Name);
}

file sealed class FakeSnapshot : IExplorerSnapshot
{
    private readonly ExplorerItem[] items;
    private bool disposed;

    public FakeSnapshot(string id)
    {
        Id = id;
        items = [new ExplorerItem(1, id, DateTimeOffset.UnixEpoch, null, ExplorerItemKind.Directory)];
    }

    public string Id { get; }
    public int DisposeCount { get; private set; }
    public ulong Count => (ulong)items.Length;

    public bool TryGetIndexByItemId(ulong itemId, out ulong index)
    {
        for (int candidate = 0; candidate < items.Length; candidate++)
        {
            if (items[candidate].ItemId == itemId)
            {
                index = (ulong)candidate;
                return true;
            }
        }
        index = 0;
        return false;
    }

    public ExplorerItemBatch GetRange(ulong start, uint count)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (start > Count) throw new ArgumentOutOfRangeException(nameof(start));
        return new ExplorerItemBatch(start, items.Skip((int)start).Take((int)Math.Min(count, Count - start)).ToArray());
    }

    public void Dispose()
    {
        if (!disposed) { disposed = true; DisposeCount++; }
    }
}
