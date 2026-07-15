# Navigation model v1

OpenExplorer now has a provider-agnostic `ExplorerNavigationController` in the Application layer. It owns the current `ExplorerLocation`, the active bounded `SnapshotFileItemList`, Back and Forward location histories, busy/error state, and a monotonically increasing navigation generation.

Initialization and normal navigation open one immutable snapshot asynchronously through `ILocationSnapshotFactory`. The synchronous provider call runs on a BCL thread-pool task, so the UI thread is not blocked. Pages remain lazy and are still fetched only by the existing 256-item cache.

Back moves the current location to Forward after its target opens successfully. Forward moves the current location to Back. Up resolves a parent syntactically through `ILocationHierarchy`, and directory activation resolves a child from the source `ExplorerItem`. History changes only after the replacement snapshot opens successfully. Root Up, empty Back/Forward, and file activation are no-ops.

Each request receives a generation. Native opening is not cancelable, but a result that finishes after a newer request or controller disposal is ignored and disposed immediately. Failed opens preserve the current location, list, and histories and expose a concise error. The controller disposes the active list exactly once; the list then disposes its native snapshot before the application disposes the engine.

The current UI starts at the current Windows user profile and supports directory double-click, Back, Forward, and Up. The location is read-only. The controller retains one base snapshot and one active native sorted view; sorting changes preserve location and history without reopening the directory. Selection is cleared only after an accepted navigation and is left unchanged for failed or stale requests. Watching, file activation, breadcrumbs, and editable address input remain deferred.

Refresh reopens the current location asynchronously through the same latest-request-wins generation model. It preserves the location, Back/Forward history, sort policy, and the existing view until a replacement snapshot and sorted view have both succeeded. Accepted refresh prunes only selection IDs that no longer resolve in the new native view; inverted Select All remains inverted and only recorded exceptions are pruned. Focus and anchor IDs are retained when present. The WinUI page exposes Refresh and F5, disables refresh and sorting while an operation is busy, preserves a reasonable virtualized viewport, and shows concise errors without replacing valid contents.
