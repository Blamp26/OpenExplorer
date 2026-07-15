using System.Collections;
using System.Collections.Generic;
using System.Globalization;

namespace OpenExplorer.Application.Diagnostics;

public sealed class SyntheticFileItemList : IList<SyntheticFileItem>, IReadOnlyList<SyntheticFileItem>, IList
{
    public const int DefaultLogicalItemCount = 100_000;
    public const int DefaultCacheCapacity = 1_024;

    private static readonly string[] Extensions = ["txt", "pdf", "jpg", "png", "zip", "exe", "dll", "mp4", ""];
    private readonly Dictionary<int, SyntheticFileItem> cache = [];
    private readonly Queue<int> cacheOrder = [];
    private readonly int cacheCapacity;
    private readonly int logicalItemCount;
    private int peakCachedItemCount;
    private int totalGeneratedItemCount;

    public SyntheticFileItemList(int logicalItemCount = DefaultLogicalItemCount, int cacheCapacity = DefaultCacheCapacity)
    {
        if (logicalItemCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(logicalItemCount), logicalItemCount, "The item count cannot be negative.");
        }

        if (cacheCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(cacheCapacity), cacheCapacity, "The cache capacity must be positive.");
        }

        this.logicalItemCount = logicalItemCount;
        this.cacheCapacity = cacheCapacity;
    }

    public int LogicalItemCount => logicalItemCount;

    public int CacheCapacity => cacheCapacity;

    public int CachedItemCount => cache.Count;

    public int PeakCachedItemCount => peakCachedItemCount;

    public int TotalGeneratedItemCount => totalGeneratedItemCount;

    public int Count => logicalItemCount;

    bool ICollection<SyntheticFileItem>.IsReadOnly => true;

    bool IList.IsReadOnly => true;

    bool IList.IsFixedSize => true;

    object ICollection.SyncRoot => this;

    bool ICollection.IsSynchronized => false;

    public SyntheticFileItem this[int index]
    {
        get
        {
            ValidateIndex(index);

            if (cache.TryGetValue(index, out SyntheticFileItem? item))
            {
                return item;
            }

            if (cache.Count >= cacheCapacity)
            {
                int evictedIndex = cacheOrder.Dequeue();
                cache.Remove(evictedIndex);
            }

            item = CreateItem(index);
            cache[index] = item;
            cacheOrder.Enqueue(index);
            totalGeneratedItemCount++;

            if (cache.Count > peakCachedItemCount)
            {
                peakCachedItemCount = cache.Count;
            }

            return item;
        }
        set => throw new NotSupportedException("The synthetic source is read-only.");
    }

    object? IList.this[int index]
    {
        get => this[index];
        set => throw new NotSupportedException("The synthetic source is read-only.");
    }

    public bool Contains(SyntheticFileItem item) => IndexOf(item) >= 0;

    public int IndexOf(SyntheticFileItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return item.Index >= 0 && item.Index < Count && ReferenceEquals(this[item.Index], item) ? item.Index : -1;
    }

    public void CopyTo(SyntheticFileItem[] array, int arrayIndex)
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

    public IEnumerator<SyntheticFileItem> GetEnumerator()
    {
        for (int index = 0; index < Count; index++)
        {
            yield return this[index];
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public void Add(SyntheticFileItem item) => throw new NotSupportedException("The synthetic source is read-only.");

    public void Clear() => throw new NotSupportedException("The synthetic source is read-only.");

    public void Insert(int index, SyntheticFileItem item) => throw new NotSupportedException("The synthetic source is read-only.");

    public bool Remove(SyntheticFileItem item) => throw new NotSupportedException("The synthetic source is read-only.");

    public void RemoveAt(int index) => throw new NotSupportedException("The synthetic source is read-only.");

    int IList.Add(object? value) => throw new NotSupportedException("The synthetic source is read-only.");

    bool IList.Contains(object? value) => value is SyntheticFileItem item && Contains(item);

    int IList.IndexOf(object? value) => value is SyntheticFileItem item ? IndexOf(item) : -1;

    void IList.Insert(int index, object? value) => throw new NotSupportedException("The synthetic source is read-only.");

    void IList.Remove(object? value) => throw new NotSupportedException("The synthetic source is read-only.");

    void IList.RemoveAt(int index) => throw new NotSupportedException("The synthetic source is read-only.");

    public void CopyTo(Array array, int index)
    {
        ArgumentNullException.ThrowIfNull(array);
        if (array is not SyntheticFileItem[] typedArray)
        {
            throw new ArgumentException("The destination array must contain SyntheticFileItem values.", nameof(array));
        }

        CopyTo(typedArray, index);
    }

    private void ValidateIndex(int index)
    {
        if ((uint)index >= (uint)logicalItemCount)
        {
            throw new ArgumentOutOfRangeException(nameof(index), index, "The index is outside the logical item range.");
        }
    }

    private static SyntheticFileItem CreateItem(int index)
    {
        bool isDirectory = index % 17 == 0;
        string extension = isDirectory ? string.Empty : Extensions[index % Extensions.Length];
        string name = isDirectory
            ? $"Folder {index:D5}"
            : extension.Length == 0
                ? $"Document {index:D5}"
                : $"Document {index:D5}.{extension}";
        string dateModified = DateTime.UnixEpoch
            .AddMinutes(index * 7L)
            .ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        string type = isDirectory ? "File folder" : GetTypeText(extension);
        string size = isDirectory
            ? "—"
            : FormatSize(1024L + (index * 7919L % 9_000_000L));

        return new SyntheticFileItem(index, name, dateModified, type, size, isDirectory);
    }

    private static string GetTypeText(string extension) => extension switch
    {
        "txt" => "Text document",
        "pdf" => "PDF document",
        "jpg" or "png" => "Image file",
        "zip" => "Compressed archive",
        "exe" => "Application",
        "dll" => "Application extension",
        "mp4" => "Video file",
        _ => "File"
    };

    private static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.0} KB",
        _ => $"{bytes / (1024.0 * 1024.0):0.0} MB"
    };
}
