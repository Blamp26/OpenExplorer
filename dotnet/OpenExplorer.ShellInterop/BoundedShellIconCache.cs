using System.Collections.Concurrent;

namespace OpenExplorer.ShellInterop;

public sealed class BoundedShellIconCache : IDisposable
{
    private readonly IShellIconExtractor _extractor;
    private readonly int _capacity;
    private readonly object _gate = new();
    private readonly Dictionary<ShellIconKey, ShellIconImage> _cache = new();
    private readonly Dictionary<ShellIconKey, Task<ShellIconImage?>> _pending = new();
    private bool _disposed;

    public BoundedShellIconCache(IShellIconExtractor extractor, int capacity = 256, int maxPending = 128)
    {
        _extractor = extractor ?? throw new ArgumentNullException(nameof(extractor));
        if (capacity < 1 || maxPending < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
        _capacity = capacity;
        MaxPending = maxPending;
    }

    public int MaxPending { get; }
    public int Count { get { lock (_gate) return _cache.Count; } }
    public int PendingCount { get { lock (_gate) return _pending.Count; } }

    public Task<ShellIconImage?> GetAsync(string path, ShellIconKind kind, CancellationToken cancellationToken = default)
    {
        var key = ShellIconKey.ForPath(path, kind);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_cache.TryGetValue(key, out var hit)) return Task.FromResult<ShellIconImage?>(hit);
            if (_pending.TryGetValue(key, out var existing)) return AwaitCancelable(existing, cancellationToken);
            if (_pending.Count >= MaxPending) return Task.FromResult<ShellIconImage?>(null);
            var task = Task.Run<ShellIconImage?>(() =>
            {
                try { return _extractor.Extract(path, kind); }
                catch { return null; }
            }, CancellationToken.None);
            _pending.Add(key, task);
            _ = CompleteAsync(key, task);
            return AwaitCancelable(task, cancellationToken);
        }
    }

    private static async Task<ShellIconImage?> AwaitCancelable(Task<ShellIconImage?> task, CancellationToken token) => await task.WaitAsync(token).ConfigureAwait(false);
    private async Task CompleteAsync(ShellIconKey key, Task<ShellIconImage?> task)
    {
        var image = await task.ConfigureAwait(false);
        lock (_gate)
        {
            _pending.Remove(key);
            if (image is null) return;
            if (_disposed)
            {
                image.Dispose();
                return;
            }
            if (_cache.Remove(key, out var old)) old.Dispose();
            _cache[key] = image;
            while (_cache.Count > _capacity)
            {
                var first = _cache.Keys.First();
                _cache[first].Dispose();
                _cache.Remove(first);
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var image in _cache.Values) image.Dispose();
            _cache.Clear();
        }
    }
}
