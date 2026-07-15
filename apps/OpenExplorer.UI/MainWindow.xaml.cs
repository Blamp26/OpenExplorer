using Microsoft.UI.Xaml;
using OpenExplorer.Application;
using OpenExplorer.Contracts;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace OpenExplorer_UI;

/// <summary>
/// The application window. This hosts a Frame that displays pages. Add your
/// UI and logic to MainPage.xaml / MainPage.xaml.cs instead of here so you
/// can use Page features such as navigation events and the Loaded lifecycle.
/// </summary>
public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        AppWindow.SetIcon("Assets/AppIcon.ico");

        // Navigate the root frame to the main page on startup.
        RootFrame.Navigate(typeof(MainPage));
    }

    public void SetViewModel(MainViewModel viewModel)
    {
        ((MainPage)RootFrame.Content).SetViewModel(viewModel);
    }

    public void SetInitializationError(string message)
    {
        ((MainPage)RootFrame.Content).SetInitializationError(message);
    }

    public void SetSnapshot(IExplorerSnapshot snapshot)
    {
        ((MainPage)RootFrame.Content).SetSnapshot(snapshot);
    }

    public void DisposeSnapshot()
    {
        if (RootFrame.Content is MainPage page)
        {
            page.DisposeSnapshot();
        }
    }
}
