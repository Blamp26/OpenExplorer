using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OpenExplorer.Application;
using OpenExplorer.Application.Navigation;
using OpenExplorer.Contracts;
using OpenExplorer_UI.Features.Performance;

namespace OpenExplorer_UI.Features.FileView;

public sealed partial class FileViewPage : Page
{
    private readonly FrameMetricsCollector frameMetricsCollector = new();
    private ExplorerNavigationController? navigationController;
    private bool updatingSortControls;

    public FileViewPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        frameMetricsCollector.MetricsUpdated += OnMetricsUpdated;
        DetailsView.SortRequested += OnSortRequested;
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

    public void SetLocation(string path) => LocationText.Text = path;

    public void SetNavigationController(ExplorerNavigationController controller)
    {
        ArgumentNullException.ThrowIfNull(controller);
        if (navigationController is not null)
        {
            navigationController.StateChanged -= OnNavigationStateChanged;
        }
        navigationController = controller;
        navigationController.StateChanged += OnNavigationStateChanged;
        DetailsView.SetSelection(navigationController.Selection);
        UpdateNavigationState();
    }

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        frameMetricsCollector.Start();
        if (navigationController is not null)
        {
            DetailsView.SetSelection(navigationController.Selection);
            navigationController.StateChanged -= OnNavigationStateChanged;
            navigationController.StateChanged += OnNavigationStateChanged;
            UpdateNavigationState();
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs args)
    {
        frameMetricsCollector.Stop();
        DetailsView.DetachSelection();
        if (navigationController is not null)
        {
            navigationController.StateChanged -= OnNavigationStateChanged;
        }
    }

    private void OnMetricsUpdated(object? sender, FrameMetricsSnapshot snapshot)
    {
        UpdateDiagnostics(snapshot);
    }

    private async void OnBackClick(object sender, RoutedEventArgs args)
    {
        if (navigationController is not null) await navigationController.GoBackAsync();
    }

    private async void OnForwardClick(object sender, RoutedEventArgs args)
    {
        if (navigationController is not null) await navigationController.GoForwardAsync();
    }

    private async void OnUpClick(object sender, RoutedEventArgs args)
    {
        if (navigationController is not null) await navigationController.GoUpAsync();
    }

    private async void OnDirectoryActivated(ExplorerItem item)
    {
        if (navigationController is not null) await navigationController.NavigateIntoAsync(item);
    }

    private async void OnSortRequested(ExplorerSortField field)
    {
        if (navigationController is null) return;
        ExplorerSortOptions current = navigationController.CurrentSortOptions;
        ExplorerSortDirection direction = current.Field == field && current.Direction == ExplorerSortDirection.Ascending
            ? ExplorerSortDirection.Descending
            : current.Field == field ? ExplorerSortDirection.Ascending : ExplorerSortDirection.Ascending;
        await navigationController.ApplySortAsync(new ExplorerSortOptions(field, direction, current.FoldersFirst));
    }

    private async void OnFoldersFirstChanged(object sender, RoutedEventArgs args)
    {
        if (updatingSortControls || navigationController is null) return;
        ExplorerSortOptions current = navigationController.CurrentSortOptions;
        await navigationController.ApplySortAsync(new ExplorerSortOptions(current.Field, current.Direction, FoldersFirstCheckBox.IsChecked == true));
    }

    private void OnNavigationStateChanged(object? sender, EventArgs args)
    {
        UpdateNavigationState();
    }

    private void UpdateNavigationState()
    {
        if (navigationController is null) return;
        BackButton.IsEnabled = navigationController.CanGoBack;
        ForwardButton.IsEnabled = navigationController.CanGoForward;
        UpButton.IsEnabled = navigationController.CanGoUp;
        NavigationProgress.IsActive = navigationController.IsBusy;
        NavigationProgress.Visibility = navigationController.IsBusy ? Visibility.Visible : Visibility.Collapsed;
        DetailsView.SetSortEnabled(!navigationController.IsBusy);
        DetailsView.SetSortOptions(navigationController.CurrentSortOptions);
        updatingSortControls = true;
        FoldersFirstCheckBox.IsEnabled = !navigationController.IsBusy;
        FoldersFirstCheckBox.IsChecked = navigationController.CurrentSortOptions.FoldersFirst;
        updatingSortControls = false;
        NavigationErrorText.Text = navigationController.ErrorMessage ?? string.Empty;
        if (navigationController.CurrentLocation is { } location)
        {
            LocationText.Text = location.Identifier;
        }
        if (navigationController.CurrentItems is { } items && !ReferenceEquals(DetailsView.Items, items))
        {
            DetailsView.SetItems(items);
        }
        UpdateDiagnostics(new FrameMetricsSnapshot(0, 0, 0, 0));
    }

    private void UpdateDiagnostics(FrameMetricsSnapshot snapshot)
    {
        var source = DetailsView.Items;
        var virtualization = DetailsView.Diagnostics;
        if (source is null)
        {
            SourceDiagnosticsText.Text = "Items: --    Cached items: --    Cached pages: --    Native range requests: --";
        }
        else
        {
            SourceDiagnosticsText.Text =
                $"Items: {source.LogicalItemCount:N0}    Cached items: {source.CurrentCachedItemCount}/{source.MaximumCachedPages * source.PageSize:N0} " +
                $"   Cached pages: {source.CurrentCachedPages}/{source.MaximumCachedPages}    Native range requests: {source.RangeRequestCount} " +
                $"   Received: {source.TotalItemsReceived:N0}    Realized: {virtualization.CurrentRealizedElementCount} (peak {virtualization.PeakRealizedElementCount})";
        }

        string fps = snapshot.SampleCount == 0 ? "--" : $"{snapshot.CurrentFps:0.0}";
        string frame = snapshot.SampleCount == 0 ? "--" : $"{snapshot.AverageFrameTimeMilliseconds:0.0}";
        long workingSetMiB = Process.GetCurrentProcess().WorkingSet64 / (1024 * 1024);
        FrameDiagnosticsText.Text =
            $"FPS: {fps}    Frame: {frame} ms    Max: {(snapshot.SampleCount == 0 ? "--" : $"{snapshot.MaximumFrameTimeMilliseconds:0.0}")} ms " +
            $"   Working set: {workingSetMiB:N0} MiB    Samples: {snapshot.SampleCount}";
    }
}
