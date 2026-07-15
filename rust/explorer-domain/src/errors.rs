//! Small, platform-independent error model.

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum ExplorerErrorCode {
    InvalidArgument,
    InitializationFailed,
    Unavailable,
    Internal,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct ExplorerError {
    pub code: ExplorerErrorCode,
    pub native_code: Option<i32>,
    pub recoverable: bool,
}

impl ExplorerError {
    pub const fn new(code: ExplorerErrorCode, native_code: Option<i32>, recoverable: bool) -> Self {
        Self {
            code,
            native_code,
            recoverable,
        }
    }
}
