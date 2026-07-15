using System.Collections.ObjectModel;

namespace OpenExplorer.Contracts;

public enum ExplorerItemKind
{
    File = 1,
    Directory = 2,
}

public enum ExplorerSortField
{
    Name = 1,
    DateModified = 2,
    Type = 3,
    Size = 4,
}

public enum ExplorerSortDirection
{
    Ascending = 1,
    Descending = 2,
}

public sealed record ExplorerSortOptions
{
    public ExplorerSortOptions(ExplorerSortField field, ExplorerSortDirection direction, bool foldersFirst)
    {
        if (!Enum.IsDefined(field)) throw new ArgumentOutOfRangeException(nameof(field));
        if (!Enum.IsDefined(direction)) throw new ArgumentOutOfRangeException(nameof(direction));
        Field = field;
        Direction = direction;
        FoldersFirst = foldersFirst;
    }

    public ExplorerSortField Field { get; }
    public ExplorerSortDirection Direction { get; }
    public bool FoldersFirst { get; }

    public static ExplorerSortOptions Default { get; } = new(ExplorerSortField.Name, ExplorerSortDirection.Ascending, foldersFirst: true);
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

public sealed record ExplorerBreadcrumb(string Label, ExplorerLocation Location, bool IsCurrent);

/// <summary>Provider-owned interpretation of user-entered locations and ancestor segments.</summary>
public interface ILocationAddressResolver
{
    ExplorerLocation ParseAddress(string input);

    IReadOnlyList<ExplorerBreadcrumb> GetBreadcrumbs(ExplorerLocation location);
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

    bool TryGetIndexByItemId(ulong itemId, out ulong index);

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

public interface ILocationHierarchy
{
    bool TryGetParent(ExplorerLocation location, out ExplorerLocation parent);

    ExplorerLocation ResolveChild(ExplorerLocation parent, ExplorerItem child);
}

public interface IExplorerSnapshotViewFactory
{
    IExplorerSnapshot CreateSortedView(IExplorerSnapshot source, ExplorerSortOptions options);
}
