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

public enum SelectionMove
{
    Previous,
    Next,
    Home,
    End,
}
