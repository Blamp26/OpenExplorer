using OpenExplorer.Application.Diagnostics;
using OpenExplorer.Application.Selection;
using OpenExplorer.Contracts;

try
{
    RunSelectionTransitions();
    RunInvertedSelectAll();
    RunRefreshReconciliation();
    RunExplicitOperationTargetChecks();
    RunSortingPreservation();
    RunKeyboardLookupChecks();
    await RunNavigationSelectionChecksAsync();
    Console.WriteLine("Selection model: transitions, inverted select-all, sorting preservation, keyboard lookup passed");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"Selection smoke test failed: {exception.Message}");
    return 1;
}

static async Task RunNavigationSelectionChecksAsync()
{
    var factory = new NavigationFactory();
    using var controller = new OpenExplorer.Application.Navigation.ExplorerNavigationController(factory, factory, factory);
    await controller.InitializeAsync(ExplorerLocation.File("A"));
    controller.Selection.SelectSingle(controller.CurrentItems!.GetSourceItem(0));
    factory.Fail = true;
    await controller.NavigateToAsync(ExplorerLocation.File("B"));
    Assert(controller.CurrentLocation!.Identifier == "A" && controller.Selection.IsSelected(1), "Failed navigation changed selection state.");
    factory.Fail = false;
    factory.BlockB = true;
    Task stale = controller.NavigateToAsync(ExplorerLocation.File("B"));
    factory.Started.Wait();
    await controller.NavigateToAsync(ExplorerLocation.File("C"));
    Assert(controller.CurrentLocation!.Identifier == "C" && !controller.Selection.IsSelected(1), "Accepted navigation did not clear selection.");
    factory.Gate.Set();
    await stale;
    Assert(controller.CurrentLocation!.Identifier == "C" && !controller.Selection.IsSelected(1), "Stale navigation restored selection.");
}

static void RunSelectionTransitions()
{
    using var source = new SnapshotFileItemList(new FakeSnapshot(12));
    using var selection = new ExplorerSelectionModel();
    selection.SetLogicalItemCount(source.LogicalItemCount);
    ExplorerItem item2 = source.GetSourceItem(2);
    selection.SelectSingle(item2);
    Assert(selection.IsSelected(item2.ItemId) && selection.AnchorItemId == item2.ItemId, "Single selection failed.");
    selection.SelectRange(source, 5, toggleRange: false);
    for (int index = 2; index <= 5; index++) Assert(selection.IsSelected(source.GetSourceItem(index).ItemId), "Shift range failed.");
    selection.Toggle(source.GetSourceItem(3));
    Assert(!selection.IsSelected(source.GetSourceItem(3).ItemId), "Ctrl toggle failed.");
    selection.SelectRange(source, 7, toggleRange: true);
    Assert(!selection.IsSelected(source.GetSourceItem(2).ItemId) && selection.IsSelected(source.GetSourceItem(7).ItemId), "Ctrl+Shift toggle range semantics failed.");
    selection.MoveFocus(source, SelectionMove.Home, extendSelection: false);
    Assert(selection.FocusedItemId == source.GetSourceItem(0).ItemId && selection.IsSelected(source.GetSourceItem(0).ItemId), "Home focus failed.");
    selection.MoveFocus(source, SelectionMove.End, extendSelection: true);
    Assert(selection.FocusedItemId == source.GetSourceItem(11).ItemId && selection.IsSelected(source.GetSourceItem(11).ItemId), "Shift+End failed.");
    selection.Clear();
    Assert(!selection.IsSelected(source.GetSourceItem(0).ItemId) && selection.FocusedItemId is null, "Escape/Clear failed.");
}

static void RunInvertedSelectAll()
{
    using var source = new SnapshotFileItemList(new FakeSnapshot(100_000));
    using var selection = new ExplorerSelectionModel();
    selection.SelectAll(source.LogicalItemCount);
    Assert(selection.IsAllSelected && selection.IsSelected(99_999), "Select All did not use all-selected state.");
    selection.Toggle(source.GetSourceItem(123));
    Assert(!selection.IsSelected(123) && selection.IsSelected(124), "Select All exception failed.");
    selection.Clear();
    Assert(!selection.IsAllSelected, "Clear did not exit inverted mode.");
}

static void RunRefreshReconciliation()
{
    using var selection = new ExplorerSelectionModel();
    using var original = new SnapshotFileItemList(new FakeSnapshot(4, [10, 20, 30, 40]));
    selection.SelectSingle(original.GetSourceItem(0));
    selection.SelectRange(original, 2, toggleRange: false);
    Assert(selection.AnchorItemId == 10 && selection.FocusedItemId == 30, "Refresh setup did not establish focus and anchor.");

    using var refreshed = new FakeSnapshot(3, [10, 30, 50]);
    selection.Reconcile(refreshed);
    Assert(selection.IsSelected(10) && selection.IsSelected(30) && !selection.IsSelected(20), "Refresh did not prune missing explicit selection.");
    Assert(selection.AnchorItemId == 10 && selection.FocusedItemId == 30, "Refresh did not preserve valid focus and anchor IDs.");
    Assert(refreshed.RangeRequestCount == 0, "Refresh reconciliation paged the managed item list.");

    selection.SelectAll(3);
    selection.Toggle(original.GetSourceItem(2));
    selection.Reconcile(refreshed);
    Assert(selection.IsAllSelected && !selection.IsSelected(30) && selection.IsSelected(10), "Refresh did not preserve inverted Select All semantics.");
}

static void RunSortingPreservation()
{
    using var original = new SnapshotFileItemList(new FakeSnapshot(4, [10, 20, 30, 40]));
    using var sorted = new SnapshotFileItemList(new FakeSnapshot(4, [40, 10, 30, 20]));
    using var selection = new ExplorerSelectionModel();
    selection.SelectSingle(original.GetSourceItem(1));
    Assert(selection.IsSelected(sorted.GetSourceItem(3).ItemId), "Selection did not survive sorting by ItemId.");
}

static void RunExplicitOperationTargetChecks()
{
    using var selection = new ExplorerSelectionModel();
    using var snapshot = new FakeSnapshot(100_000);
    selection.SelectSingle(snapshot.GetRange(42, 1).Items[0]);
    selection.Toggle(snapshot.GetRange(77, 1).Items[0]);

    Assert(selection.TryGetExplicitSelectedItems(snapshot, out IReadOnlyList<ExplorerItem> targets), "Explicit operation targets were not resolved.");
    Assert(targets.Count == 2 && targets[0].ItemId == 42 && targets[1].ItemId == 77, "Stable-ID operation targets were incorrect.");
    Assert(snapshot.RangeRequestCount == 4, "Operation target resolution performed an unbounded managed scan.");

    selection.SelectAll(snapshot.Count);
    Assert(!selection.TryGetExplicitSelectedItems(snapshot, out _), "Inverted Select All was materialized for an operation.");
}

static void RunKeyboardLookupChecks()
{
    var snapshot = new LookupSnapshot(100_000);
    using var source = new SnapshotFileItemList(snapshot);
    using var selection = new ExplorerSelectionModel();
    selection.SelectSingle(source.GetSourceItem(50_000));
    long rangeRequestsBeforeMovement = source.RangeRequestCount;

    Assert(selection.MoveFocus(source, SelectionMove.Next, extendSelection: false) == 50_001, "Arrow movement returned the wrong index.");
    Assert(selection.MoveFocus(source, SelectionMove.Home, extendSelection: false) == 0, "Home movement returned the wrong index.");
    Assert(selection.MoveFocus(source, SelectionMove.End, extendSelection: false) == 99_999, "End movement returned the wrong index.");
    Assert(snapshot.LookupRequests >= 3, "Keyboard movement did not use snapshot lookup.");
    Assert(source.RangeRequestCount - rangeRequestsBeforeMovement <= 4, "Keyboard remapping caused proportional range reads.");
    Assert(selection.MoveFocus(source, SelectionMove.Home, extendSelection: true) == 0, "Shift+Home movement returned the wrong index.");

    selection.SelectSingle(new ExplorerItem(999_999, "missing", DateTimeOffset.UnixEpoch, null, ExplorerItemKind.File));
    Assert(selection.MoveFocus(source, SelectionMove.Next, extendSelection: false) == 0, "Missing focused ID was not remapped predictably.");
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

file sealed class FakeSnapshot : IExplorerSnapshot
{
    private readonly ulong[] ids;
    private bool disposed;

    public FakeSnapshot(int count, ulong[]? ids = null) => this.ids = ids ?? Enumerable.Range(0, count).Select(index => (ulong)index).ToArray();
    public int RangeRequestCount { get; private set; }
    public ulong Count => (ulong)ids.Length;

    public bool TryGetIndexByItemId(ulong itemId, out ulong index)
    {
        int found = Array.IndexOf(ids, itemId);
        index = found < 0 ? 0UL : (ulong)found;
        return found >= 0;
    }

    public ExplorerItemBatch GetRange(ulong start, uint count)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        RangeRequestCount++;
        if (start > Count || count > Count - start) throw new ArgumentOutOfRangeException();
        var items = new List<ExplorerItem>((int)count);
        for (ulong index = start; index < start + count; index++)
        {
            items.Add(new ExplorerItem(ids[index], $"item-{ids[index]}", DateTimeOffset.UnixEpoch, 1, ExplorerItemKind.File));
        }
        return new ExplorerItemBatch(start, items);
    }

    public void Dispose() => disposed = true;
}

file sealed class LookupSnapshot : IExplorerSnapshot
{
    private readonly ulong count;
    private bool disposed;

    public LookupSnapshot(ulong count) => this.count = count;
    public int LookupRequests { get; private set; }
    public ulong Count => count;

    public bool TryGetIndexByItemId(ulong itemId, out ulong index)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        LookupRequests++;
        index = itemId == 0 || itemId > count ? 0 : itemId - 1;
        return itemId > 0 && itemId <= count;
    }

    public ExplorerItemBatch GetRange(ulong start, uint requestedCount)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ulong actualCount = Math.Min((ulong)requestedCount, count - start);
        var items = new List<ExplorerItem>((int)actualCount);
        for (ulong index = start; index < start + actualCount; index++)
        {
            items.Add(new ExplorerItem(index + 1, $"item-{index + 1}", DateTimeOffset.UnixEpoch, 1, ExplorerItemKind.File));
        }
        return new ExplorerItemBatch(start, items);
    }

    public void Dispose() => disposed = true;
}

file sealed class NavigationFactory : ILocationSnapshotFactory, ILocationHierarchy, IExplorerSnapshotViewFactory
{
    public bool Fail { get; set; }
    public bool BlockB { get; set; }
    public ManualResetEventSlim Started { get; } = new(false);
    public ManualResetEventSlim Gate { get; } = new(false);

    public IExplorerSnapshot OpenSnapshot(ExplorerLocation location)
    {
        if (Fail) throw new InvalidOperationException("Navigation failure.");
        if (BlockB && location.Identifier == "B")
        {
            Started.Set();
            Gate.Wait();
        }
        return new FakeSnapshot(1, [1]);
    }

    public IExplorerSnapshot CreateSortedView(IExplorerSnapshot source, ExplorerSortOptions options) => new FakeSnapshot(checked((int)source.Count), [1]);
    public bool TryGetParent(ExplorerLocation location, out ExplorerLocation parent) { parent = null!; return false; }
    public ExplorerLocation ResolveChild(ExplorerLocation parent, ExplorerItem child) => ExplorerLocation.File(child.Name);
}
