using FastExplorer.Application;
using Microsoft.UI.Xaml.Controls;

namespace FastExplorer_UI;

public sealed partial class MainPage : Page
{
    public MainPage()
    {
        InitializeComponent();
    }

    public void SetViewModel(MainViewModel viewModel)
    {
        TitleText.Text = viewModel.Title;
        VersionText.Text = viewModel.NativeApiVersionText;
    }

    public void SetInitializationError(string message)
    {
        VersionText.Text = $"Native initialization error: {message}";
    }
}
