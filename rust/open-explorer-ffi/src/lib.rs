//! Stable C ABI for engine creation and immutable synthetic snapshots.
//!
//! All unsafe code in the repository is intentionally confined to this crate.

use std::ffi::c_void;
use std::mem::{offset_of, size_of};

use open_explorer_engine::{ExplorerEngine, ExplorerSnapshot, API_VERSION, MAX_RANGE_COUNT};

type EngineHandle = c_void;
type SnapshotHandle = c_void;

pub const STATUS_OK: u32 = 0;
pub const STATUS_NULL_POINTER: u32 = 1;
pub const STATUS_INVALID_ARGUMENT: u32 = 2;
pub const STATUS_OUT_OF_RANGE: u32 = 3;
pub const STATUS_BUFFER_TOO_SMALL: u32 = 4;
pub const STATUS_INTERNAL_ERROR: u32 = 5;
pub const STATUS_PANIC: u32 = 6;

#[repr(C)]
#[derive(Clone, Copy, Debug, Default, PartialEq, Eq)]
pub struct FeItemRecord {
    pub item_id: u64,
    pub modified_unix_ms: i64,
    pub size: u64,
    pub name_offset: u32,
    pub name_length: u32,
    pub kind: u32,
    pub flags: u32,
}

const SIZE_FLAG: u32 = 1;

#[no_mangle]
pub extern "C" fn fe_api_version() -> u32 {
    API_VERSION
}

#[no_mangle]
pub extern "C" fn fe_engine_create() -> *mut EngineHandle {
    std::panic::catch_unwind(|| {
        ExplorerEngine::new()
            .map(|engine| Box::into_raw(Box::new(engine)).cast::<EngineHandle>())
            .unwrap_or(std::ptr::null_mut())
    })
    .unwrap_or(std::ptr::null_mut())
}

/// Destroys an engine handle. Null is safe.
///
/// # Safety
/// `handle` must be null or a live handle returned by `fe_engine_create`.
#[no_mangle]
pub unsafe extern "C" fn fe_engine_destroy(handle: *mut EngineHandle) {
    let _ = std::panic::catch_unwind(|| {
        if !handle.is_null() {
            // SAFETY: the caller contract requires a live engine allocation.
            unsafe { drop(Box::from_raw(handle.cast::<ExplorerEngine>())) };
        }
    });
}

#[no_mangle]
/// Creates an immutable synthetic snapshot owned independently from `engine`.
///
/// # Safety
/// `engine` and `output` must be valid pointers when non-null; the returned
/// handle must be destroyed with `fe_snapshot_destroy`.
pub unsafe extern "C" fn fe_engine_create_synthetic_snapshot(
    engine: *mut EngineHandle,
    item_count: u64,
    output: *mut *mut SnapshotHandle,
) -> u32 {
    catch_status(|| {
        if engine.is_null() || output.is_null() {
            return STATUS_NULL_POINTER;
        }
        if item_count == 0 {
            return STATUS_INVALID_ARGUMENT;
        }
        // SAFETY: validated non-null; the engine is only an ownership gate and
        // the snapshot owns its independent immutable source state.
        unsafe {
            *output = Box::into_raw(Box::new(ExplorerSnapshot::synthetic(item_count))).cast()
        };
        STATUS_OK
    })
}

#[no_mangle]
/// Reads the immutable logical item count.
///
/// # Safety
/// `snapshot` and `output` must be valid pointers to a live snapshot and output.
pub unsafe extern "C" fn fe_snapshot_count(snapshot: *mut SnapshotHandle, output: *mut u64) -> u32 {
    catch_status(|| {
        if snapshot.is_null() || output.is_null() {
            return STATUS_NULL_POINTER;
        }
        // SAFETY: validated live handle and output pointer.
        unsafe { *output = snapshot_ref(snapshot).count() };
        STATUS_OK
    })
}

#[no_mangle]
/// Computes the exact buffers needed for a range.
///
/// # Safety
/// All non-null pointers must be valid for the duration of the call.
pub unsafe extern "C" fn fe_snapshot_get_range_requirements(
    snapshot: *mut SnapshotHandle,
    start: u64,
    requested_count: u32,
    output_count: *mut u32,
    output_utf8_bytes: *mut u32,
) -> u32 {
    catch_status(|| {
        if snapshot.is_null() || output_count.is_null() || output_utf8_bytes.is_null() {
            return STATUS_NULL_POINTER;
        }
        if requested_count > MAX_RANGE_COUNT {
            return STATUS_INVALID_ARGUMENT;
        }
        let snapshot = snapshot_ref(snapshot);
        if start > snapshot.count() {
            return STATUS_OUT_OF_RANGE;
        }
        let items = match snapshot.get_range(start, requested_count) {
            Ok(items) => items,
            Err(_) => return STATUS_INTERNAL_ERROR,
        };
        let bytes = match utf8_bytes(&items) {
            Ok(bytes) => bytes,
            Err(status) => return status,
        };
        // SAFETY: validated output pointers.
        unsafe {
            *output_count = items.len() as u32;
            *output_utf8_bytes = bytes;
        }
        STATUS_OK
    })
}

#[no_mangle]
/// Writes one checked record array and UTF-8 arena for a range.
///
/// # Safety
/// Pointers must be valid for their declared capacities and the snapshot must
/// be live. Data buffers may be null only for zero-item or zero-byte outputs.
pub unsafe extern "C" fn fe_snapshot_get_range(
    snapshot: *mut SnapshotHandle,
    start: u64,
    requested_count: u32,
    records: *mut FeItemRecord,
    record_capacity: u32,
    utf8_buffer: *mut u8,
    utf8_capacity: u32,
    output_count: *mut u32,
    output_utf8_bytes: *mut u32,
) -> u32 {
    catch_status(|| {
        if snapshot.is_null() || output_count.is_null() || output_utf8_bytes.is_null() {
            return STATUS_NULL_POINTER;
        }
        if requested_count > MAX_RANGE_COUNT {
            return STATUS_INVALID_ARGUMENT;
        }
        let snapshot = snapshot_ref(snapshot);
        if start > snapshot.count() {
            return STATUS_OUT_OF_RANGE;
        }
        let items = match snapshot.get_range(start, requested_count) {
            Ok(items) => items,
            Err(_) => return STATUS_INTERNAL_ERROR,
        };
        let names_bytes = match utf8_names(&items) {
            Ok(names) => names,
            Err(status) => return status,
        };
        let item_count = items.len() as u32;
        if item_count > 0 && records.is_null() {
            return STATUS_NULL_POINTER;
        }
        if names_bytes > 0 && utf8_buffer.is_null() {
            return STATUS_NULL_POINTER;
        }
        if item_count > record_capacity || names_bytes > utf8_capacity {
            return STATUS_BUFFER_TOO_SMALL;
        }

        let mut offset = 0_u32;
        // SAFETY: capacities were checked before any write and the pointers are
        // non-null whenever the corresponding range is non-empty.
        unsafe {
            for (index, item) in items.iter().enumerate() {
                let length = u32::try_from(item.name.len()).unwrap_or(u32::MAX);
                *records.add(index) = FeItemRecord {
                    item_id: item.item_id,
                    modified_unix_ms: item.modified_unix_ms,
                    size: item.size.unwrap_or(0),
                    name_offset: offset,
                    name_length: length,
                    kind: item.kind as u32,
                    flags: if item.size.is_some() { SIZE_FLAG } else { 0 },
                };
                std::ptr::copy_nonoverlapping(
                    item.name.as_ptr(),
                    utf8_buffer.add(offset as usize),
                    length as usize,
                );
                offset = offset.saturating_add(length);
            }
            *output_count = item_count;
            *output_utf8_bytes = offset;
        }
        STATUS_OK
    })
}

#[no_mangle]
/// Destroys a snapshot handle. Null is safe.
///
/// # Safety
/// `snapshot` must be null or a live handle returned by snapshot creation.
pub unsafe extern "C" fn fe_snapshot_destroy(snapshot: *mut SnapshotHandle) {
    let _ = std::panic::catch_unwind(|| {
        if !snapshot.is_null() {
            // SAFETY: the caller contract requires a live snapshot allocation.
            unsafe { drop(Box::from_raw(snapshot.cast::<ExplorerSnapshot>())) };
        }
    });
}

fn catch_status(function: impl FnOnce() -> u32) -> u32 {
    std::panic::catch_unwind(std::panic::AssertUnwindSafe(function)).unwrap_or(STATUS_PANIC)
}

unsafe fn snapshot_ref<'a>(snapshot: *mut SnapshotHandle) -> &'a ExplorerSnapshot {
    // SAFETY: all callers validate non-null and require a live handle.
    unsafe { &*snapshot.cast::<ExplorerSnapshot>() }
}

fn utf8_bytes(items: &[open_explorer_domain::ExplorerItem]) -> Result<u32, u32> {
    items.iter().try_fold(0_u32, |total, item| {
        total
            .checked_add(u32::try_from(item.name.len()).map_err(|_| STATUS_INVALID_ARGUMENT)?)
            .ok_or(STATUS_INVALID_ARGUMENT)
    })
}

fn utf8_names(items: &[open_explorer_domain::ExplorerItem]) -> Result<u32, u32> {
    utf8_bytes(items)
}

const _: () = {
    assert!(size_of::<FeItemRecord>() == 40);
    assert!(offset_of!(FeItemRecord, item_id) == 0);
    assert!(offset_of!(FeItemRecord, modified_unix_ms) == 8);
    assert!(offset_of!(FeItemRecord, size) == 16);
    assert!(offset_of!(FeItemRecord, name_offset) == 24);
    assert!(offset_of!(FeItemRecord, name_length) == 28);
    assert!(offset_of!(FeItemRecord, kind) == 32);
    assert!(offset_of!(FeItemRecord, flags) == 36);
};

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn api_version_is_two() {
        assert_eq!(fe_api_version(), 2);
    }

    #[test]
    fn record_layout_is_stable() {
        assert_eq!(size_of::<FeItemRecord>(), 40);
        assert_eq!(offset_of!(FeItemRecord, flags), 36);
    }

    #[test]
    fn snapshot_range_is_stable_and_truncates() {
        let engine = fe_engine_create();
        let mut snapshot = std::ptr::null_mut();
        assert_eq!(
            unsafe { fe_engine_create_synthetic_snapshot(engine, 3, &mut snapshot) },
            STATUS_OK
        );
        let mut count = 0;
        let mut bytes = 0;
        assert_eq!(
            unsafe { fe_snapshot_get_range_requirements(snapshot, 2, 10, &mut count, &mut bytes) },
            STATUS_OK
        );
        assert_eq!(count, 1);
        assert!(bytes > 0);
        unsafe {
            fe_snapshot_destroy(snapshot);
            fe_engine_destroy(engine);
        }
    }

    #[test]
    fn snapshot_range_contract_covers_buffers_unicode_and_ownership() {
        let engine = fe_engine_create();
        let mut first = std::ptr::null_mut();
        let mut second = std::ptr::null_mut();
        assert_eq!(
            unsafe { fe_engine_create_synthetic_snapshot(engine, 100_000, &mut first) },
            STATUS_OK
        );
        assert_eq!(
            unsafe { fe_engine_create_synthetic_snapshot(engine, 100_000, &mut second) },
            STATUS_OK
        );
        unsafe { fe_engine_destroy(engine) };

        let mut snapshot_count = 0_u64;
        let mut count = 0_u32;
        let mut bytes = 0;
        assert_eq!(
            unsafe { fe_snapshot_count(first, &mut snapshot_count) },
            STATUS_OK
        );
        assert_eq!(snapshot_count, 100_000);
        assert_eq!(
            unsafe {
                fe_snapshot_get_range_requirements(first, 100_000, 0, &mut count, &mut bytes)
            },
            STATUS_OK
        );
        assert_eq!((count, bytes), (0, 0));
        assert_eq!(
            unsafe {
                fe_snapshot_get_range_requirements(first, 100_001, 0, &mut count, &mut bytes)
            },
            STATUS_OUT_OF_RANGE
        );
        assert_eq!(
            unsafe { fe_snapshot_get_range_requirements(first, 0, 4097, &mut count, &mut bytes) },
            STATUS_INVALID_ARGUMENT
        );

        assert_eq!(
            unsafe { fe_snapshot_get_range_requirements(first, 42, 1, &mut count, &mut bytes) },
            STATUS_OK
        );
        assert_eq!(count, 1);
        let mut records = vec![FeItemRecord::default(); 1];
        let mut text = vec![0_u8; bytes as usize];
        let mut written_count = 777;
        let mut written_bytes = 777;
        assert_eq!(
            unsafe {
                fe_snapshot_get_range(
                    first,
                    42,
                    1,
                    records.as_mut_ptr(),
                    1,
                    text.as_mut_ptr(),
                    text.len() as u32,
                    &mut written_count,
                    &mut written_bytes,
                )
            },
            STATUS_OK
        );
        assert_eq!((written_count, written_bytes), (1, bytes));
        assert_eq!(std::str::from_utf8(&text).unwrap(), "Résumé директория №42");

        written_count = 777;
        written_bytes = 777;
        assert_eq!(
            unsafe {
                fe_snapshot_get_range(
                    first,
                    0,
                    2,
                    records.as_mut_ptr(),
                    1,
                    text.as_mut_ptr(),
                    text.len() as u32,
                    &mut written_count,
                    &mut written_bytes,
                )
            },
            STATUS_BUFFER_TOO_SMALL
        );
        assert_eq!((written_count, written_bytes), (777, 777));

        assert_eq!(
            unsafe { fe_snapshot_get_range_requirements(first, 4_242, 1, &mut count, &mut bytes) },
            STATUS_OK
        );
        let mut long_text = vec![0_u8; bytes as usize];
        assert_eq!(
            unsafe {
                fe_snapshot_get_range(
                    first,
                    4_242,
                    1,
                    records.as_mut_ptr(),
                    1,
                    long_text.as_mut_ptr(),
                    long_text.len() as u32,
                    &mut written_count,
                    &mut written_bytes,
                )
            },
            STATUS_OK
        );
        assert!(std::str::from_utf8(&long_text).unwrap().chars().count() > 260);

        assert_eq!(
            unsafe {
                fe_snapshot_get_range_requirements(first, 0, 1, std::ptr::null_mut(), &mut bytes)
            },
            STATUS_NULL_POINTER
        );
        unsafe {
            fe_snapshot_destroy(first);
            fe_snapshot_destroy(second);
        }
    }

    #[test]
    fn invalid_and_null_calls_return_statuses() {
        let mut count = 0;
        let mut bytes = 0;
        assert_eq!(
            unsafe {
                fe_snapshot_get_range_requirements(
                    std::ptr::null_mut(),
                    0,
                    0,
                    &mut count,
                    &mut bytes,
                )
            },
            STATUS_NULL_POINTER
        );
        assert_eq!(
            unsafe {
                fe_snapshot_get_range_requirements(
                    std::ptr::null_mut(),
                    0,
                    4097,
                    &mut count,
                    &mut bytes,
                )
            },
            STATUS_NULL_POINTER
        );
        unsafe { fe_snapshot_destroy(std::ptr::null_mut()) };
    }
}
