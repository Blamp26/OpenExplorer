using OpenExplorer.Contracts;

namespace OpenExplorer.Application;

public sealed class MainViewModel
{
    public MainViewModel(IExplorerEngine engine)
    {
        ArgumentNullException.ThrowIfNull(engine);

        Title = "OpenExplorer";
        NativeApiVersionText = $"Native API version: {engine.ApiVersion}";
    }

    public string Title { get; }

    public string NativeApiVersionText { get; }
}
