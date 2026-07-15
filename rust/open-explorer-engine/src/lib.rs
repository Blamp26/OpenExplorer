//! Minimal engine façade; filesystem capabilities are intentionally deferred.

use open_explorer_domain::ExplorerError;

pub const API_VERSION: u32 = 1;

pub struct ExplorerEngine;

impl ExplorerEngine {
    pub const fn new() -> Result<Self, ExplorerError> {
        Ok(Self)
    }

    pub const fn api_version(&self) -> u32 {
        API_VERSION
    }
}
