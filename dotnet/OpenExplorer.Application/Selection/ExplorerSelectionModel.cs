using OpenExplorer.Application.Diagnostics;
using OpenExplorer.Contracts;

namespace OpenExplorer.Application.Selection;

public sealed class ExplorerSelectionModel : IDisposable
{
    private readonly HashSet<ulong> selectedIds = [];
    private readonly HashSet<ulong> deselectedAllIds = [];
    private bool allSelected;
    private ulong logicalItemCount;
    private ulong? anchorItemId;
    private ulong? focusedItemId;
    private bool disposed;

    public event EventHandler? Changed;

    public bool IsAllSelected { get { ThrowIfDisposed(); return allSelected; } }
    public ulong LogicalItemCount { get { ThrowIfDisposed(); return logicalItemCount; } }
    public ulong? AnchorItemId { get { ThrowIfDisposed(); return anchorItemId; } }
    public ulong? FocusedItemId { get { ThrowIfDisposed(); return focusedItemId; } }

    /// <summary>Returns the recorded selection state without enumerating a snapshot.</summary>
    public ExplorerSelectionState CaptureState()
    {
        ThrowIfDisposed();
        return new ExplorerSelectionState(
            allSelected,
            selectedIds.ToArray(),
            deselectedAllIds.ToArray(),
            anchorItemId,
            focusedItemId,
            logicalItemCount);
    }

    /// <summary>
    /// Returns only explicitly recorded selected IDs. An inverted Select All is
    /// intentionally not expanded because doing so would require a directory scan.
    /// </summary>
    public bool TryGetExplicitSelectedIds(out IReadOnlyList<ulong> itemIds)
    {
        ThrowIfDisposed();
        if (allSelected)
        {
            itemIds = Array.Empty<ulong>();
            return false;
        }

        itemIds = selectedIds.ToArray();
        return true;
    }

    /// <summary>
    /// Resolves explicit selection targets through the snapshot's stable-ID
    /// lookup. This is bounded by the recorded selection and never scans pages.
    /// Inverted Select All is deliberately rejected because expanding it would
    /// materialize the directory.
    /// </summary>
    public bool TryGetExplicitSelectedItems(IExplorerSnapshot snapshot, out IReadOnlyList<ExplorerItem> items)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!TryGetExplicitSelectedIds(out IReadOnlyList<ulong> ids))
        {
            items = Array.Empty<ExplorerItem>();
            return false;
        }

        var resolved = new List<ExplorerItem>(ids.Count);
        foreach (ulong itemId in ids)
        {
            if (!snapshot.TryGetIndexByItemId(itemId, out ulong index)) continue;
            ExplorerItemBatch batch = snapshot.GetRange(index, 1);
            if (batch.Items.Count == 1 && batch.Items[0].ItemId == itemId)
            {
                resolved.Add(batch.Items[0]);
            }
        }

        items = resolved;
        return true;
    }

    /// <summary>Returns an explicitly selected item by stable ID using one native lookup.</summary>
    public bool TrySelectItem(IExplorerSnapshot snapshot, ulong itemId)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!snapshot.TryGetIndexByItemId(itemId, out ulong index)) return false;
        ExplorerItemBatch batch = snapshot.GetRange(index, 1);
        if (batch.Items.Count != 1 || batch.Items[0].ItemId != itemId) return false;
        SelectSingle(batch.Items[0]);
        return true;
    }

    public bool TrySelectItem(SnapshotFileItemList items, ulong itemId)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(items);
        if (!items.TryGetIndexByItemId(itemId, out ulong index) || index > int.MaxValue) return false;
        SelectSingle(items.GetSourceItem(checked((int)index)));
        return true;
    }

    public void SetLogicalItemCount(ulong count)
    {
        ThrowIfDisposed();
        logicalItemCount = count;
        if (!allSelected) return;
        selectedIds.Clear();
    }

    public bool IsSelected(ulong itemId)
    {
        ThrowIfDisposed();
        return allSelected ? !deselectedAllIds.Contains(itemId) : selectedIds.Contains(itemId);
    }

    public void SelectSingle(ExplorerItem item)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(item);
        selectedIds.Clear();
        deselectedAllIds.Clear();
        allSelected = false;
        selectedIds.Add(item.ItemId);
        anchorItemId = item.ItemId;
        focusedItemId = item.ItemId;
        RaiseChanged();
    }

    public void Toggle(ExplorerItem item)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(item);
        if (allSelected)
        {
            if (!deselectedAllIds.Add(item.ItemId)) deselectedAllIds.Remove(item.ItemId);
        }
        else if (!selectedIds.Add(item.ItemId))
        {
            selectedIds.Remove(item.ItemId);
        }
        anchorItemId ??= item.ItemId;
        focusedItemId = item.ItemId;
        RaiseChanged();
    }

    // Ctrl+Shift toggles the complete anchor-to-click range and preserves the anchor.
    public void SelectRange(SnapshotFileItemList items, int clickedIndex, bool toggleRange)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(items);
        if ((uint)clickedIndex >= (uint)items.Count) throw new ArgumentOutOfRangeException(nameof(clickedIndex));

        int anchorIndex = anchorItemId.HasValue && items.TryGetIndexByItemId(anchorItemId.Value, out ulong anchorLogicalIndex) && anchorLogicalIndex <= int.MaxValue
            ? (int)anchorLogicalIndex
            : -1;
        if (anchorIndex < 0) anchorIndex = clickedIndex;
        int first = Math.Min(anchorIndex, clickedIndex);
        int last = Math.Max(anchorIndex, clickedIndex);
        ulong clickedId = items.GetSourceItem(clickedIndex).ItemId;

        if (!toggleRange)
        {
            selectedIds.Clear();
            deselectedAllIds.Clear();
            allSelected = false;
        }

        for (int index = first; index <= last; index++)
        {
            ulong itemId = items.GetSourceItem(index).ItemId;
            if (toggleRange)
            {
                if (allSelected)
                {
                    if (!deselectedAllIds.Add(itemId)) deselectedAllIds.Remove(itemId);
                }
                else if (!selectedIds.Add(itemId)) selectedIds.Remove(itemId);
            }
            else
            {
                selectedIds.Add(itemId);
            }
        }

        focusedItemId = clickedId;
        anchorItemId ??= clickedId;
        RaiseChanged();
    }

    public void SelectAll(ulong count)
    {
        ThrowIfDisposed();
        logicalItemCount = count;
        selectedIds.Clear();
        deselectedAllIds.Clear();
        allSelected = true;
        RaiseChanged();
    }

    /// <summary>
    /// Reconciles identity-based selection state with a newly opened snapshot.
    /// Only IDs already recorded by the selection model are queried; the snapshot
    /// is never paged or enumerated.
    /// </summary>
    public void Reconcile(IExplorerSnapshot snapshot)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(snapshot);

        HashSet<ulong> retainedSelected = [];
        HashSet<ulong> retainedExceptions = [];
        foreach (ulong itemId in selectedIds)
        {
            if (snapshot.TryGetIndexByItemId(itemId, out _)) retainedSelected.Add(itemId);
        }

        foreach (ulong itemId in deselectedAllIds)
        {
            if (snapshot.TryGetIndexByItemId(itemId, out _)) retainedExceptions.Add(itemId);
        }

        ulong? retainedAnchor = anchorItemId.HasValue && snapshot.TryGetIndexByItemId(anchorItemId.Value, out _)
            ? anchorItemId
            : null;
        ulong? retainedFocus = focusedItemId.HasValue && snapshot.TryGetIndexByItemId(focusedItemId.Value, out _)
            ? focusedItemId
            : null;

        bool changed = logicalItemCount != snapshot.Count
            || !selectedIds.SetEquals(retainedSelected)
            || !deselectedAllIds.SetEquals(retainedExceptions)
            || anchorItemId != retainedAnchor
            || focusedItemId != retainedFocus;

        logicalItemCount = snapshot.Count;
        selectedIds.Clear();
        selectedIds.UnionWith(retainedSelected);
        deselectedAllIds.Clear();
        deselectedAllIds.UnionWith(retainedExceptions);
        anchorItemId = retainedAnchor;
        focusedItemId = retainedFocus;

        if (changed) RaiseChanged();
    }

    public void Clear()
    {
        ThrowIfDisposed();
        bool changed = allSelected || selectedIds.Count != 0 || deselectedAllIds.Count != 0 || anchorItemId.HasValue || focusedItemId.HasValue;
        allSelected = false;
        selectedIds.Clear();
        deselectedAllIds.Clear();
        anchorItemId = null;
        focusedItemId = null;
        if (changed) RaiseChanged();
    }

    public int? MoveFocus(SnapshotFileItemList items, SelectionMove move, bool extendSelection)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count == 0) return null;
        int currentIndex = focusedItemId.HasValue && items.TryGetIndexByItemId(focusedItemId.Value, out ulong currentLogicalIndex)
            ? checked((int)currentLogicalIndex)
            : -1;
        int target = move switch
        {
            SelectionMove.Home => 0,
            SelectionMove.End => items.Count - 1,
            SelectionMove.Previous => Math.Max(0, currentIndex < 0 ? 0 : currentIndex - 1),
            SelectionMove.Next => Math.Min(items.Count - 1, currentIndex < 0 ? 0 : currentIndex + 1),
            _ => throw new ArgumentOutOfRangeException(nameof(move)),
        };

        if (extendSelection)
        {
            SelectRange(items, target, toggleRange: false);
            return target;
        }

        SelectSingle(items.GetSourceItem(target));
        return target;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        selectedIds.Clear();
        deselectedAllIds.Clear();
        Changed = null;
        GC.SuppressFinalize(this);
    }

    private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(disposed, this);
}

public sealed record ExplorerSelectionState(
    bool IsAllSelected,
    IReadOnlyList<ulong> SelectedIds,
    IReadOnlyList<ulong> DeselectedAllIds,
    ulong? AnchorItemId,
    ulong? FocusedItemId,
    ulong LogicalItemCount);

public enum SelectionMove
{
    Previous,
    Next,
    Home,
    End,
}
