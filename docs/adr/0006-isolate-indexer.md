# 0006 Isolate Indexer

## Status
Accepted

## Context
Indexing may require long-lived work and different privileges from the UI.

## Decision
Run indexing as a separate process with a versioned protocol.

## Consequences
The UI stays responsive and indexer failures are contained.

## Rejected alternatives
Running an indexer loop inside the UI process.
