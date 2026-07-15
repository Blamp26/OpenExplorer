using FastExplorer.Contracts;

namespace FastExplorer.Application;

public sealed class MainViewModel
{
    public MainViewModel(IExplorerEngine engine)
    {
        ArgumentNullException.ThrowIfNull(engine);

        Title = "FastExplorer";
        NativeApiVersionText = $"Native API version: {engine.ApiVersion}";
    }

    public string Title { get; }

    public string NativeApiVersionText { get; }
}
