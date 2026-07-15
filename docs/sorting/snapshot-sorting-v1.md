# Snapshot sorting v1

OpenExplorer sorts through an immutable native view. The Rust engine keeps the base snapshot unchanged and creates a sorted logical permutation over shared source data. The managed page cache and virtualized `ItemsRepeater` consume that view through the existing range ABI; no item sorting occurs in C#, Application, or WinUI.

Supported fields are Name, Date modified, Type, and Size, each ascending or descending. Folders-first is an independent policy: when enabled, directories remain before files regardless of direction. Directories are name-ordered for Size sorting. When disabled, missing sizes sort after present sizes in both directions.

Name comparison uses standard-library Unicode lowercase behavior, then original name and item ID. Type uses lowercase filename extensions; extensionless files and names without a non-leading dot use an empty extension key. Natural-number sorting, locale-aware Shell collation, Shell type descriptions, filtering, search, and persistence are deferred.

The navigation controller owns one base snapshot and one active sorted view. Applying a sort creates a new view away from the UI thread without reopening or re-enumerating the current location, preserves Back and Forward history, replaces the current page list only after success, and scrolls to the top. A single generation protects navigation and sorting together; stale non-cancelable work is ignored and disposed. Stable-ID selection survives sorted view replacement without retaining a complete snapshot or managed ID-to-index map.
