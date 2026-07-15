# Native ABI v4

Native ABI v4 keeps the v3 opaque engine and snapshot ABI and adds native sorted snapshot views. The API version is returned by `fe_api_version` and is `4`.

## Exports

Preserved exports:

- `fe_api_version`
- `fe_engine_create`
- `fe_engine_destroy`
- `fe_engine_create_synthetic_snapshot`
- `fe_engine_open_local_directory_snapshot`
- `fe_snapshot_count`
- `fe_snapshot_get_range_requirements`
- `fe_snapshot_get_range`
- `fe_snapshot_destroy`

The single new export is `fe_snapshot_create_sorted_view(source, sort_field, sort_direction, sort_flags, output)`. It returns a separate opaque handle and does not mutate the source.

Sort fields are `1 NAME`, `2 DATE_MODIFIED`, `3 TYPE`, and `4 SIZE`. Directions are `1 ASCENDING` and `2 DESCENDING`. Flag bit `0` is `FOLDERS_FIRST`; all other flag bits are invalid. Null source or output pointers return `NULL_POINTER`; invalid field, direction, or flags return `INVALID_ARGUMENT`.

## Ownership and ranges

A sorted view shares immutable source data and owns its logical permutation. It remains valid after its source snapshot and engine are destroyed. Destroying a view does not affect its source. Multiple views are independent. Range calls retain the two-call requirements/data pattern, the `4096` maximum request, the 40-byte `FeItemRecord` layout, and the contiguous UTF-8 arena.

## Comparison policy

Name uses Unicode lowercase, then original name, then item ID. Date uses Unix milliseconds, then name, then item ID. Type uses a lowercase extension without its leading dot; extensionless and `.gitignore`-style names use an empty key, while directories use a deterministic directory key. Size compares real file sizes. Missing sizes sort after present sizes in both directions. Folders-first grouping is independent of direction, and directories are name-ordered for Size sorting. Natural-number sorting and Shell collation are not used.

The source snapshot's IDs, names, timestamps, kinds, sizes, and lossy-name flags are unchanged. No JSON, managed callback, or filesystem re-enumeration crosses the ABI.
