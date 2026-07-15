//! Provider-independent location schemes.

#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash)]
pub enum LocationScheme {
    File,
    Shell,
    Search,
    Archive,
    Sftp,
    Mtp,
}
