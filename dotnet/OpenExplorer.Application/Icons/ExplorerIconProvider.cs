using OpenExplorer.Contracts;

namespace OpenExplorer.Application.Icons;

public enum ExplorerIconKind { GenericFile, GenericFolder, ShellFile, ShellFolder }

public sealed record ExplorerIconRequest(ulong ItemId, string Name, ExplorerItemKind ItemKind, ExplorerLocation Location)
{
    public string CacheKey => ItemKind == ExplorerItemKind.Directory
        ? "folder"
        : $"file:{Path.GetExtension(Name).ToUpperInvariant()}";
}

public sealed record ExplorerIconResult(ulong ItemId, ExplorerIconKind Kind, string PresentationKey);

/// Infrastructure implements this with the isolated ShellHost. Application code never sees pipes,
/// processes, handles, or image buffers.
public interface IExplorerIconProvider : IAsyncDisposable
{
    ValueTask<IReadOnlyList<ExplorerIconResult>> RequestBatchAsync(
        IReadOnlyList<ExplorerIconRequest> requests, CancellationToken cancellationToken);
}

public sealed class ExplorerIconCoordinator : IAsyncDisposable
{
    private const int MaximumCacheEntries = 256;
    private const int MaximumQueuedItems = 512;
    private readonly IExplorerIconProvider provider;
    private readonly Dictionary<string, ExplorerIconKind> cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Task<ExplorerIconKind>> inFlight = new(StringComparer.OrdinalIgnoreCase);
    private readonly object gate = new();
    private long generation;
    private bool disposed;

    public ExplorerIconCoordinator(IExplorerIconProvider provider) => this.provider = provider ?? throw new ArgumentNullException(nameof(provider));

    public int CachedEntryCount { get { lock (gate) return cache.Count; } }
    public int InFlightCount { get { lock (gate) return inFlight.Count; } }

    public long Invalidate()
    {
        lock (gate) { generation++; }
        return generation;
    }

    public async Task RequestAsync(ExplorerLocation location, IReadOnlyList<ExplorerIconRequest> requests,
        long requestGeneration, Action<ExplorerIconResult> apply, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(location);
        ArgumentNullException.ThrowIfNull(requests);
        ArgumentNullException.ThrowIfNull(apply);
        if (requests.Count == 0 || requestGeneration != CurrentGeneration) return;

        var selected = requests.Take(MaximumQueuedItems).DistinctBy(r => r.ItemId).ToArray();
        var pending = new List<(ExplorerIconRequest Request, Task<ExplorerIconKind> Task)>();
        var newRequests = new List<ExplorerIconRequest>();
        foreach (ExplorerIconRequest request in selected)
        {
            string key = request.CacheKey;
            lock (gate)
            {
                if (cache.TryGetValue(key, out ExplorerIconKind cached))
                {
                    if (requestGeneration == CurrentGeneration) apply(new ExplorerIconResult(request.ItemId, cached, key));
                    continue;
                }

                if (!inFlight.TryGetValue(key, out Task<ExplorerIconKind>? task))
                {
                    task = NewFetchTask(request, key, newRequests);
                    inFlight[key] = task;
                }
                pending.Add((request, task));
            }
        }

        // The provider receives one bounded batch for all newly visible cache keys.
        // NewFetchTask registers shared completion tasks; no row performs a synchronous round trip.
        if (newRequests.Count > 0) _ = CompleteBatchAsync(newRequests, cancellationToken);

        foreach ((ExplorerIconRequest request, Task<ExplorerIconKind> task) in pending)
        {
            ExplorerIconKind kind;
            try { kind = await task.ConfigureAwait(false); }
            catch { kind = request.ItemKind == ExplorerItemKind.Directory ? ExplorerIconKind.GenericFolder : ExplorerIconKind.GenericFile; }
            if (requestGeneration == CurrentGeneration)
                apply(new ExplorerIconResult(request.ItemId, kind, request.CacheKey));
        }
    }

    private Task<ExplorerIconKind> NewFetchTask(ExplorerIconRequest request, string key, List<ExplorerIconRequest> batch)
    {
        var completion = new TaskCompletionSource<ExplorerIconKind>(TaskCreationOptions.RunContinuationsAsynchronously);
        batch.Add(request);
        lock (gate) pendingCompletions[key] = completion;
        return completion.Task;
    }

    private readonly Dictionary<string, TaskCompletionSource<ExplorerIconKind>> pendingCompletions = new(StringComparer.OrdinalIgnoreCase);

    private async Task CompleteBatchAsync(IReadOnlyList<ExplorerIconRequest> requests, CancellationToken cancellationToken)
    {
        try
        {
            IReadOnlyList<ExplorerIconResult> result = await provider.RequestBatchAsync(requests, cancellationToken).ConfigureAwait(false);
            var byId = result.ToDictionary(x => x.ItemId);
            foreach (ExplorerIconRequest request in requests)
            {
                ExplorerIconKind kind = byId.TryGetValue(request.ItemId, out ExplorerIconResult? icon)
                    ? icon.Kind
                    : request.ItemKind == ExplorerItemKind.Directory ? ExplorerIconKind.GenericFolder : ExplorerIconKind.GenericFile;
                Complete(request.CacheKey, kind);
            }
        }
        catch
        {
            foreach (ExplorerIconRequest request in requests)
                Complete(request.CacheKey, request.ItemKind == ExplorerItemKind.Directory ? ExplorerIconKind.GenericFolder : ExplorerIconKind.GenericFile);
        }
    }

    private void Complete(string key, ExplorerIconKind kind)
    {
        TaskCompletionSource<ExplorerIconKind>? completion;
        lock (gate)
        {
            pendingCompletions.Remove(key, out completion);
            inFlight.Remove(key);
            if (cache.Count >= MaximumCacheEntries) cache.Remove(cache.Keys.First());
            cache[key] = kind;
        }
        completion?.TrySetResult(kind);
    }

    private long CurrentGeneration { get { lock (gate) return generation; } }

    public async ValueTask DisposeAsync()
    {
        lock (gate) { if (disposed) return; disposed = true; generation++; }
        await provider.DisposeAsync().ConfigureAwait(false);
    }
}

public sealed class DeterministicFakeIconProvider : IExplorerIconProvider
{
    public int BatchCount { get; private set; }
    public int RequestCount { get; private set; }
    public bool FailRequests { get; set; }

    public ValueTask<IReadOnlyList<ExplorerIconResult>> RequestBatchAsync(IReadOnlyList<ExplorerIconRequest> requests, CancellationToken cancellationToken)
    {
        BatchCount++;
        RequestCount += requests.Count;
        if (FailRequests) throw new IOException("fake ShellHost unavailable");
        IReadOnlyList<ExplorerIconResult> result = requests.Select(r => new ExplorerIconResult(
            r.ItemId, r.ItemKind == ExplorerItemKind.Directory ? ExplorerIconKind.ShellFolder : ExplorerIconKind.ShellFile, r.CacheKey)).ToArray();
        return ValueTask.FromResult(result);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

public sealed class FallbackIconProvider : IExplorerIconProvider
{
    public ValueTask<IReadOnlyList<ExplorerIconResult>> RequestBatchAsync(IReadOnlyList<ExplorerIconRequest> requests, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<ExplorerIconResult>>(requests.Select(r => new ExplorerIconResult(r.ItemId,
            r.ItemKind == ExplorerItemKind.Directory ? ExplorerIconKind.GenericFolder : ExplorerIconKind.GenericFile, r.CacheKey)).ToArray());
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
