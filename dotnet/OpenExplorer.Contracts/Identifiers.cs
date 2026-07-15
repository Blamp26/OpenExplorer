namespace OpenExplorer.Contracts;

public readonly record struct ItemId(ulong Value);

public readonly record struct LocationId(ulong Value);

public readonly record struct SnapshotId(ulong Value);

public readonly record struct OperationId(ulong Value);

public readonly record struct SearchSessionId(ulong Value);
