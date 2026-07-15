namespace OpenExplorer.Contracts;

public interface IExplorerEngine : IDisposable
{
    uint ApiVersion { get; }
}
