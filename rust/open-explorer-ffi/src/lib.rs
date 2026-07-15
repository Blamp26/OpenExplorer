//! Stable C ABI for engine and immutable snapshots. All repository Rust unsafe
//! code is intentionally confined to this crate.

use std::ffi::c_void;
use std::mem::{offset_of, size_of};
use std::slice;
use std::str;

use open_explorer_domain::{ExplorerErrorCode, ExplorerItem};
use open_explorer_engine::{
    ExplorerEngine, ExplorerSnapshot, API_VERSION, MAX_RANGE_COUNT, SORT_DIRECTION_ASCENDING,
    SORT_DIRECTION_DESCENDING, SORT_FIELD_DATE_MODIFIED, SORT_FIELD_NAME, SORT_FIELD_SIZE,
    SORT_FIELD_TYPE, SORT_FLAG_FOLDERS_FIRST,
};

type EngineHandle = c_void;
type SnapshotHandle = c_void;

pub const STATUS_OK: u32 = 0;
pub const STATUS_NULL_POINTER: u32 = 1;
pub const STATUS_INVALID_ARGUMENT: u32 = 2;
pub const STATUS_OUT_OF_RANGE: u32 = 3;
pub const STATUS_BUFFER_TOO_SMALL: u32 = 4;
pub const STATUS_INTERNAL_ERROR: u32 = 5;
pub const STATUS_PANIC: u32 = 6;
pub const STATUS_NOT_FOUND: u32 = 7;
pub const STATUS_ACCESS_DENIED: u32 = 8;
pub const STATUS_NOT_DIRECTORY: u32 = 9;
pub const STATUS_INVALID_UTF8: u32 = 10;
pub const STATUS_IO_ERROR: u32 = 11;

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
const LOSSY_NAME_FLAG: u32 = 1 << 1;

#[no_mangle]
pub extern "C" fn fe_api_version() -> u32 {
    API_VERSION
}

#[no_mangle]
pub extern "C" fn fe_engine_create() -> *mut EngineHandle {
    std::panic::catch_unwind(|| {
        ExplorerEngine::new()
            .map(|engine| Box::into_raw(Box::new(engine)).cast())
            .unwrap_or(std::ptr::null_mut())
    })
    .unwrap_or(std::ptr::null_mut())
}

#[no_mangle]
/// # Safety
/// The handle is null or a live engine handle returned by `fe_engine_create`.
pub unsafe extern "C" fn fe_engine_destroy(handle: *mut EngineHandle) {
    let _ = std::panic::catch_unwind(|| {
        if !handle.is_null() {
            unsafe { drop(Box::from_raw(handle.cast::<ExplorerEngine>())) };
        }
    });
}

#[no_mangle]
/// # Safety
/// Pointers are null only where the documented ABI permits; output points to writable storage.
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
        unsafe {
            *output = Box::into_raw(Box::new(ExplorerSnapshot::synthetic(item_count))).cast();
        }
        STATUS_OK
    })
}

#[no_mangle]
/// # Safety
/// The engine and output are live pointers; the path points to `path_length` readable bytes.
pub unsafe extern "C" fn fe_engine_open_local_directory_snapshot(
    engine: *mut EngineHandle,
    path: *const u8,
    path_length: u32,
    output: *mut *mut SnapshotHandle,
) -> u32 {
    catch_status(|| {
        if engine.is_null() || output.is_null() {
            return STATUS_NULL_POINTER;
        }
        unsafe {
            *output = std::ptr::null_mut();
        }
        if path_length == 0 {
            return STATUS_INVALID_ARGUMENT;
        }
        if path.is_null() {
            return STATUS_NULL_POINTER;
        }
        if path_length > 1_048_576 {
            return STATUS_INVALID_ARGUMENT;
        }
        let bytes = unsafe { slice::from_raw_parts(path, path_length as usize) };
        let path = match str::from_utf8(bytes) {
            Ok(value) => value,
            Err(_) => return STATUS_INVALID_UTF8,
        };
        match ExplorerSnapshot::local_directory(path) {
            Ok(snapshot) => {
                unsafe {
                    *output = Box::into_raw(Box::new(snapshot)).cast();
                }
                STATUS_OK
            }
            Err(error) => error_status(error.code),
        }
    })
}

#[no_mangle]
/// # Safety
/// The source is a live snapshot handle and output points to writable storage.
pub unsafe extern "C" fn fe_snapshot_create_sorted_view(
    source: *mut SnapshotHandle,
    sort_field: u32,
    sort_direction: u32,
    sort_flags: u32,
    output: *mut *mut SnapshotHandle,
) -> u32 {
    catch_status(|| {
        if source.is_null() || output.is_null() {
            return STATUS_NULL_POINTER;
        }
        unsafe {
            *output = std::ptr::null_mut();
        }
        if !matches!(
            sort_field,
            SORT_FIELD_NAME | SORT_FIELD_DATE_MODIFIED | SORT_FIELD_TYPE | SORT_FIELD_SIZE
        ) || !matches!(
            sort_direction,
            SORT_DIRECTION_ASCENDING | SORT_DIRECTION_DESCENDING
        ) || sort_flags & !SORT_FLAG_FOLDERS_FIRST != 0
        {
            return STATUS_INVALID_ARGUMENT;
        }
        match snapshot_ref(source).create_sorted_view(sort_field, sort_direction, sort_flags) {
            Ok(view) => {
                unsafe {
                    *output = Box::into_raw(Box::new(view)).cast();
                }
                STATUS_OK
            }
            Err(error) => error_status(error.code),
        }
    })
}

#[no_mangle]
/// # Safety
/// Snapshot and output are valid pointers to a live snapshot and writable storage.
pub unsafe extern "C" fn fe_snapshot_count(snapshot: *mut SnapshotHandle, output: *mut u64) -> u32 {
    catch_status(|| {
        if snapshot.is_null() || output.is_null() {
            return STATUS_NULL_POINTER;
        }
        unsafe {
            *output = snapshot_ref(snapshot).count();
        }
        STATUS_OK
    })
}

#[no_mangle]
/// # Safety
/// Snapshot and output are valid pointers to a live snapshot and writable storage.
pub unsafe extern "C" fn fe_snapshot_find_item_index(
    snapshot: *mut SnapshotHandle,
    item_id: u64,
    output: *mut u64,
) -> u32 {
    catch_status(|| {
        if snapshot.is_null() || output.is_null() {
            return STATUS_NULL_POINTER;
        }
        match snapshot_ref(snapshot).find_item_index(item_id) {
            Some(index) => {
                unsafe { *output = index };
                STATUS_OK
            }
            None => STATUS_NOT_FOUND,
        }
    })
}

#[no_mangle]
/// # Safety
/// Output pointers are writable and data pointers are valid for their declared capacities.
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
        match snapshot.get_range(start, requested_count) {
            Ok(items) => match utf8_bytes(&items) {
                Ok(bytes) => {
                    unsafe {
                        *output_count = items.len() as u32;
                        *output_utf8_bytes = bytes;
                    }
                    STATUS_OK
                }
                Err(status) => status,
            },
            Err(error) => error_status(error.code),
        }
    })
}

#[no_mangle]
/// # Safety
/// Snapshot and output pointers are valid; supplied buffers are valid for their capacities.
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
        let items = match snapshot_ref(snapshot).get_range(start, requested_count) {
            Ok(items) => items,
            Err(error) => return error_status(error.code),
        };
        let bytes = match utf8_bytes(&items) {
            Ok(value) => value,
            Err(status) => return status,
        };
        let count = match u32::try_from(items.len()) {
            Ok(value) => value,
            Err(_) => return STATUS_INTERNAL_ERROR,
        };
        if count > 0 && records.is_null() || bytes > 0 && utf8_buffer.is_null() {
            return STATUS_NULL_POINTER;
        }
        if count > record_capacity || bytes > utf8_capacity {
            return STATUS_BUFFER_TOO_SMALL;
        }
        let mut offset = 0_u32;
        for (index, item) in items.iter().enumerate() {
            let length = match u32::try_from(item.name.len()) {
                Ok(value) => value,
                Err(_) => return STATUS_INTERNAL_ERROR,
            };
            let flags = if item.size.is_some() { SIZE_FLAG } else { 0 }
                | if item.name_was_lossy {
                    LOSSY_NAME_FLAG
                } else {
                    0
                };
            unsafe {
                *records.add(index) = FeItemRecord {
                    item_id: item.item_id,
                    modified_unix_ms: item.modified_unix_ms,
                    size: item.size.unwrap_or(0),
                    name_offset: offset,
                    name_length: length,
                    kind: item.kind as u32,
                    flags,
                };
                std::ptr::copy_nonoverlapping(
                    item.name.as_ptr(),
                    utf8_buffer.add(offset as usize),
                    length as usize,
                );
            }
            offset = match offset.checked_add(length) {
                Some(value) => value,
                None => return STATUS_INTERNAL_ERROR,
            };
        }
        unsafe {
            *output_count = count;
            *output_utf8_bytes = bytes;
        }
        STATUS_OK
    })
}

#[no_mangle]
/// # Safety
/// The handle is null or a live snapshot handle returned by a snapshot creation export.
pub unsafe extern "C" fn fe_snapshot_destroy(snapshot: *mut SnapshotHandle) {
    let _ = std::panic::catch_unwind(|| {
        if !snapshot.is_null() {
            unsafe { drop(Box::from_raw(snapshot.cast::<ExplorerSnapshot>())) };
        }
    });
}

fn catch_status(function: impl FnOnce() -> u32) -> u32 {
    std::panic::catch_unwind(std::panic::AssertUnwindSafe(function)).unwrap_or(STATUS_PANIC)
}

unsafe fn snapshot_ref<'a>(snapshot: *mut SnapshotHandle) -> &'a ExplorerSnapshot {
    unsafe { &*snapshot.cast::<ExplorerSnapshot>() }
}

fn utf8_bytes(items: &[ExplorerItem]) -> Result<u32, u32> {
    items.iter().try_fold(0_u32, |total, item| {
        total
            .checked_add(u32::try_from(item.name.len()).map_err(|_| STATUS_INVALID_ARGUMENT)?)
            .ok_or(STATUS_INVALID_ARGUMENT)
    })
}

fn error_status(code: ExplorerErrorCode) -> u32 {
    match code {
        ExplorerErrorCode::InvalidArgument => STATUS_INVALID_ARGUMENT,
        ExplorerErrorCode::OutOfRange => STATUS_OUT_OF_RANGE,
        ExplorerErrorCode::NotFound => STATUS_NOT_FOUND,
        ExplorerErrorCode::AccessDenied => STATUS_ACCESS_DENIED,
        ExplorerErrorCode::NotDirectory => STATUS_NOT_DIRECTORY,
        ExplorerErrorCode::InvalidUtf8 => STATUS_INVALID_UTF8,
        ExplorerErrorCode::IoError => STATUS_IO_ERROR,
        _ => STATUS_INTERNAL_ERROR,
    }
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
    fn api_and_layout_are_v5_stable() {
        assert_eq!(fe_api_version(), 5);
        assert_eq!(size_of::<FeItemRecord>(), 40);
        assert_eq!(offset_of!(FeItemRecord, flags), 36);
    }

    #[test]
    fn local_directory_is_non_recursive_and_survives_engine_disposal() {
        let root = std::env::temp_dir().join(format!("openexplorer-ffi-{}", std::process::id()));
        let _ = std::fs::remove_dir_all(&root);
        std::fs::create_dir(&root).unwrap();
        std::fs::write(root.join("a.txt"), b"abc").unwrap();
        std::fs::create_dir(root.join("nested")).unwrap();
        std::fs::write(root.join("nested").join("deep.txt"), b"deep").unwrap();
        let engine = fe_engine_create();
        let mut snapshot = std::ptr::null_mut();
        let path = root.to_string_lossy().into_owned();
        assert_eq!(
            unsafe {
                fe_engine_open_local_directory_snapshot(
                    engine,
                    path.as_ptr(),
                    path.len() as u32,
                    &mut snapshot,
                )
            },
            STATUS_OK
        );
        unsafe { fe_engine_destroy(engine) };
        let mut count = 0;
        assert_eq!(
            unsafe { fe_snapshot_count(snapshot, &mut count) },
            STATUS_OK
        );
        assert_eq!(count, 2);
        unsafe { fe_snapshot_destroy(snapshot) };
        std::fs::remove_dir_all(root).unwrap();
    }

    #[test]
    fn range_validation_remains_atomic_and_null_safe() {
        let engine = fe_engine_create();
        let mut snapshot = std::ptr::null_mut();
        assert_eq!(
            unsafe { fe_engine_create_synthetic_snapshot(engine, 4, &mut snapshot) },
            STATUS_OK
        );
        let mut count = 91_u32;
        let mut bytes = 92_u32;
        assert_eq!(
            unsafe {
                fe_snapshot_get_range_requirements(snapshot, 0, 1, std::ptr::null_mut(), &mut bytes)
            },
            STATUS_NULL_POINTER
        );
        let mut record = FeItemRecord::default();
        let mut text = [0_u8; 64];
        assert_eq!(
            unsafe {
                fe_snapshot_get_range(
                    snapshot,
                    0,
                    2,
                    &mut record,
                    1,
                    text.as_mut_ptr(),
                    64,
                    &mut count,
                    &mut bytes,
                )
            },
            STATUS_BUFFER_TOO_SMALL
        );
        assert_eq!((count, bytes), (91, 92));
        unsafe {
            fe_snapshot_destroy(snapshot);
            fe_engine_destroy(engine);
        }
    }

    #[test]
    fn sorted_view_is_independent_and_validates_inputs() {
        let engine = fe_engine_create();
        let mut source = std::ptr::null_mut();
        assert_eq!(
            unsafe { fe_engine_create_synthetic_snapshot(engine, 64, &mut source) },
            STATUS_OK
        );
        let mut view = std::ptr::null_mut();
        assert_eq!(
            unsafe {
                fe_snapshot_create_sorted_view(
                    source,
                    SORT_FIELD_NAME,
                    SORT_DIRECTION_ASCENDING,
                    SORT_FLAG_FOLDERS_FIRST,
                    &mut view,
                )
            },
            STATUS_OK
        );
        assert!(!view.is_null());
        let mut invalid = std::ptr::null_mut();
        assert_eq!(
            unsafe {
                fe_snapshot_create_sorted_view(
                    source,
                    99,
                    SORT_DIRECTION_ASCENDING,
                    0,
                    &mut invalid,
                )
            },
            STATUS_INVALID_ARGUMENT
        );
        assert!(invalid.is_null());
        assert_eq!(
            unsafe {
                fe_snapshot_create_sorted_view(
                    std::ptr::null_mut(),
                    SORT_FIELD_NAME,
                    SORT_DIRECTION_ASCENDING,
                    0,
                    &mut invalid,
                )
            },
            STATUS_NULL_POINTER
        );
        unsafe { fe_snapshot_destroy(source) };
        let mut count = 0;
        assert_eq!(unsafe { fe_snapshot_count(view, &mut count) }, STATUS_OK);
        assert_eq!(count, 64);
        unsafe {
            fe_snapshot_destroy(view);
            fe_engine_destroy(engine);
        }
    }

    #[test]
    fn item_lookup_supports_base_sorted_and_not_found() {
        let engine = fe_engine_create();
        let mut source = std::ptr::null_mut();
        assert_eq!(
            unsafe { fe_engine_create_synthetic_snapshot(engine, 100_000, &mut source) },
            STATUS_OK
        );
        let mut index = 99;
        assert_eq!(
            unsafe { fe_snapshot_find_item_index(source, 100_000, &mut index) },
            STATUS_OK
        );
        assert_eq!(index, 99_999);
        assert_eq!(
            unsafe { fe_snapshot_find_item_index(source, 100_001, &mut index) },
            STATUS_NOT_FOUND
        );
        assert_eq!(
            unsafe { fe_snapshot_find_item_index(source, 1, std::ptr::null_mut()) },
            STATUS_NULL_POINTER
        );

        let mut view = std::ptr::null_mut();
        assert_eq!(
            unsafe {
                fe_snapshot_create_sorted_view(
                    source,
                    SORT_FIELD_NAME,
                    SORT_DIRECTION_DESCENDING,
                    0,
                    &mut view,
                )
            },
            STATUS_OK
        );
        unsafe {
            fe_snapshot_destroy(source);
            fe_engine_destroy(engine);
        }
        assert_eq!(
            unsafe { fe_snapshot_find_item_index(view, 100_000, &mut index) },
            STATUS_OK
        );
        assert!(index < 100_000);
        unsafe { fe_snapshot_destroy(view) };
    }
}
