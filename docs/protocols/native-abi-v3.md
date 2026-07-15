# Native ABI v3

Native API v3 preserves the v1 engine exports and v2 synthetic snapshot exports, and adds `fe_engine_open_local_directory_snapshot`.

Preserved exports are `fe_api_version`, `fe_engine_create`, `fe_engine_destroy`, `fe_engine_create_synthetic_snapshot`, `fe_snapshot_count`, `fe_snapshot_get_range_requirements`, `fe_snapshot_get_range`, and `fe_snapshot_destroy`. The new export accepts an engine handle, a UTF-8 path pointer and byte length, and an output snapshot handle pointer.

Status values are stable: `0 OK`, `1 NULL_POINTER`, `2 INVALID_ARGUMENT`, `3 OUT_OF_RANGE`, `4 BUFFER_TOO_SMALL`, `5 INTERNAL_ERROR`, `6 PANIC`, `7 NOT_FOUND`, `8 ACCESS_DENIED`, `9 NOT_DIRECTORY`, `10 INVALID_UTF8`, and `11 IO_ERROR`.

Paths must be non-empty, valid UTF-8, absolute directory paths. The path pointer is borrowed for the call only and is never retained. The provider does not canonicalize the path or impose a 260-character limit. Snapshot creation enumerates immediate children once and owns immutable metadata. Snapshot handles remain valid after their engine is disposed and are destroyed independently; null destruction is safe.

The record remains a 40-byte `FeItemRecord`: `item_id` offset 0, `modified_unix_ms` offset 8, `size` offset 16, `name_offset` offset 24, `name_length` offset 28, `kind` offset 32, and `flags` offset 36. Kind `1` is file and `2` is directory. Flag bit 0 means size is present; bit 1 means the filename required lossy Unicode conversion.

Range access uses the existing two-call pattern. Requirements return the exact record count and UTF-8 arena byte count; the data call writes one contiguous record array and one contiguous, non-null-terminated UTF-8 arena. Requests are limited to 4,096 items, truncate at the snapshot end, and `start == count` is valid. Capacities are checked before any write; insufficient buffers return `BUFFER_TOO_SMALL` atomically.

All valid Unicode names are preserved. Non-Unicode Windows names are lossily converted with replacement characters and flagged. Metadata failures abort snapshot creation rather than fabricating values or returning a partial snapshot. v2 consumers must continue using the historical ABI; v3 is required for local-directory snapshots.
