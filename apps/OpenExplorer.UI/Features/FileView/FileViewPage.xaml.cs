using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OpenExplorer.Application;
using OpenExplorer_UI.Features.Performance;

namespace OpenExplorer_UI.Features.FileView;

public sealed partial class FileViewPage : Page
{
    private readonly FrameMetricsCollector frameMetricsCollector = new();

    public FileViewPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        frameMetricsCollector.MetricsUpdated += OnMetricsUpdated;
        UpdateDiagnostics(new FrameMetricsSnapshot(0, 0, 0, 0));
    }

    public void SetViewModel(MainViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        TitleText.Text = viewModel.Title;
        VersionText.Text = viewModel.NativeApiVersionText;
    }

    public void SetInitializationError(string message)
    {
        VersionText.Text = $"Native initialization error: {message}";
    }

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        frameMetricsCollector.Start();
    }

    private void OnUnloaded(object sender, RoutedEventArgs args)
    {
        frameMetricsCollector.Stop();
    }

    private void OnMetricsUpdated(object? sender, FrameMetricsSnapshot snapshot)
    {
        UpdateDiagnostics(snapshot);
    }

    private void UpdateDiagnostics(FrameMetricsSnapshot snapshot)
    {
        var source = DetailsView.Items;
        var virtualization = DetailsView.Diagnostics;
        SourceDiagnosticsText.Text =
            $"Items: {source.LogicalItemCount:N0}    Realized: {virtualization.CurrentRealizedElementCount} " +
            $"(peak {virtualization.PeakRealizedElementCount})    Cache: {source.CachedItemCount}/{source.CacheCapacity} " +
            $"(generated {source.TotalGeneratedItemCount:N0})";

        string fps = snapshot.SampleCount == 0 ? "--" : $"{snapshot.CurrentFps:0.0}";
        string frame = snapshot.SampleCount == 0 ? "--" : $"{snapshot.AverageFrameTimeMilliseconds:0.0}";
        long workingSetMiB = Process.GetCurrentProcess().WorkingSet64 / (1024 * 1024);
        FrameDiagnosticsText.Text =
            $"FPS: {fps}    Frame: {frame} ms    Max: {(snapshot.SampleCount == 0 ? "--" : $"{snapshot.MaximumFrameTimeMilliseconds:0.0}")} ms " +
            $"   Working set: {workingSetMiB:N0} MiB    Samples: {snapshot.SampleCount}";
    }
}
