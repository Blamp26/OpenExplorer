using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using OpenExplorer.Application.Diagnostics;
using OpenExplorer.Contracts;
using OpenExplorer_UI.Features.Performance;

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

    public event Action<ExplorerItem>? DirectoryActivated;

    public event Action<ExplorerSortField>? SortRequested;

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
    }

    private void OnElementClearing(ItemsRepeater sender, ItemsRepeaterElementClearingEventArgs args)
    {
        Diagnostics.RecordCleared();
    }
}
