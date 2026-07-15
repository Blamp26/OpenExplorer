# Native ABI v2 snapshot range API

The native API version is `2`. Existing exports remain stable: `fe_api_version`, `fe_engine_create`, and `fe_engine_destroy`.

Snapshot support adds `fe_engine_create_synthetic_snapshot`, `fe_snapshot_count`, `fe_snapshot_get_range_requirements`, `fe_snapshot_get_range`, and `fe_snapshot_destroy`.

Fallible functions return stable `u32` statuses: `0 OK`, `1 NULL_POINTER`, `2 INVALID_ARGUMENT`, `3 OUT_OF_RANGE`, `4 BUFFER_TOO_SMALL`, `5 INTERNAL_ERROR`, and `6 PANIC`. Interop maps every non-zero status to an exception containing the operation, numeric value, and symbolic name.

The snapshot handle is separate from the engine handle. It owns its immutable synthetic source state and remains valid after engine disposal. Each handle is destroyed exactly once with `fe_snapshot_destroy`; null destruction is safe. Managed ownership is represented by `SafeSnapshotHandle`.

## Record layout

`FeItemRecord` is a 40-byte `repr(C)` record with verified offsets:

| Field | Offset | Size |
| --- | ---: | ---: |
| `item_id` | 0 | 8 |
| `modified_unix_ms` | 8 | 8 |
| `size` | 16 | 8 |
| `name_offset` | 24 | 4 |
| `name_length` | 28 | 4 |
| `kind` | 32 | 4 |
| `flags` | 36 | 4 |

Kinds are `1` file and `2` directory. Flag bit 0 means size is present; directory records leave it unset. Records contain no pointers or fixed filename arrays.

Each response has one contiguous record array and one contiguous UTF-8 arena. Name offsets and lengths identify non-null-terminated spans relative to the supplied arena. The managed layer decodes only those spans with strict UTF-8 validation, preserving Unicode and names longer than 260 characters.

## Range protocol

Call requirements first to obtain the truncated item count and exact UTF-8 byte count, then call the data function once. Requests above `4096` are invalid. Zero requests and `start == count` are valid; `start > count` returns `OUT_OF_RANGE`. Data capacities are validated before any write; insufficient record or text capacity returns `BUFFER_TOO_SMALL` with no partial range. Zero-item calls permit null data buffers, while output pointers remain required.

The current source is deterministic synthetic data. No JSON, filesystem path, or per-item FFI call crosses the boundary.
