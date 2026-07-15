using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Input;
using OpenExplorer.Application;
using OpenExplorer.Application.Navigation;
using OpenExplorer.Application.Icons;
using OpenExplorer.Application.Operations;
using OpenExplorer.Application.Selection;
using OpenExplorer.Contracts;
using OpenExplorer_UI.Features.Performance;
using OpenExplorer_UI.Features.FileView.Details;
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
    private bool operationBusy;
    private IExplorerFileOperationProvider? operationProvider;
    private ExplorerFileOperationCoordinator? operationCoordinator;

    public FileViewPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        frameMetricsCollector.MetricsUpdated += OnMetricsUpdated;
        DetailsView.SortRequested += OnSortRequested;
        DetailsView.DirectoryActivated += OnDirectoryActivated;
        DetailsView.RenameCommitRequested += OnRenameCommitRequested;
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

    public void SetFileOperationProvider(IExplorerFileOperationProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        operationProvider = provider;
        UpdateOperationControls();
    }

    public void SetFileOperationCoordinator(ExplorerFileOperationCoordinator coordinator)
    {
        ArgumentNullException.ThrowIfNull(coordinator);
        operationCoordinator = coordinator;
        coordinator.StateChanged += OnOperationStateChanged;
        UpdateOperationControls();
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

    private async void OnRefreshClick(object sender, RoutedEventArgs args) => await RefreshAsync();

    private async void OnRenameClick(object sender, RoutedEventArgs args) => await BeginRenameAsync();

    private async void OnNewFolderClick(object sender, RoutedEventArgs args) => await CreateFolderAsync();

    private async void OnDeleteClick(object sender, RoutedEventArgs args) => await DeleteAsync();

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
            return;
        }

        if (addressEditMode || operationBusy) return;
        if (args.Key == VirtualKey.F2)
        {
            args.Handled = true;
            await BeginRenameAsync();
        }
        else if (args.Key == VirtualKey.N && IsDown(VirtualKey.Control) && IsDown(VirtualKey.Shift))
        {
            args.Handled = true;
            await CreateFolderAsync();
        }
        else if (args.Key == VirtualKey.Delete)
        {
            args.Handled = true;
            await DeleteAsync();
        }
    }

    private async Task BeginRenameAsync()
    {
        if (!TryGetOperationItem(out ExplorerItem item) || operationProvider is null || navigationController?.CurrentLocation is not { } location) return;
        DetailsView.BeginRename(item);
        await Task.CompletedTask;
    }

    private async void OnRenameCommitRequested(RenameCommitRequest request)
    {
        if (operationProvider is null || navigationController?.CurrentLocation is not { } location) return;
        string name = request.Name.Trim();
        if (string.IsNullOrWhiteSpace(name)) { NavigationErrorText.Text = "A name is required."; DetailsView.BeginRename(request.ItemId); return; }
        ExplorerFileOperationRequest operation = new(ExplorerFileOperationKind.Rename, location,
            [new ExplorerFileOperationItem(request.ItemId, request.OriginalName, request.Kind)], name, navigationController.Generation);
        await ExecuteOperationAsync(operation, request.ItemId);
    }

    private async Task CreateFolderAsync()
    {
        if (operationProvider is null || navigationController?.CurrentLocation is not { } location) return;
        ExplorerFileOperationRequest operation = new(ExplorerFileOperationKind.CreateFolder, location, Array.Empty<ExplorerFileOperationItem>(), null, navigationController.Generation);
        await ExecuteOperationAsync(operation, null);
    }

    private async Task DeleteAsync()
    {
        if (operationProvider is null || navigationController?.CurrentLocation is not { } location) return;
        IReadOnlyList<ExplorerFileOperationItem> items = GetSelectedOperationItems();
        if (items.Count == 0)
        {
            NavigationErrorText.Text = "Select an item to delete.";
            return;
        }
        ContentDialog dialog = new()
        {
            Title = "Move to Recycle Bin?",
            Content = items.Count == 1 ? $"Move '{items[0].Name}' to the Recycle Bin?" : $"Move these {items.Count} items to the Recycle Bin?",
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            XamlRoot = XamlRoot,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        ExplorerFileOperationRequest operation = new(ExplorerFileOperationKind.RecycleBinDelete, location,
            items, null, navigationController.Generation);
        await ExecuteOperationAsync(operation, null);
    }

    private async Task ExecuteOperationAsync(ExplorerFileOperationRequest operation, ulong? renameId)
    {
        if (operationProvider is null || operationBusy) return;
        operationBusy = true;
        DetailsView.SetOperationBusy(true);
        UpdateOperationControls();
        try
        {
            ExplorerFileOperationResult result = operationCoordinator is not null
                ? await operationCoordinator.ExecuteAsync(operation, CancellationToken.None)
                : await operationProvider.ExecuteAsync(operation, CancellationToken.None);
            if (result.Status is ExplorerFileOperationStatus.Succeeded or ExplorerFileOperationStatus.Partial && result.Mutated)
            {
                if (operationCoordinator is null) await RefreshAsync();
                if (result.CreatedItemId is ulong createdId)
                {
                    if (DetailsView.Items is { } items && navigationController?.Selection.TrySelectItem(items, createdId) == true)
                    {
                        DetailsView.FocusItem(createdId);
                        DetailsView.BeginRename(createdId);
                    }
                }
                else if (renameId.HasValue) DetailsView.FocusItem(renameId.Value);
            }
            if (result.Failures.Count > 0)
                NavigationErrorText.Text = string.Join(" ", result.Failures.Take(2).Select(f => f.Message));
        }
        catch (Exception ex)
        {
            NavigationErrorText.Text = ex.Message;
        }
        finally
        {
            operationBusy = false;
            DetailsView.SetOperationBusy(false);
            OperationStatusText.Text = string.Empty;
            UpdateOperationControls();
        }
    }

    private bool TryGetFocusedItem(out ExplorerItem item)
    {
        item = default!;
        if (navigationController?.Selection.FocusedItemId is not ulong id || DetailsView.Items is null ||
            !DetailsView.Items.TryGetIndexByItemId(id, out ulong index) || index > int.MaxValue) return false;
        item = DetailsView.Items.GetSourceItem((int)index);
        return true;
    }

    private bool TryGetOperationItem(out ExplorerItem item)
    {
        item = default!;
        if (navigationController is null) return false;
        ulong? id = navigationController.Selection.FocusedItemId;
        if (!id.HasValue)
        {
            ExplorerSelectionState state = navigationController.Selection.CaptureState();
            id = state.SelectedIds.Count == 1 ? state.SelectedIds[0] : null;
        }
        return id.HasValue && TryGetItem(id.Value, out item);
    }

    private IReadOnlyList<ExplorerFileOperationItem> GetSelectedOperationItems()
    {
        if (navigationController is null || DetailsView.Items is null) return Array.Empty<ExplorerFileOperationItem>();
        ExplorerSelectionState state = navigationController.Selection.CaptureState();
        if (state.IsAllSelected)
        {
            NavigationErrorText.Text = "Delete requires individually selected items.";
            return Array.Empty<ExplorerFileOperationItem>();
        }

        List<ExplorerFileOperationItem> result = [];
        foreach (ulong id in state.SelectedIds)
            if (TryGetItem(id, out ExplorerItem item)) result.Add(new ExplorerFileOperationItem(item.ItemId, item.Name, item.Kind));
        return result;
    }

    private bool TryGetItem(ulong itemId, out ExplorerItem item)
    {
        item = default!;
        if (DetailsView.Items?.TryGetIndexByItemId(itemId, out ulong index) != true || index > int.MaxValue) return false;
        item = DetailsView.Items.GetSourceItem((int)index);
        return true;
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

    private void OnOperationStateChanged(object? sender, EventArgs args)
    {
        operationBusy = operationCoordinator?.IsBusy ?? operationBusy;
        UpdateOperationControls();
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
        UpdateOperationControls();
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

    private void UpdateOperationControls()
    {
        bool enabled = operationProvider is not null && !(operationCoordinator?.IsBusy ?? operationBusy) && navigationController is { IsBusy: false, CurrentLocation: not null };
        OperationStatusText.Text = operationBusy ? "Working…" : string.Empty;
        RenameButton.IsEnabled = enabled && navigationController?.Selection.FocusedItemId is not null;
        NewFolderButton.IsEnabled = enabled;
        DeleteButton.IsEnabled = enabled && navigationController?.Selection.FocusedItemId is not null;
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
