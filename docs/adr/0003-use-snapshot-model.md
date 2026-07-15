# 0003 Use a snapshot model

## Status
Accepted

## Context
Directories and search results change while UI consumers read them.

## Decision
Future engine APIs will identify stable snapshots and expose bounded ranges and batches.

## Consequences
UI updates can be consistent without materializing huge directories in one message.

## Rejected alternatives
Live mutable collections owned by the UI and one-shot unbounded payloads.
