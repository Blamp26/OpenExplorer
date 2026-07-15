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

WinUI renders state but does not enumerate files. C# Application contains orchestration but no native calls. C# Interop is the only managed layer allowed to access Rust exports. Rust Engine owns the current immutable synthetic snapshot range source; sorting and filtering remain future work. Indexer and ShellHost remain separate processes.

Locations are provider-based and are not assumed to be normal filesystem paths. External Shell extensions must not run inside the main UI process. Future native APIs will use opaque handles, ranges, pages, batches, and events. Large directories must never cross a boundary as one JSON payload.

Native ABI v2 exposes an opaque snapshot handle and two-call batched range access. Interop owns native handles, Application owns the bounded page cache, and UI consumes display rows without calling native exports. Future IPC is planned as versioned Named Pipes with Protocol Buffers; neither is implemented yet.
