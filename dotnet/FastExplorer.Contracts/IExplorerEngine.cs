namespace FastExplorer.Contracts;

public interface IExplorerEngine : IDisposable
{
    uint ApiVersion { get; }
}
