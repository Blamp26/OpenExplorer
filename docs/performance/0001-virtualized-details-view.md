# Virtualized Details View performance gate

This gate establishes the UI rendering boundary before broader filesystem integration. The active UI now receives pages from one immutable local-directory Rust snapshot through native ABI v3. A synthetic Rust snapshot remains available for diagnostics and regression tests.

## Source

The Rust engine enumerates the current user profile's immediate children once and owns their immutable metadata. Requested ranges generate records and one UTF-8 arena only for that page. The Application project owns `SnapshotFileItemList`, which requests 256-item pages and retains at most four pages, or 1,024 display rows by default. Synthetic construction still stores only a logical count and generates requested ranges lazily.

The page cache is deterministic LRU and exposes logical count, current and peak cached items, cached pages, native range request count, and total items received. It has no background thread, async runtime, global cache, or external dependency.

## View virtualization

`VirtualizedDetailsView` uses a WinUI `ItemsRepeater` inside a `ScrollViewer` with a vertical `StackLayout`. Rows have a fixed 32 effective-pixel height and share a lightweight four-column template for Name, Date modified, Type, and Size. The row template uses one-time bindings, text trimming, and no per-row service or timer objects.

`ElementPrepared` and `ElementClearing` maintain current, peak, prepared, and cleared element counters. Cleared elements are not retained. During fast scrolling, the realized count should remain proportional to the viewport plus the layout's small realization buffer, rather than approaching 100,000.

## Frame and memory diagnostics

`FrameMetricsCollector` subscribes to `CompositionTarget.Rendering` while the page is loaded. It stores at most 120 frame intervals in a ring buffer and publishes approximate FPS, average frame time, maximum frame time, and sample count no more than once per second. The page also displays the current process working set in MiB using the supported `Process.WorkingSet64` property. The collector unsubscribes on unload and avoids duplicate subscriptions.

## Manual check

Open `OpenExplorer.sln` in Visual Studio, select `OpenExplorer.UI` as the startup project, choose `Debug | x64`, and launch using the existing WinUI template workflow. The application is packaged, so the normal repository launch command is:

```powershell
Set-Location 'D:\source\repos\OpenExplorer'
.\tools\run.ps1
```

The non-visual startup check is:

```powershell
.\tools\run.ps1 -SmokeTest
```

The script uses the project-supported Windows App SDK WinApp development deployment and packaged activation path. Smoke mode confirms process and top-level-window startup, but is not a visual performance benchmark. Observe the details rows while dragging the vertical scrollbar quickly. The diagnostics should show the current profile's actual item count, bounded page/item cache values, native range request count, and a realized-element count tied to the viewport. The visible API line is sourced from the real Rust DLL and reads `Native API version: 3`.

Measured FPS and working-set values are machine-dependent. This gate does not claim a fixed FPS result and does not replace profiling on representative hardware.

## Current limitations

The active data is a snapshot of one local directory and is not refreshed. There is no navigation, watcher, sorting, filtering, selection, activation, icon loading, thumbnail loading, or file operation. The details header and rows share a minimum content width so horizontal scrolling remains available when the window is narrow.
