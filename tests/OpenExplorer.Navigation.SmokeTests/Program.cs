using System.Diagnostics;
using OpenExplorer.Application.Diagnostics;
using OpenExplorer.Application.Navigation;
using OpenExplorer.Contracts;
using OpenExplorer.Interop;

try
{
    await RunFakeNavigationChecksAsync();
    await RunStaleRequestCheckAsync();
    await RunRealProviderCheckAsync();
    Console.WriteLine("Navigation model: history, stale requests, and local folder transitions passed");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"Navigation smoke test failed: {exception.Message}");
    return 1;
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

static async Task RunRealProviderCheckAsync()
{
    string root = Path.Combine(Path.GetTempPath(), $"OpenExplorer-Navigation-{Environment.ProcessId}-{Stopwatch.GetTimestamp()}");
    try
    {
        Directory.CreateDirectory(Path.Combine(root, "Child", "Grandchild"));
        File.WriteAllText(Path.Combine(root, "root.txt"), "root");
        File.WriteAllText(Path.Combine(root, "Child", "inside.txt"), "inside");

        using var engine = new NativeExplorerEngine();
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
