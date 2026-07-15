//! Stable C ABI for the FastExplorer native engine.
//!
//! # Safety
//!
//! A handle returned by `fe_engine_create` is owned by the caller and must be
//! released exactly once with `fe_engine_destroy`; null is accepted by destroy.

use std::ffi::c_void;

use fast_explorer_engine::{ExplorerEngine, API_VERSION};

type EngineHandle = c_void;

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

/// Destroys an engine handle produced by [`fe_engine_create`].
///
/// # Safety
/// The pointer must be null or a live handle returned by `fe_engine_create`
/// that has not already been destroyed.
#[no_mangle]
pub unsafe extern "C" fn fe_engine_destroy(handle: *mut EngineHandle) {
    let _ = std::panic::catch_unwind(|| {
        if !handle.is_null() {
            // SAFETY: the caller contract guarantees a live allocation created by
            // fe_engine_create and transfers ownership to this function.
            unsafe { drop(Box::from_raw(handle.cast::<ExplorerEngine>())) };
        }
    });
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn api_version_is_one() {
        assert_eq!(fe_api_version(), 1);
    }

    #[test]
    fn create_returns_non_null() {
        let handle = fe_engine_create();
        assert!(!handle.is_null());
        unsafe { fe_engine_destroy(handle) };
    }

    #[test]
    fn create_and_destroy_succeeds() {
        let handle = fe_engine_create();
        unsafe { fe_engine_destroy(handle) };
    }

    #[test]
    fn destroy_null_is_safe() {
        unsafe { fe_engine_destroy(std::ptr::null_mut()) };
    }

    #[test]
    fn independent_create_destroy_cycles_work() {
        for _ in 0..8 {
            let handle = fe_engine_create();
            assert!(!handle.is_null());
            unsafe { fe_engine_destroy(handle) };
        }
    }
}
