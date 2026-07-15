# 0005 Isolate ShellHost

## Status
Accepted

## Context
Third-party Shell extensions can be unreliable or unsafe for the main UI process.

## Decision
Run Shell integration in a separate ShellHost process.

## Consequences
The UI remains isolated, with process and protocol overhead at the boundary.

## Rejected alternatives
Loading external Shell extensions directly inside WinUI.
