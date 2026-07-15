using OpenExplorer.Application;
using OpenExplorer.Application.Navigation;
using OpenExplorer.Contracts;
using OpenExplorer.Interop;
using OpenExplorer.ShellInterop;
using OpenExplorer.Application.Icons;
using Microsoft.UI.Xaml;

namespace OpenExplorer_UI;

public partial class App : Application
{
    private Window? _window;
    private NativeExplorerEngine? _engine;
    private ExplorerNavigationController? _navigationController;
    private ExplorerIconCoordinator? _iconCoordinator;

    public App()
    {
        InitializeComponent();
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        _window.Activate();
        try
        {
            _engine = new NativeExplorerEngine();
            _navigationController = new ExplorerNavigationController(_engine, _engine, _engine);
            _iconCoordinator = new ExplorerIconCoordinator(new ShellHostIconProvider());
            ((MainWindow)_window).SetViewModel(new MainViewModel(_engine));
            ((MainWindow)_window).SetNavigationController(_navigationController);
            ((MainWindow)_window).SetIconCoordinator(_iconCoordinator);
            string profilePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            await _navigationController.InitializeAsync(ExplorerLocation.File(profilePath));
        }
        catch (Exception exception)
        {
            ((MainWindow)_window).SetInitializationError(exception.Message);
        }

        _window.Closed += OnWindowClosed;
    }

    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        if (_window is MainWindow)
        {
            _navigationController?.Dispose();
            _iconCoordinator?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        _navigationController = null;
        _engine?.Dispose();
        _engine = null;
        _iconCoordinator = null;
    }
}
