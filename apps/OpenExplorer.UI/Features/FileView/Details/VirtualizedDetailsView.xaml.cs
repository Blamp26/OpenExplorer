using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Input;
using OpenExplorer.Application.Diagnostics;
using OpenExplorer.Application.Selection;
using OpenExplorer.Contracts;
using OpenExplorer_UI.Features.Performance;
using Windows.System;
using Windows.UI.Core;

namespace OpenExplorer_UI.Features.FileView.Details;

public sealed partial class VirtualizedDetailsView : UserControl
{
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

    public void DetachSelection()
    {
        if (selection is null) return;
        selection.Changed -= OnSelectionChanged;
        selection = null;
    }

    public void SetItems(SnapshotFileItemList items)
    {
        ArgumentNullException.ThrowIfNull(items);
        Items = items;
        DetailsRepeater.ItemsSource = Items;
        Diagnostics.Reset();
        DetailsScrollViewer.ChangeView(null, 0, null);
    }

    public void ClearItems()
    {
        Items = null;
        DetailsRepeater.ItemsSource = null;
        realizedRows.Clear();
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
        }
    }

    private void OnElementClearing(ItemsRepeater sender, ItemsRepeaterElementClearingEventArgs args)
    {
        Diagnostics.RecordCleared();
        if (args.Element is FrameworkElement element)
        {
            realizedRows.Remove(element);
            element.ClearValue(FrameworkElement.TagProperty);
        }
    }

    private void OnSelectionChanged(object? sender, EventArgs args) => RefreshRealizedRows();

    private void RefreshRealizedRows()
    {
        foreach (FrameworkElement row in realizedRows) ApplyRowState(row);
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
}
