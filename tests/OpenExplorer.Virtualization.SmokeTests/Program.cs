using OpenExplorer.Application.Diagnostics;
using OpenExplorer.Contracts;
using OpenExplorer.Application.Icons;

return await RunSmokeTestAsync();

static async Task<int> RunSmokeTestAsync()
{
    try
    {
        using var fake = new FakeSnapshot(100_000);
        using var source = new SnapshotFileItemList(fake);
        Assert(source.LogicalItemCount == 100_000, "The logical count was incorrect.");
        Assert(source.RangeRequestCount == 0, "Construction performed a range request.");
        _ = source[0];
        Assert(source.RangeRequestCount == 1 && source.CurrentCachedItemCount == 256, "The first page was not loaded exactly once.");
        _ = source[1];
        Assert(source.RangeRequestCount == 1, "A same-page access reloaded the page.");
        _ = source[256];
        Assert(source.RangeRequestCount == 2, "The second page was not loaded.");

        for (int read = 0; read < 10_000; read++)
        {
            int index = (read * 7_919) % source.Count;
            Assert(source[index].ItemId == (ulong)index + 1, $"Read {read} returned a wrong item.");
            Assert(source.CurrentCachedPages <= 4, "The page cache exceeded four pages.");
            Assert(source.CurrentCachedItemCount <= 1_024, "The item cache exceeded 1,024 items.");
        }

        _ = source[99_999];
        Assert(source[99_999].Name == "Document 99999", "The final partial page was incorrect.");
        long requestsBeforeEviction = source.RangeRequestCount;
        _ = source[0];
        Assert(source.RangeRequestCount > requestsBeforeEviction, "The least-recently-used page was not reloaded.");
        AssertThrows<ArgumentOutOfRangeException>(() => _ = source[-1]);
        AssertThrows<ArgumentOutOfRangeException>(() => _ = source[100_000]);

        source.Dispose();
        Assert(fake.DisposeCount == 1, "The snapshot was not disposed exactly once.");
        AssertThrows<ObjectDisposedException>(() => _ = source[0]);
        source.Dispose();
        Assert(fake.DisposeCount == 1, "Snapshot disposal was not idempotent.");

        await using var iconProvider = new DeterministicFakeIconProvider();
        await VerifyIconsAsync(iconProvider);

        Console.WriteLine("Snapshot virtualization source: 100000 items, page 256, cache <= 1024");
        return 0;
    }
    catch (Exception exception)
    {
        Console.Error.WriteLine($"Snapshot virtualization smoke test failed: {exception.Message}");
        return 1;
    }
}

static async Task VerifyIconsAsync(DeterministicFakeIconProvider provider)
{
    await using var coordinator = new ExplorerIconCoordinator(provider);
    var location = ExplorerLocation.File("C:\\Smoke");
    long generation = coordinator.Invalidate();
    var items = Enumerable.Range(1, 24).Select(i => new ExplorerIconRequest((ulong)i, $"file{i}.{(i % 2 == 0 ? "txt" : "pdf")}", ExplorerItemKind.File, location)).ToArray();
    var applied = new List<ExplorerIconResult>();
    await coordinator.RequestAsync(location, items, generation, applied.Add);
    Assert(provider.BatchCount == 1 && provider.RequestCount == 2, "Icons were not requested as one coalesced batch.");
    Assert(applied.Count == 24, "The icon batch did not apply every current identity.");
    long staleGeneration = coordinator.Invalidate();
    var stale = new List<ExplorerIconResult>();
    await coordinator.RequestAsync(location, items, staleGeneration - 1, stale.Add);
    Assert(stale.Count == 0, "A stale icon generation was applied.");
    Assert(coordinator.CachedEntryCount <= 256, "The icon cache exceeded its bound.");
    Console.WriteLine("Icon smoke: batched placeholders, bounded cache, and stale row results passed");
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static void AssertThrows<TException>(Action action) where TException : Exception
{
    try { action(); }
    catch (TException) { return; }
    throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
}

file sealed class FakeSnapshot : IExplorerSnapshot
{
    private readonly ulong count;
    private bool disposed;

    public FakeSnapshot(ulong count) => this.count = count;
    public int DisposeCount { get; private set; }
    public ulong Count => count;

    public bool TryGetIndexByItemId(ulong itemId, out ulong index)
    {
        index = itemId == 0 ? 0 : itemId - 1;
        return itemId > 0 && index < count;
    }

    public ExplorerItemBatch GetRange(ulong start, uint requested)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (start > count) throw new ArgumentOutOfRangeException(nameof(start));
        uint actual = (uint)Math.Min((ulong)requested, count - start);
        var items = new ExplorerItem[actual];
        for (uint offset = 0; offset < actual; offset++)
        {
            ulong index = start + offset;
            items[offset] = new ExplorerItem(index + 1, $"Document {index:00000}", DateTimeOffset.UnixEpoch, 1, ExplorerItemKind.File);
        }
        return new ExplorerItemBatch(start, items);
    }

    public void Dispose()
    {
        if (!disposed)
        {
            disposed = true;
            DisposeCount++;
        }
    }
}
