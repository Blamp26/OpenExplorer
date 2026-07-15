using OpenExplorer.Application;
using OpenExplorer.Contracts;
using OpenExplorer.Interop;
using Microsoft.UI.Xaml;

namespace OpenExplorer_UI;

public partial class App : Application
{
    private Window? _window;
    private NativeExplorerEngine? _engine;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        try
        {
            _engine = new NativeExplorerEngine();
            ((MainWindow)_window).SetViewModel(new MainViewModel(_engine));
            IExplorerSnapshot snapshot = _engine.CreateSyntheticSnapshot(100_000);
            ((MainWindow)_window).SetSnapshot(snapshot);
        }
        catch (Exception exception)
        {
            ((MainWindow)_window).SetInitializationError(exception.Message);
        }

        _window.Activate();
        _window.Closed += OnWindowClosed;
    }

    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        if (_window is MainWindow mainWindow)
        {
            mainWindow.DisposeSnapshot();
        }
        _engine?.Dispose();
        _engine = null;
    }
}
