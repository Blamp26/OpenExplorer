using Microsoft.UI.Xaml.Controls;
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

    public void SetSnapshot(IExplorerSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (Items is not null)
        {
            throw new InvalidOperationException("The snapshot has already been assigned.");
        }

        Items = new SnapshotFileItemList(snapshot);
        DetailsRepeater.ItemsSource = Items;
    }

    public void DisposeSnapshot()
    {
        Items?.Dispose();
        Items = null;
        DetailsRepeater.ItemsSource = null;
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
