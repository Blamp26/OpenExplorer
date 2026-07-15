using Microsoft.UI.Xaml.Controls;
using OpenExplorer.Application;
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

    public void SetSnapshot(IExplorerSnapshot snapshot) => FileView.SetSnapshot(snapshot);

    public void SetLocation(string path) => FileView.SetLocation(path);

    public void DisposeSnapshot() => FileView.DisposeSnapshot();
}
