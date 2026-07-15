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

    private void OnRowDoubleTapped(object sender, DoubleTappedRoutedEventArgs args)
    {
        if (sender is FrameworkElement { DataContext: SnapshotFileItem item } && item.IsDirectory)
        {
            DirectoryActivated?.Invoke(item.SourceItem);
        }
        args.Handled = true;
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
