using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Input;
using OpenExplorer.Application;
using OpenExplorer.Application.Navigation;
using OpenExplorer.Application.Icons;
using OpenExplorer.Contracts;
using OpenExplorer_UI.Features.Performance;
using Windows.System;
using Windows.UI.Core;

namespace OpenExplorer_UI.Features.FileView;

public sealed partial class FileViewPage : Page
{
    private readonly FrameMetricsCollector frameMetricsCollector = new();
    private ExplorerNavigationController? navigationController;
    private bool updatingSortControls;
    private bool addressEditMode;
    private bool submittingAddress;

    public FileViewPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        frameMetricsCollector.MetricsUpdated += OnMetricsUpdated;
        DetailsView.SortRequested += OnSortRequested;
        DetailsView.DirectoryActivated += OnDirectoryActivated;
        AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(OnPageKeyDown), handledEventsToo: true);
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

    public void SetLocation(string path) => AddressTextBox.Text = path;

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

    public void SetIconCoordinator(ExplorerIconCoordinator coordinator) => DetailsView.SetIconProvider(coordinator);

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

    private async void OnRefreshClick(object sender, RoutedEventArgs args) => await RefreshAsync();

    private async void OnPageKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (IsDown(VirtualKey.Control) && args.Key == VirtualKey.L ||
            IsDown(VirtualKey.Menu) && args.Key == VirtualKey.D)
        {
            args.Handled = true;
            BeginAddressEdit();
            return;
        }

        if (args.Key == VirtualKey.F5 && !addressEditMode)
        {
            args.Handled = true;
            await RefreshAsync();
        }
    }

    private void OnAddressEditClick(object sender, RoutedEventArgs args) => BeginAddressEdit();

    private void OnAddressCancelClick(object sender, RoutedEventArgs args) => CancelAddressEdit();

    private void BeginAddressEdit()
    {
        if (navigationController?.CurrentLocation is not { } location || navigationController.IsBusy) return;
        addressEditMode = true;
        AddressTextBox.Text = location.Identifier;
        AddressTextBox.Visibility = Visibility.Visible;
        AddressCancelButton.Visibility = Visibility.Visible;
        AddressEditButton.Visibility = Visibility.Collapsed;
        AddressTextBox.Focus(FocusState.Programmatic);
        AddressTextBox.SelectAll();
        UpdateAddressControls();
    }

    private void CancelAddressEdit()
    {
        addressEditMode = false;
        submittingAddress = false;
        AddressTextBox.Text = navigationController?.CurrentLocation?.Identifier ?? string.Empty;
        AddressTextBox.Visibility = Visibility.Collapsed;
        AddressCancelButton.Visibility = Visibility.Collapsed;
        AddressEditButton.Visibility = Visibility.Visible;
        if (navigationController?.CurrentLocation is { } location)
        {
            RenderBreadcrumbs(location);
        }
        UpdateAddressControls();
        DetailsView.FocusView();
    }

    private async void OnAddressKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key == VirtualKey.Escape)
        {
            args.Handled = true;
            CancelAddressEdit();
            return;
        }

        if (args.Key != VirtualKey.Enter || submittingAddress) return;
        args.Handled = true;
        submittingAddress = true;
        try
        {
            if (navigationController is null) return;
            await navigationController.NavigateAddressAsync(AddressTextBox.Text);
            CancelAddressEdit();
        }
        catch
        {
            NavigationErrorText.Text = "Unable to open that folder.";
            submittingAddress = false;
        }
    }

    private static bool IsDown(VirtualKey key) =>
        (InputKeyboardSource.GetKeyStateForCurrentThread(key) & CoreVirtualKeyStates.Down) != 0;

    private async Task RefreshAsync()
    {
        if (navigationController is null || navigationController.IsBusy) return;
        await navigationController.RefreshAsync();
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
        BackButton.IsEnabled = !navigationController.IsBusy && navigationController.CanGoBack;
        ForwardButton.IsEnabled = !navigationController.IsBusy && navigationController.CanGoForward;
        UpButton.IsEnabled = !navigationController.IsBusy && navigationController.CanGoUp;
        RefreshButton.IsEnabled = !navigationController.IsBusy && navigationController.CurrentLocation is not null;
        UpdateAddressControls();
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
            DetailsView.SetIconLocation(location);
            if (!addressEditMode) AddressTextBox.Text = location.Identifier;
            if (!addressEditMode) RenderBreadcrumbs(location);
        }
        if (navigationController.CurrentItems is { } items && !ReferenceEquals(DetailsView.Items, items))
        {
            DetailsView.SetItems(items);
        }
        UpdateDiagnostics(new FrameMetricsSnapshot(0, 0, 0, 0));
    }

    private void RenderBreadcrumbs(ExplorerLocation location)
    {
        BreadcrumbPanel.Children.Clear();
        foreach (ExplorerBreadcrumb breadcrumb in navigationController?.CurrentBreadcrumbs ?? Array.Empty<ExplorerBreadcrumb>())
        {
            if (BreadcrumbPanel.Children.Count > 0)
            {
                BreadcrumbPanel.Children.Add(new TextBlock { Text = "›", Opacity = 0.65, VerticalAlignment = VerticalAlignment.Center });
            }

            if (breadcrumb.IsCurrent)
            {
                TextBlock text = new()
                {
                    Text = breadcrumb.Label,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    VerticalAlignment = VerticalAlignment.Center,
                    Padding = new Thickness(8, 5, 8, 5),
                    MaxWidth = 360,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                };
                AutomationProperties.SetName(text, $"Current location: {breadcrumb.Label}");
                BreadcrumbPanel.Children.Add(text);
            }
            else
            {
                Button button = new()
                {
                    Content = breadcrumb.Label,
                    Tag = breadcrumb.Location,
                    Padding = new Thickness(8, 5, 8, 5),
                    MinWidth = 0,
                    MaxWidth = 280,
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                };
                AutomationProperties.SetName(button, $"Navigate to {breadcrumb.Label}");
                button.Click += OnBreadcrumbClick;
                BreadcrumbPanel.Children.Add(button);
            }
        }
    }

    private async void OnBreadcrumbClick(object sender, RoutedEventArgs args)
    {
        if (navigationController is null || navigationController.IsBusy || sender is not Button { Tag: ExplorerLocation location }) return;
        await navigationController.NavigateToAsync(location);
    }

    private void UpdateAddressControls()
    {
        AddressEditButton.IsEnabled = !addressEditMode && navigationController is { IsBusy: false, CurrentLocation: not null };
        AddressTextBox.IsEnabled = addressEditMode && navigationController is { IsBusy: false } && !submittingAddress;
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
