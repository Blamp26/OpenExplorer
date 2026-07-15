# Virtualized selection v1

Selection is provider-agnostic Application state. Its source of truth is stable `ExplorerItem.ItemId`, not a realized WinUI row or cached display object. A normal click replaces the explicit ID set, Ctrl+click toggles one ID, and Shift+click selects the inclusive range from the anchor to the clicked item. Ctrl+Shift+click toggles that inclusive range and preserves the existing anchor; this is the chosen Explorer-like extension semantics.

Ctrl+A uses inverted state: all logical items are selected and a deselected-ID exception set records changes. It does not enumerate the directory or create one managed selection entry per item. Escape clears the model. Arrow/Home/End update the focused ID, and Shift variants extend from the anchor.

Sorting replaces the native logical view but does not replace the selection model. Focus and anchor remain IDs and are looked up only when needed; no complete managed ID-to-index map is created. Accepted navigation clears selection after the new snapshot/view is accepted. Failed or stale navigation leaves the existing selection unchanged.

The ItemsRepeater asks the model for selected/focused state when an element is prepared and refreshes currently realized rows when selection changes. Cleared elements are not retained. Page size and the four-page/1,024-item cache remain unchanged.

Selection, sorting, and navigation are intentionally limited to the current view. Persistence, clipboard operations, and file activation remain deferred.
