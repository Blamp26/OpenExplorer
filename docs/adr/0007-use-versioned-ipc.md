# 0007 Use versioned IPC

## Status
Accepted

## Context
Indexer and ShellHost will evolve separately from the UI.

## Decision
Future communication will use versioned Named Pipes and Protocol Buffers.

## Consequences
Compatibility can be negotiated explicitly, with serialization and transport kept outside the current native ABI.

## Rejected alternatives
Unversioned ad hoc messages, HTTP, and JSON directory payloads.
