# Native ABI v5

Native API v5 preserves all v4 exports, record layout, opaque snapshot ownership, two-call UTF-8 range transfer, and native sorted views. It adds:

```text
fe_snapshot_find_item_index(snapshot, item_id, out_index) -> u32
```

The lookup returns `OK` and the logical index when the stable `ItemId` is present, `NOT_FOUND` when absent, and `NULL_POINTER` for a null snapshot or output pointer. Interop maps `NOT_FOUND` to `false`; other non-zero statuses remain managed native exceptions.

Base snapshots resolve the current deterministic `ItemId` directly to its source index. Sorted views build an inverse logical-index permutation while the sorted view is created, so lookup is O(1) after the view’s O(n) construction. The inverse mapping shares the immutable source lifetime and remains valid after the source snapshot and engine are disposed.

No record layout changed: `FeItemRecord` remains 40 bytes with offsets `item_id=0`, `modified_unix_ms=8`, `size=16`, `name_offset=24`, `name_length=28`, `kind=32`, and `flags=36`. No JSON or managed full-directory ID map crosses the boundary.
