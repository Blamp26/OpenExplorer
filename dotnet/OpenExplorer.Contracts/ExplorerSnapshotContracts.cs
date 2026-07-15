using System.Collections.ObjectModel;

namespace OpenExplorer.Contracts;

public enum ExplorerItemKind
{
    File = 1,
    Directory = 2,
}

public enum ExplorerLocationScheme
{
    File = 1,
    Shell = 2,
    Search = 3,
    Archive = 4,
    Sftp = 5,
    Mtp = 6,
}

public sealed record ExplorerLocation
{
    public ExplorerLocation(ExplorerLocationScheme scheme, string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier)) throw new ArgumentException("A location identifier is required.", nameof(identifier));
        Scheme = scheme;
        Identifier = identifier;
    }

    public ExplorerLocationScheme Scheme { get; }
    public string Identifier { get; }

    public static ExplorerLocation File(string path) => new(ExplorerLocationScheme.File, path);
}

public sealed record ExplorerItem(
    ulong ItemId,
    string Name,
    DateTimeOffset DateModified,
    ulong? Size,
    ExplorerItemKind Kind,
    bool NameWasLossy = false);

public sealed class ExplorerItemBatch
{
    public ExplorerItemBatch(ulong startIndex, IReadOnlyList<ExplorerItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        StartIndex = startIndex;
        Items = new ReadOnlyCollection<ExplorerItem>(items.ToArray());
    }

    public ulong StartIndex { get; }

    public IReadOnlyList<ExplorerItem> Items { get; }
}

public interface IExplorerSnapshot : IDisposable
{
    ulong Count { get; }

    ExplorerItemBatch GetRange(ulong start, uint count);
}

public interface IDiagnosticSnapshotFactory
{
    IExplorerSnapshot CreateSyntheticSnapshot(ulong itemCount);
}

public interface ILocationSnapshotFactory
{
    IExplorerSnapshot OpenSnapshot(ExplorerLocation location);
}
