using System.Collections.ObjectModel;

namespace OpenExplorer.Contracts;

public enum ExplorerItemKind
{
    File = 1,
    Directory = 2,
}

public sealed record ExplorerItem(
    ulong ItemId,
    string Name,
    DateTimeOffset DateModified,
    ulong? Size,
    ExplorerItemKind Kind);

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
