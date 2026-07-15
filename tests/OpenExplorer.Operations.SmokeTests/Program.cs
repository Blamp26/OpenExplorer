using OpenExplorer.Application.Navigation;
using OpenExplorer.Application.Operations;
using OpenExplorer.Contracts;

try
{
    await RunAcceptedBatchAsync();
    await RunStaleBatchAsync();
    Console.WriteLine("Operations smoke: one accepted refresh per batch and stale completion suppression passed");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"Operations smoke test failed: {exception.Message}");
    return 1;
}

static async Task RunAcceptedBatchAsync()
{
    var factory = new OperationFactory();
    using var navigation = new ExplorerNavigationController(factory, factory, factory);
    await navigation.InitializeAsync(ExplorerLocation.File("A"));
    var provider = new FakeProvider
    {
        Result = new(ExplorerFileOperationStatus.Succeeded, Array.Empty<ExplorerFileOperationFailure>(), true),
        WaitForRelease = false,
    };
    using var coordinator = new ExplorerFileOperationCoordinator(navigation, provider);
    var request = new ExplorerFileOperationRequest(
        ExplorerFileOperationKind.Rename,
        navigation.CurrentLocation!,
        [new ExplorerFileOperationItem(1, "old.txt", ExplorerItemKind.File)],
        "new.txt",
        navigation.Generation);

    ExplorerFileOperationResult result = await coordinator.ExecuteAsync(request);
    Assert(result.Mutated && factory.OpenCount == 2, "An accepted mutation did not perform exactly one refresh.");
    Assert(navigation.CurrentLocation == ExplorerLocation.File("A") && !navigation.IsBusy && !coordinator.IsBusy, "Accepted operation changed state or busy ownership.");
}

static async Task RunStaleBatchAsync()
{
    var factory = new OperationFactory();
    using var navigation = new ExplorerNavigationController(factory, factory, factory);
    await navigation.InitializeAsync(ExplorerLocation.File("A"));
    var provider = new FakeProvider
    {
        Result = new(ExplorerFileOperationStatus.Succeeded, Array.Empty<ExplorerFileOperationFailure>(), true),
        WaitForRelease = true,
    };
    using var coordinator = new ExplorerFileOperationCoordinator(navigation, provider);
    var request = new ExplorerFileOperationRequest(
        ExplorerFileOperationKind.Rename,
        navigation.CurrentLocation!,
        [new ExplorerFileOperationItem(1, "old.txt", ExplorerItemKind.File)],
        "new.txt",
        navigation.Generation);

    Task<ExplorerFileOperationResult> operation = coordinator.ExecuteAsync(request);
    await provider.Started;
    await navigation.NavigateToAsync(ExplorerLocation.File("B"));
    provider.Release.SetResult(true);
    await operation;
    Assert(factory.OpenCount == 2 && navigation.CurrentLocation == ExplorerLocation.File("B"), "Stale operation refreshed or changed the new location.");
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

file sealed class FakeProvider : IExplorerFileOperationProvider
{
    public ExplorerFileOperationResult Result { get; init; } = new(ExplorerFileOperationStatus.Succeeded, Array.Empty<ExplorerFileOperationFailure>(), false);
    public bool WaitForRelease { get; init; }
    public TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task Started => started.Task;
    private readonly TaskCompletionSource<bool> started = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async Task<ExplorerFileOperationResult> ExecuteAsync(ExplorerFileOperationRequest request, CancellationToken cancellationToken)
    {
        started.TrySetResult(true);
        if (WaitForRelease) await Release.Task.WaitAsync(cancellationToken);
        return Result;
    }
}

file sealed class OperationFactory : ILocationSnapshotFactory, IExplorerSnapshotViewFactory, ILocationHierarchy
{
    public int OpenCount { get; private set; }
    public IExplorerSnapshot OpenSnapshot(ExplorerLocation location) => new OperationSnapshot(++OpenCount);
    public IExplorerSnapshot CreateSortedView(IExplorerSnapshot source, ExplorerSortOptions options) => new OperationSnapshot(((OperationSnapshot)source).Version);
    public bool TryGetParent(ExplorerLocation location, out ExplorerLocation parent) { parent = location; return false; }
    public ExplorerLocation ResolveChild(ExplorerLocation parent, ExplorerItem child) => parent;
}

file sealed class OperationSnapshot : IExplorerSnapshot
{
    public OperationSnapshot(int version) => Version = version;
    public int Version { get; }
    public ulong Count => 1;
    public bool TryGetIndexByItemId(ulong itemId, out ulong index) { index = 0; return itemId == 1; }
    public ExplorerItemBatch GetRange(ulong start, uint count) => new(start, [new ExplorerItem(1, $"item-{Version}", DateTimeOffset.UnixEpoch, 1, ExplorerItemKind.File)]);
    public void Dispose() { }
}
