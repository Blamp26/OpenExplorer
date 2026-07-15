using System.Collections;
using OpenExplorer.Contracts;

namespace OpenExplorer.Application.Diagnostics;

public sealed class SnapshotFileItemList : IList<SnapshotFileItem>, IReadOnlyList<SnapshotFileItem>, IList, IDisposable
{
    public const int DefaultPageSize = 256;
    public const int DefaultMaximumCachedPages = 4;
    public const int DefaultMaximumCachedItems = 1_024;

    private readonly IExplorerSnapshot snapshot;
    private readonly int pageSize;
    private readonly int maximumCachedPages;
    private readonly Dictionary<ulong, CachedPage> pages = [];
    private readonly LinkedList<ulong> lru = [];
    private bool disposed;
    private int cachedItemCount;
    private int peakCachedItemCount;
    private long rangeRequestCount;
    private long totalItemsReceived;

    public SnapshotFileItemList(
        IExplorerSnapshot snapshot,
        int pageSize = DefaultPageSize,
        int maximumCachedPages = DefaultMaximumCachedPages)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (pageSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pageSize));
        }
        if (maximumCachedPages <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumCachedPages));
        }
        if ((long)pageSize * maximumCachedPages > DefaultMaximumCachedItems)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumCachedPages), "The configured cache cannot exceed 1,024 items.");
        }

        this.snapshot = snapshot;
        this.pageSize = pageSize;
        this.maximumCachedPages = maximumCachedPages;
    }

    public ulong LogicalItemCount { get { ThrowIfDisposed(); return snapshot.Count; } }

    public int PageSize { get { ThrowIfDisposed(); return pageSize; } }

    public int MaximumCachedPages { get { ThrowIfDisposed(); return maximumCachedPages; } }

    public int CurrentCachedPages { get { ThrowIfDisposed(); return pages.Count; } }

    public int CurrentCachedItemCount { get { ThrowIfDisposed(); return cachedItemCount; } }

    public int PeakCachedItemCount { get { ThrowIfDisposed(); return peakCachedItemCount; } }

    public long RangeRequestCount { get { ThrowIfDisposed(); return rangeRequestCount; } }

    public long TotalItemsReceived { get { ThrowIfDisposed(); return totalItemsReceived; } }

    public int Count { get { ThrowIfDisposed(); return checked((int)Math.Min(LogicalItemCount, int.MaxValue)); } }

    bool ICollection<SnapshotFileItem>.IsReadOnly => true;

    bool IList.IsReadOnly => true;

    bool IList.IsFixedSize => true;

    object ICollection.SyncRoot => this;

    bool ICollection.IsSynchronized => false;

    public SnapshotFileItem this[int index]
    {
        get
        {
            ThrowIfDisposed();
            if ((uint)index >= (uint)Count)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }
            return GetPage((ulong)index / (uint)pageSize)[index % pageSize];
        }
        set => throw new NotSupportedException("The snapshot source is read-only.");
    }

    object? IList.this[int index]
    {
        get => this[index];
        set => throw new NotSupportedException("The snapshot source is read-only.");
    }

    public IEnumerator<SnapshotFileItem> GetEnumerator()
    {
        for (int index = 0; index < Count; index++)
        {
            yield return this[index];
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public bool Contains(SnapshotFileItem item) => IndexOf(item) >= 0;

    public int IndexOf(SnapshotFileItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return -1;
    }

    public void CopyTo(SnapshotFileItem[] array, int arrayIndex)
    {
        ArgumentNullException.ThrowIfNull(array);
        if (arrayIndex < 0 || array.Length - arrayIndex < Count)
        {
            throw new ArgumentException("The destination array is too small.", nameof(array));
        }
        for (int index = 0; index < Count; index++)
        {
            array[arrayIndex + index] = this[index];
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        pages.Clear();
        lru.Clear();
        cachedItemCount = 0;
        snapshot.Dispose();
        GC.SuppressFinalize(this);
    }

    public void Add(SnapshotFileItem item) => throw new NotSupportedException();
    public void Clear() => throw new NotSupportedException();
    public void Insert(int index, SnapshotFileItem item) => throw new NotSupportedException();
    public bool Remove(SnapshotFileItem item) => throw new NotSupportedException();
    public void RemoveAt(int index) => throw new NotSupportedException();
    int IList.Add(object? value) => throw new NotSupportedException();
    bool IList.Contains(object? value) => value is SnapshotFileItem item && Contains(item);
    int IList.IndexOf(object? value) => value is SnapshotFileItem item ? IndexOf(item) : -1;
    void IList.Insert(int index, object? value) => throw new NotSupportedException();
    void IList.Remove(object? value) => throw new NotSupportedException();
    void IList.RemoveAt(int index) => throw new NotSupportedException();
    void ICollection.CopyTo(Array array, int index)
    {
        if (array is not SnapshotFileItem[] typedArray)
        {
            throw new ArgumentException("The destination array must contain SnapshotFileItem values.", nameof(array));
        }
        CopyTo(typedArray, index);
    }

    private SnapshotFileItem[] GetPage(ulong pageIndex)
    {
        if (pages.TryGetValue(pageIndex, out CachedPage? cached))
        {
            lru.Remove(cached.Node);
            cached.Node = lru.AddLast(pageIndex);
            return cached.Items;
        }

        ulong start = checked(pageIndex * (uint)pageSize);
        uint requested = (uint)Math.Min((ulong)pageSize, LogicalItemCount - start);
        rangeRequestCount++;
        ExplorerItemBatch batch = snapshot.GetRange(start, requested);
        if (batch.StartIndex != start || batch.Items.Count != requested)
        {
            throw new InvalidOperationException("The snapshot returned an inconsistent page.");
        }
        var items = new SnapshotFileItem[batch.Items.Count];
        for (int index = 0; index < items.Length; index++)
        {
            items[index] = new SnapshotFileItem(batch.Items[index]);
        }

        var newPage = new CachedPage(items, lru.AddLast(pageIndex));
        pages.Add(pageIndex, newPage);
        cachedItemCount += items.Length;
        peakCachedItemCount = Math.Max(peakCachedItemCount, cachedItemCount);
        while (pages.Count > maximumCachedPages)
        {
            ulong evictedIndex = lru.First!.Value;
            lru.RemoveFirst();
            CachedPage evicted = pages[evictedIndex];
            pages.Remove(evictedIndex);
            cachedItemCount -= evicted.Items.Length;
        }
        totalItemsReceived += items.Length;
        return items;
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(disposed, this);

    private sealed class CachedPage(SnapshotFileItem[] items, LinkedListNode<ulong> node)
    {
        public SnapshotFileItem[] Items { get; } = items;
        public LinkedListNode<ulong> Node { get; set; } = node;
    }
}
