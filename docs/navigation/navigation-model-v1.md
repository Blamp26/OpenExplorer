# Navigation model v1

OpenExplorer now has a provider-agnostic `ExplorerNavigationController` in the Application layer. It owns the current `ExplorerLocation`, the active bounded `SnapshotFileItemList`, Back and Forward location histories, busy/error state, and a monotonically increasing navigation generation.

Initialization and normal navigation open one immutable snapshot asynchronously through `ILocationSnapshotFactory`. The synchronous provider call runs on a BCL thread-pool task, so the UI thread is not blocked. Pages remain lazy and are still fetched only by the existing 256-item cache.

Back moves the current location to Forward after its target opens successfully. Forward moves the current location to Back. Up resolves a parent syntactically through `ILocationHierarchy`, and directory activation resolves a child from the source `ExplorerItem`. History changes only after the replacement snapshot opens successfully. Root Up, empty Back/Forward, and file activation are no-ops.

Each request receives a generation. Native opening is not cancelable, but a result that finishes after a newer request or controller disposal is ignored and disposed immediately. Failed opens preserve the current location, list, and histories and expose a concise error. The controller disposes the active list exactly once; the list then disposes its native snapshot before the application disposes the engine.

The current UI starts at the current Windows user profile and supports directory double-click, Back, Forward, and Up. The location is read-only. Sorting, watching, selection, file activation, breadcrumbs, and editable address input remain deferred.
