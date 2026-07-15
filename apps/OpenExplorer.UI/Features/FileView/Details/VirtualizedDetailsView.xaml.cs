using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Input;
using OpenExplorer.Application.Diagnostics;
using OpenExplorer.Application.Icons;
using OpenExplorer.Application.Selection;
using OpenExplorer.Contracts;
using OpenExplorer_UI.Features.Performance;
using Windows.System;
using Windows.UI.Core;

namespace OpenExplorer_UI.Features.FileView.Details;

public sealed partial class VirtualizedDetailsView : UserControl
{
    private readonly Dictionary<ulong, string> renameTextById = [];
    private ulong? editingItemId;
    private bool operationBusy;

    public event Action<RenameCommitRequest>? RenameCommitRequested;
    public VirtualizedDetailsView()
    {
        InitializeComponent();
        DetailsRepeater.ElementPrepared += OnElementPrepared;
        DetailsRepeater.ElementClearing += OnElementClearing;
    }

    public SnapshotFileItemList? Items { get; private set; }

    public VirtualizationDiagnostics Diagnostics { get; } = new();

    private readonly HashSet<FrameworkElement> realizedRows = [];
    private ExplorerSelectionModel? selection;
    private ExplorerIconCoordinator? iconCoordinator;
    private ExplorerLocation? iconLocation;
    private readonly List<ExplorerIconRequest> pendingIconRequests = [];
    private bool iconFlushScheduled;
    private long iconGeneration;

    public event Action<ExplorerItem>? DirectoryActivated;

    public event Action<ExplorerSortField>? SortRequested;

    public void SetSelection(ExplorerSelectionModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (ReferenceEquals(selection, model)) return;
        if (selection is not null) selection.Changed -= OnSelectionChanged;
        selection = model;
        selection.Changed += OnSelectionChanged;
        RefreshRealizedRows();
    }

    public void SetIconProvider(ExplorerIconCoordinator coordinator)
    {
        ArgumentNullException.ThrowIfNull(coordinator);
        iconCoordinator = coordinator;
        iconGeneration = coordinator.Invalidate();
    }

    public void SetIconLocation(ExplorerLocation? location)
    {
        if (Equals(iconLocation, location)) return;
        iconLocation = location;
        iconGeneration = iconCoordinator?.Invalidate() ?? iconGeneration + 1;
        pendingIconRequests.Clear();
    }

    public void DetachSelection()
    {
        if (selection is null) return;
        selection.Changed -= OnSelectionChanged;
        selection = null;
    }

    public void SetItems(SnapshotFileItemList items)
    {
        ArgumentNullException.ThrowIfNull(items);
        double previousOffset = DetailsScrollViewer.VerticalOffset;
        Items = items;
        DetailsRepeater.ItemsSource = Items;
        iconGeneration = iconCoordinator?.Invalidate() ?? iconGeneration + 1;
        pendingIconRequests.Clear();
        Diagnostics.Reset();
        DispatcherQueue.TryEnqueue(() => RestoreViewport(previousOffset));
    }

    public void ClearItems()
    {
        Items = null;
        DetailsRepeater.ItemsSource = null;
        realizedRows.Clear();
        iconGeneration = iconCoordinator?.Invalidate() ?? iconGeneration + 1;
        pendingIconRequests.Clear();
    }

    public void FocusView() => DetailsScrollViewer.Focus(FocusState.Programmatic);

    public void SetOperationBusy(bool busy) { operationBusy = busy; }

    public void BeginRename(ExplorerItem item)
    {
        if (operationBusy) return;
        editingItemId = item.ItemId;
        renameTextById[item.ItemId] = item.Kind == ExplorerItemKind.File
            ? Path.GetFileNameWithoutExtension(item.Name)
            : item.Name;
        RefreshRealizedRows();
        if (Items?.TryGetIndexByItemId(item.ItemId, out ulong index) == true && index <= int.MaxValue)
        {
            UIElement element = DetailsRepeater.GetOrCreateElement((int)index);
            element.Focus(FocusState.Programmatic);
            if (FindRenameBox(element) is { } box) { box.Focus(FocusState.Programmatic); box.SelectAll(); }
        }
    }

    public void BeginRename(ulong itemId)
    {
        if (Items?.TryGetIndexByItemId(itemId, out ulong index) != true || index > int.MaxValue) return;
        BeginRename(Items.GetSourceItem((int)index));
    }

    public void ShowRenameError(ulong itemId, string message)
    {
        if (editingItemId != itemId) return;
        // Keep the identity-keyed editor text intact; the page owns the visible error.
    }

    public void FocusItem(ulong itemId)
    {
        if (Items?.TryGetIndexByItemId(itemId, out ulong index) == true && index <= int.MaxValue) FocusLogicalIndex((int)index);
    }

    public void SetSortOptions(ExplorerSortOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        string Arrow(ExplorerSortField field) => options.Field == field
            ? options.Direction == ExplorerSortDirection.Ascending ? " ↑" : " ↓"
            : string.Empty;
        NameHeaderButton.Content = $"Name{Arrow(ExplorerSortField.Name)}";
        DateModifiedHeaderButton.Content = $"Date modified{Arrow(ExplorerSortField.DateModified)}";
        TypeHeaderButton.Content = $"Type{Arrow(ExplorerSortField.Type)}";
        SizeHeaderButton.Content = $"Size{Arrow(ExplorerSortField.Size)}";
    }

    public void SetSortEnabled(bool enabled)
    {
        NameHeaderButton.IsEnabled = enabled;
        DateModifiedHeaderButton.IsEnabled = enabled;
        TypeHeaderButton.IsEnabled = enabled;
        SizeHeaderButton.IsEnabled = enabled;
    }

    private void OnRowDoubleTapped(object sender, DoubleTappedRoutedEventArgs args)
    {
        if (sender is FrameworkElement { DataContext: SnapshotFileItem item } && item.IsDirectory)
        {
            DirectoryActivated?.Invoke(item.SourceItem);
        }
        args.Handled = true;
    }

    private void OnRowTapped(object sender, TappedRoutedEventArgs args)
    {
        if (selection is null || Items is null || sender is not FrameworkElement row || row.DataContext is not SnapshotFileItem item)
        {
            return;
        }

        int index = DetailsRepeater.GetElementIndex(row);
        if (index < 0) return;
        bool control = IsDown(VirtualKey.Control);
        bool shift = IsDown(VirtualKey.Shift);
        if (shift)
        {
            selection.SelectRange(Items, index, toggleRange: control);
        }
        else if (control)
        {
            selection.Toggle(item.SourceItem);
        }
        else
        {
            selection.SelectSingle(item.SourceItem);
        }

        row.Focus(FocusState.Pointer);
        args.Handled = true;
    }

    private void OnRowKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (selection is null || Items is null) return;
        bool shift = IsDown(VirtualKey.Shift);
        switch (args.Key)
        {
            case VirtualKey.A when IsDown(VirtualKey.Control):
                selection.SelectAll(Items.LogicalItemCount);
                args.Handled = true;
                break;
            case VirtualKey.Escape:
                selection.Clear();
                args.Handled = true;
                break;
            case VirtualKey.Left:
            case VirtualKey.Up:
                FocusLogicalIndex(selection.MoveFocus(Items, SelectionMove.Previous, shift));
                args.Handled = true;
                break;
            case VirtualKey.Right:
            case VirtualKey.Down:
                FocusLogicalIndex(selection.MoveFocus(Items, SelectionMove.Next, shift));
                args.Handled = true;
                break;
            case VirtualKey.Home:
                FocusLogicalIndex(selection.MoveFocus(Items, SelectionMove.Home, shift));
                args.Handled = true;
                break;
            case VirtualKey.End:
                FocusLogicalIndex(selection.MoveFocus(Items, SelectionMove.End, shift));
                args.Handled = true;
                break;
        }
    }

    private void OnSortHeaderClick(object sender, RoutedEventArgs args)
    {
        if (sender is Button { Tag: string tag } && Enum.TryParse(tag, out ExplorerSortField field))
        {
            SortRequested?.Invoke(field);
        }
    }

    private void OnElementPrepared(ItemsRepeater sender, ItemsRepeaterElementPreparedEventArgs args)
    {
        Diagnostics.RecordPrepared();
        if (args.Element is FrameworkElement element)
        {
            realizedRows.Add(element);
            ApplyRowState(element);
            ApplyRenameState(element);
            if (element.DataContext is SnapshotFileItem item) QueueIcon(item, DetailsRepeater.GetElementIndex(element));
        }
    }

    private void OnElementClearing(ItemsRepeater sender, ItemsRepeaterElementClearingEventArgs args)
    {
        Diagnostics.RecordCleared();
        if (args.Element is FrameworkElement element)
        {
            realizedRows.Remove(element);
            if (FindRenameBox(element) is { } box) { box.Text = string.Empty; box.Visibility = Visibility.Collapsed; }
            element.ClearValue(FrameworkElement.TagProperty);
        }
    }

    private void QueueIcon(SnapshotFileItem item, int index)
    {
        if (iconCoordinator is null || iconLocation is null || index < 0) return;
        pendingIconRequests.Add(new ExplorerIconRequest(item.ItemId, item.Name, item.SourceItem.Kind, iconLocation));
        // A small look-ahead keeps fast scrolling warm without enumerating the directory.
        if (Items is not null)
        {
            for (int i = index + 1; i <= index + 16 && i < Items.Count; i++)
            {
                SnapshotFileItem near = Items[i];
                pendingIconRequests.Add(new ExplorerIconRequest(near.ItemId, near.Name, near.SourceItem.Kind, iconLocation));
            }
        }
        if (iconFlushScheduled) return;
        iconFlushScheduled = true;
        DispatcherQueue.TryEnqueue(async () => await FlushIconsAsync());
    }

    private async Task FlushIconsAsync()
    {
        iconFlushScheduled = false;
        if (iconCoordinator is null || iconLocation is null || pendingIconRequests.Count == 0) return;
        ExplorerIconRequest[] requests = pendingIconRequests.DistinctBy(x => x.ItemId).Take(512).ToArray();
        pendingIconRequests.Clear();
        long generation = iconGeneration;
        await iconCoordinator.RequestAsync(iconLocation, requests, generation, result =>
        {
            if (generation != iconGeneration || Items is null || !Items.TryGetIndexByItemId(result.ItemId, out _)) return;
            foreach (FrameworkElement row in realizedRows)
                if (row.DataContext is SnapshotFileItem item && item.ItemId == result.ItemId) item.SetIcon(result);
        });
    }

    private void OnSelectionChanged(object? sender, EventArgs args) => RefreshRealizedRows();

    private void RefreshRealizedRows()
    {
        foreach (FrameworkElement row in realizedRows) { ApplyRowState(row); ApplyRenameState(row); }
    }

    private void ApplyRenameState(FrameworkElement row)
    {
        if (row.DataContext is not SnapshotFileItem item || FindRenameBox(row) is not { } box) return;
        bool editing = editingItemId == item.ItemId;
        box.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
        if (editing && renameTextById.TryGetValue(item.ItemId, out string? text) && box.Text != text) box.Text = text;
    }

    private static TextBox? FindRenameBox(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, i);
            if (child is TextBox box) return box;
            if (FindRenameBox(child) is { } nested) return nested;
        }
        return null;
    }

    protected override void OnKeyDown(KeyRoutedEventArgs args)
    {
        base.OnKeyDown(args);
        if (editingItemId is not ulong id || Items is null || !Items.TryGetIndexByItemId(id, out ulong index) || index > int.MaxValue) return;
        if (args.Key == VirtualKey.Escape)
        {
            editingItemId = null; renameTextById.Remove(id); RefreshRealizedRows(); FocusView(); args.Handled = true;
        }
        else if (args.Key == VirtualKey.Enter && FindRenameBox(DetailsRepeater.GetOrCreateElement((int)index)) is { } box)
        {
            ExplorerItem item = Items.GetSourceItem((int)index);
            editingItemId = null; renameTextById.Remove(id); RefreshRealizedRows();
            RenameCommitRequested?.Invoke(new RenameCommitRequest(id, item.Name, item.Kind, box.Text));
            args.Handled = true;
        }
    }

    private void ApplyRowState(FrameworkElement row)
    {
        if (row is not Control control || selection is null || row.DataContext is not SnapshotFileItem item) return;
        bool selected = selection.IsSelected(item.ItemId);
        control.Background = selected
            ? (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["OpenExplorerDetailsSelectionBackground"]
            : null;
        control.BorderBrush = selection.FocusedItemId == item.ItemId
            ? (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["OpenExplorerDetailsFocusBorder"]
            : null;
        control.BorderThickness = selection.FocusedItemId == item.ItemId ? new Thickness(1, 0, 1, 0) : new Thickness(0);
    }

    private static bool IsDown(VirtualKey key) =>
        (InputKeyboardSource.GetKeyStateForCurrentThread(key) & CoreVirtualKeyStates.Down) != 0;

    private void FocusLogicalIndex(int? index)
    {
        if (!index.HasValue || Items is null) return;
        double rowTop = 34 + (index.Value * 32.0);
        double rowBottom = rowTop + 32;
        double viewportTop = DetailsScrollViewer.VerticalOffset;
        double viewportBottom = viewportTop + DetailsScrollViewer.ViewportHeight;
        double targetOffset = rowTop < viewportTop
            ? rowTop
            : rowBottom > viewportBottom
                ? rowBottom - DetailsScrollViewer.ViewportHeight
                : viewportTop;
        DetailsScrollViewer.ChangeView(null, Math.Max(0, targetOffset), null);
        DispatcherQueue.TryEnqueue(() =>
        {
            if (Items is null || (uint)index.Value >= (uint)Items.Count) return;
            UIElement element = DetailsRepeater.GetOrCreateElement(index.Value);
            element.Focus(FocusState.Keyboard);
        });
    }

    private void RestoreViewport(double previousOffset)
    {
        if (Items is null) return;
        DetailsScrollViewer.ChangeView(null, Math.Max(0, previousOffset), null);
        if (selection?.FocusedItemId is not ulong focusedId ||
            !Items.TryGetIndexByItemId(focusedId, out ulong focusedIndex) ||
            focusedIndex > int.MaxValue)
        {
            return;
        }

        FocusLogicalIndex((int)focusedIndex);
    }
}

public sealed record RenameCommitRequest(ulong ItemId, string OriginalName, ExplorerItemKind Kind, string Name);
