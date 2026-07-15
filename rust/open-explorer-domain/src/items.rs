//! Platform-independent immutable explorer item values.

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum ExplorerItemKind {
    File = 1,
    Directory = 2,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct ExplorerItem {
    pub item_id: u64,
    pub name: String,
    pub modified_unix_ms: i64,
    pub size: Option<u64>,
    pub kind: ExplorerItemKind,
}
