using Microsoft.UI.Xaml.Controls;
using OpenExplorer.Application.Diagnostics;
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

    public SyntheticFileItemList Items { get; } = new();

    public VirtualizationDiagnostics Diagnostics { get; } = new();

    private void OnElementPrepared(ItemsRepeater sender, ItemsRepeaterElementPreparedEventArgs args)
    {
        Diagnostics.RecordPrepared();
    }

    private void OnElementClearing(ItemsRepeater sender, ItemsRepeaterElementClearingEventArgs args)
    {
        Diagnostics.RecordCleared();
    }
}
