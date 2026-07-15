using Microsoft.UI.Xaml.Controls;
using OpenExplorer.Application;

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
}
