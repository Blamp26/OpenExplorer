using Microsoft.UI.Xaml.Controls;
using OpenExplorer.Application;
using OpenExplorer.Application.Navigation;
using OpenExplorer.Contracts;

namespace OpenExplorer_UI;

public sealed partial class MainPage : Page
{
    public MainPage()
    {
        InitializeComponent();
    }

    public void SetViewModel(MainViewModel viewModel)
    {
        FileView.SetViewModel(viewModel);
    }

    public void SetInitializationError(string message)
    {
        FileView.SetInitializationError(message);
    }

    public void SetNavigationController(ExplorerNavigationController controller) => FileView.SetNavigationController(controller);

}
