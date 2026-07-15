# OpenExplorer architecture

```text
WinUI
  → C# Application
  → C# Contracts
  ← C# Interop
  → Rust FFI
  → Rust Engine

WinUI/Application
  ↔ Indexer through versioned IPC

WinUI/Application
  ↔ ShellHost through versioned IPC
```

WinUI renders state but does not enumerate files or sort items. C# Application contains orchestration but no native calls. C# Interop is the only managed layer allowed to access Rust exports. Rust Engine owns immutable local-directory snapshots and sorted logical views; the synthetic snapshot remains a diagnostic source. Indexer and ShellHost remain separate processes.

Locations are provider-based and are not assumed to be normal filesystem paths. External Shell extensions must not run inside the main UI process. Future native APIs will use opaque handles, ranges, pages, batches, and events. Large directories must never cross a boundary as one JSON payload.

Native ABI v4 preserves the opaque snapshot handle, two-call batched range access, and local-directory open operation, and adds an independent sorted-view handle. Interop owns native handles and provider-specific File hierarchy resolution, Application owns the provider-agnostic navigation controller, base snapshot, active sorted view, bounded page cache, and stable-ID selection model, and UI consumes display rows without calling native exports. Sorting is performed in Rust and does not reopen directories. Navigation history stores locations rather than complete snapshots. Selection uses IDs and inverted Select All exceptions rather than row controls or complete directory materialization. No directory contents cross the boundary as JSON. Future IPC is planned as versioned Named Pipes with Protocol Buffers; neither is implemented yet.
