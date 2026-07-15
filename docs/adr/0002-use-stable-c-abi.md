# 0002 Use a stable C ABI

## Status
Accepted

## Context
Managed and Rust releases need a narrow, explicit boundary.

## Decision
Expose only opaque handles and scalar functions through a stable C ABI.

## Consequences
Interop stays reviewable and avoids Rust layout or string ownership leaking into C#.

## Rejected alternatives
Rust ABI exports, JSON over the boundary, COM, and HTTP.
