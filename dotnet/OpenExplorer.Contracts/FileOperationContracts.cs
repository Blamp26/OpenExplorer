namespace OpenExplorer.Contracts;

public enum ExplorerFileOperationKind
{
    Rename,
    CreateFolder,
    RecycleBinDelete,
}

public enum ExplorerFileOperationStatus
{
    Succeeded,
    Cancelled,
    Failed,
    Partial,
}

public sealed record ExplorerFileOperationItem(ulong ItemId, string Name, ExplorerItemKind Kind);

public sealed record ExplorerFileOperationRequest(
    ExplorerFileOperationKind Kind,
    ExplorerLocation Location,
    IReadOnlyList<ExplorerFileOperationItem> Items,
    string? DesiredName,
    long Generation);

public sealed record ExplorerFileOperationFailure(ulong? ItemId, string? Name, string Message);

public sealed record ExplorerFileOperationResult(
    ExplorerFileOperationStatus Status,
    IReadOnlyList<ExplorerFileOperationFailure> Failures,
    bool Mutated,
    string? CreatedName = null,
    ulong? CreatedItemId = null);

public interface IExplorerFileOperationProvider
{
    Task<ExplorerFileOperationResult> ExecuteAsync(ExplorerFileOperationRequest request, CancellationToken cancellationToken);
}
