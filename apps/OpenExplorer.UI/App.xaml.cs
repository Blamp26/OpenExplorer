using OpenExplorer.Application;
using OpenExplorer.Application.Navigation;
using OpenExplorer.Contracts;
using OpenExplorer.Interop;
using Microsoft.UI.Xaml;

namespace OpenExplorer_UI;

public partial class App : Application
{
    private Window? _window;
    private NativeExplorerEngine? _engine;
    private ExplorerNavigationController? _navigationController;

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
            ((MainWindow)_window).SetViewModel(new MainViewModel(_engine));
            ((MainWindow)_window).SetNavigationController(_navigationController);
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
        }
        _navigationController = null;
        _engine?.Dispose();
        _engine = null;
    }
}
