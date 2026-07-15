//! Platform-independent domain contracts for OpenExplorer.

pub mod errors;
pub mod identifiers;
pub mod locations;

pub use errors::{ExplorerError, ExplorerErrorCode};
pub use identifiers::{ItemId, LocationId, OperationId, SearchSessionId, SnapshotId};
pub use locations::LocationScheme;
