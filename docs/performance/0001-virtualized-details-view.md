# Virtualized Details View performance gate

This gate establishes the UI rendering boundary before OpenExplorer connects a real directory snapshot to the application. It exercises the shape and cost of a Details View without filesystem access, native enumeration, or a second data transport.

## Source

The Application project owns `SyntheticFileItem` and `SyntheticFileItemList`. The default logical count is exactly 100,000 items, but construction performs no item generation. Each item is derived deterministically from its index and is generated only when indexed.

The source retains a FIFO cache with a default capacity of 1,024 items. Its diagnostics expose logical count, current and peak cache size, and total generated item count. The source has no background thread, async runtime, or external dependency.

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

The script uses the project-supported Windows App SDK WinApp development deployment and packaged activation path. Smoke mode confirms process and top-level-window startup, but is not a visual performance benchmark. Observe the details rows while dragging the vertical scrollbar quickly. The diagnostics should show 100,000 logical items, a bounded source cache, and a realized-element count tied to the viewport.

Measured FPS and working-set values are machine-dependent. This gate does not claim a fixed FPS result and does not replace profiling on representative hardware.

## Current limitations

The data is synthetic and deterministic. There is no filesystem enumeration, snapshot integration, sorting, filtering, selection, activation, icon loading, thumbnail loading, or file operation. The details header and rows share a minimum content width so horizontal scrolling remains available when the window is narrow.
