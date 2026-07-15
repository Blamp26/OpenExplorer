# 0001 Use WinUI 3 and Rust

## Status
Accepted

## Context
OpenExplorer needs a native Windows UI and a platform-independent engine boundary.

## Decision
Use WinUI 3 for the packaged desktop UI and Rust for the engine and native ABI.

## Consequences
The UI follows Windows conventions while engine code can remain tightly scoped and testable. Two toolchains must be built together.

## Rejected alternatives
WPF, WinForms, UWP, MAUI, Avalonia, Tauri, Electron, and WebView UI.
