# Isolated shell icon provider v1

Details View talks to `ExplorerIconCoordinator`, an Application-owned abstraction. It submits bounded batches containing stable item IDs, names, kinds, and the accepted location. The coordinator owns the bounded cache and coalesces duplicate cache-key work. UI code receives only a small presentation key and maps failures to generic placeholders; it never owns processes, pipes, native icon handles, or image buffers.

Requests are issued for realized rows plus a small look-ahead window. A generation is invalidated on navigation, refresh, sorting, item replacement, and eviction. Results are applied only when the generation and stable item ID still match the current snapshot. This prevents recycled rows from displaying an older item's icon and prevents stale ShellHost results from changing a valid view.

Startup wires the isolated ShellHost transport. If its executable is missing, times out, disconnects, crashes, or returns malformed data, the client retries once with bounded backoff and resolves the batch to folder/file placeholders without affecting navigation. Missing, inaccessible, or unsupported icons follow the same fallback path.
