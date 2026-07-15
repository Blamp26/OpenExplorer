using FastExplorer.Application;
using FastExplorer.Interop;
using Microsoft.UI.Xaml;

namespace FastExplorer_UI;

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
        }
        catch (Exception exception)
        {
            ((MainWindow)_window).SetInitializationError(exception.Message);
        }

        _window.Activate();
    }
}
