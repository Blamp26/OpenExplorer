# OpenExplorer

OpenExplorer is a Windows-only file manager foundation built around WinUI 3 and a Rust native engine. The current bootstrap proves the complete managed-to-native path: the packaged WinUI application composes a C# view model over `OpenExplorer.Interop`, which loads a stable Rust C ABI and displays native API version `1`.

## Stack and status

- WinUI 3, Windows App SDK, XAML, and C# on .NET 10.
- C# Contracts, Application, Interop, and Design System projects.
- Rust 2021 with an MSVC `cdylib` and a narrow C ABI.
- x64 is the first supported architecture.
- File browsing, search, indexing, and Shell integration are not implemented yet.

## Layout

`apps` contains the WinUI application and future Indexer/ShellHost processes. `dotnet` contains managed layers, `rust` contains the Cargo workspace, `tests` contains the native smoke test, and `docs` contains architecture decisions and future protocol/performance notes.

## Requirements

Windows, PowerShell, .NET SDK 10.0.301 or a compatible latest patch, Visual Studio MSBuild with WinUI support, and the stable `x86_64-pc-windows-msvc` Rust toolchain with Rustfmt and Clippy.

## Build and verify

```powershell
Set-Location 'D:\source\repos\OpenExplorer'
.\tools\build.ps1 -Configuration Debug
```

```powershell
Set-Location 'D:\source\repos\OpenExplorer'
.\tools\verify.ps1
```

The build compiles the Rust workspace, copies the native DLL into `artifacts`, and builds `OpenExplorer.sln` for `Debug|x64`.

OpenExplorer is a packaged WinUI application. Use the repository launch script so the generated loose package is deployed with the Windows App SDK WinApp tooling and activated with package identity:

```powershell
Set-Location 'D:\source\repos\OpenExplorer'
.\tools\run.ps1
```

The automated startup check is:

```powershell
.\tools\run.ps1 -SmokeTest
```

Smoke mode confirms that the process creates a top-level window and remains alive for five seconds, then closes only that launched instance. Direct execution of the packaged executable is not the supported repository workflow because packaged WinUI activation supplies the required package identity and Windows App Runtime registration.

## Deferred work

Directory enumeration, navigation, file operations, search, indexing, Shell integration, previews, thumbnails, and all other file-browser functionality are deliberately deferred.
