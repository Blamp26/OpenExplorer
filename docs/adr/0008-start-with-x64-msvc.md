# 0008 Start with x64 MSVC

## Status
Accepted

## Context
The first native build needs a single supported Windows architecture.

## Decision
Target `x86_64-pc-windows-msvc` and `Debug|x64` first.

## Consequences
Build and DLL loading are deterministic while other architectures remain future work.

## Rejected alternatives
Starting with GNU Rust targets or maintaining multiple architectures during bootstrap.
