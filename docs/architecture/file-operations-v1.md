# Basic file operations

File operations are provider-agnostic requests owned by Application. A request captures the accepted location, stable item IDs, and the navigation generation. The operation coordinator permits one mutation batch at a time, ignores stale completions, and accepts at most one refresh after a batch reports an actual mutation. Selection and focus reconciliation remain stable-ID based; inverted Select All is never expanded into a managed directory scan.

Windows mutation work is implemented by `OpenExplorer.ShellHost`, a separate process reached through the bounded versioned local Named Pipe protocol. The WinUI process never calls Shell APIs, Recycle Bin APIs, or filesystem mutation APIs. ShellHost validates operation shape, path/name lengths, UTF-8, and Windows names before executing rename, race-safe unique folder creation, or Recycle Bin deletion. Results carry a status and bounded per-item failures.

Inline rename state is keyed by stable ItemId in the virtualized Details View. Recycled rows clear editor state before reuse. New-folder and rename commands are disabled while a mutation or navigation is busy; failures leave the accepted snapshot visible and show a concise message.
