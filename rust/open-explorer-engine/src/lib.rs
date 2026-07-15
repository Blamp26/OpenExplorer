//! Engine facade for immutable synthetic and local-directory snapshots.

use std::fs;
use std::path::Path;
use std::time::{SystemTime, UNIX_EPOCH};

use open_explorer_domain::{ExplorerError, ExplorerErrorCode, ExplorerItem, ExplorerItemKind};

pub const API_VERSION: u32 = 3;
pub const MAX_RANGE_COUNT: u32 = 4096;

pub struct ExplorerEngine;

impl ExplorerEngine {
    pub const fn new() -> Result<Self, ExplorerError> {
        Ok(Self)
    }
    pub const fn api_version(&self) -> u32 {
        API_VERSION
    }
}

pub struct ExplorerSnapshot {
    source: SnapshotSource,
}

enum SnapshotSource {
    Synthetic { item_count: u64 },
    Local { items: Vec<ExplorerItem> },
}

impl ExplorerSnapshot {
    pub const fn synthetic(item_count: u64) -> Self {
        Self {
            source: SnapshotSource::Synthetic { item_count },
        }
    }

    pub fn local_directory(path: &str) -> Result<Self, ExplorerError> {
        let path = Path::new(path);
        if !path.is_absolute() {
            return Err(error(ExplorerErrorCode::InvalidArgument));
        }
        let metadata = fs::metadata(path).map_err(map_io_error)?;
        if !metadata.is_dir() {
            return Err(error(ExplorerErrorCode::NotDirectory));
        }
        let mut items = Vec::new();
        for (index, entry) in fs::read_dir(path).map_err(map_io_error)?.enumerate() {
            let entry = entry.map_err(map_io_error)?;
            let metadata = entry.metadata().map_err(map_io_error)?;
            let kind = if metadata.is_dir() {
                ExplorerItemKind::Directory
            } else {
                ExplorerItemKind::File
            };
            let name_os = entry.file_name();
            let name_lossy = name_os.to_string_lossy();
            let name_was_lossy = matches!(name_lossy, std::borrow::Cow::Owned(_));
            let modified_unix_ms =
                system_time_to_unix_ms(metadata.modified().map_err(map_io_error)?)?;
            let size = (kind == ExplorerItemKind::File).then_some(metadata.len());
            let item_id = u64::try_from(index)
                .ok()
                .and_then(|value| value.checked_add(1))
                .ok_or_else(|| error(ExplorerErrorCode::Internal))?;
            items.push(ExplorerItem {
                item_id,
                name: name_lossy.into_owned(),
                modified_unix_ms,
                size,
                kind,
                name_was_lossy,
            });
        }
        Ok(Self {
            source: SnapshotSource::Local { items },
        })
    }

    pub fn count(&self) -> u64 {
        match &self.source {
            SnapshotSource::Synthetic { item_count } => *item_count,
            SnapshotSource::Local { items } => items.len() as u64,
        }
    }

    pub const fn materialized_item_count(&self) -> usize {
        match &self.source {
            SnapshotSource::Synthetic { .. } => 0,
            SnapshotSource::Local { items } => items.len(),
        }
    }

    pub fn get_range(
        &self,
        start: u64,
        requested_count: u32,
    ) -> Result<Vec<ExplorerItem>, ExplorerError> {
        if requested_count > MAX_RANGE_COUNT {
            return Err(error(ExplorerErrorCode::InvalidArgument));
        }
        if start > self.count() {
            return Err(error(ExplorerErrorCode::OutOfRange));
        }
        let available = self.count().saturating_sub(start);
        let actual = u64::from(requested_count).min(available);
        if let SnapshotSource::Local { items } = &self.source {
            let start_index =
                usize::try_from(start).map_err(|_| error(ExplorerErrorCode::OutOfRange))?;
            let end = start_index
                .checked_add(
                    usize::try_from(actual).map_err(|_| error(ExplorerErrorCode::OutOfRange))?,
                )
                .ok_or_else(|| error(ExplorerErrorCode::OutOfRange))?;
            return Ok(items[start_index..end].to_vec());
        }
        let mut result = Vec::with_capacity(
            usize::try_from(actual).map_err(|_| error(ExplorerErrorCode::Internal))?,
        );
        for offset in 0..actual {
            result.push(create_synthetic_item(start + offset));
        }
        Ok(result)
    }
}

fn create_synthetic_item(index: u64) -> ExplorerItem {
    let is_directory = index.is_multiple_of(17);
    let extension = if is_directory {
        ""
    } else {
        ["txt", "pdf", "jpg", "png", "zip", "exe", "dll", "mp4", ""][index as usize % 9]
    };
    let name = if index == 42 {
        format!("Résumé директория №{index}")
    } else if index == 4242 {
        format!("Long-{}-{index}", "x".repeat(280))
    } else if is_directory {
        format!("Folder {index:05}")
    } else if extension.is_empty() {
        format!("Document {index:05}")
    } else {
        format!("Document {index:05}.{extension}")
    };
    ExplorerItem {
        item_id: index + 1,
        name,
        modified_unix_ms: 1_609_459_200_000_i64 + (index as i64 * 420_000),
        size: (!is_directory).then_some(1_024 + (index * 7_919 % 9_000_000)),
        kind: if is_directory {
            ExplorerItemKind::Directory
        } else {
            ExplorerItemKind::File
        },
        name_was_lossy: false,
    }
}

fn error(code: ExplorerErrorCode) -> ExplorerError {
    ExplorerError::new(code, None, true)
}

fn map_io_error(error: std::io::Error) -> ExplorerError {
    use std::io::ErrorKind;
    let code = match error.kind() {
        ErrorKind::NotFound => ExplorerErrorCode::NotFound,
        ErrorKind::PermissionDenied => ExplorerErrorCode::AccessDenied,
        _ => ExplorerErrorCode::IoError,
    };
    ExplorerError::new(code, error.raw_os_error(), true)
}

fn system_time_to_unix_ms(time: SystemTime) -> Result<i64, ExplorerError> {
    let millis: i128 = match time.duration_since(UNIX_EPOCH) {
        Ok(value) => {
            i128::try_from(value.as_millis()).map_err(|_| error(ExplorerErrorCode::Internal))?
        }
        Err(_) => -i128::try_from(
            UNIX_EPOCH
                .duration_since(time)
                .map_err(|_| error(ExplorerErrorCode::Internal))?
                .as_millis(),
        )
        .map_err(|_| error(ExplorerErrorCode::Internal))?,
    };
    i64::try_from(millis).map_err(|_| error(ExplorerErrorCode::Internal))
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn synthetic_snapshot_is_lazy_and_deterministic() {
        let snapshot = ExplorerSnapshot::synthetic(100_000);
        assert_eq!(snapshot.count(), 100_000);
        assert_eq!(snapshot.materialized_item_count(), 0);
        assert_eq!(
            snapshot.get_range(0, 2).unwrap(),
            snapshot.get_range(0, 2).unwrap()
        );
    }

    #[test]
    fn synthetic_snapshot_has_unicode_and_long_names() {
        let snapshot = ExplorerSnapshot::synthetic(10_000);
        assert!(snapshot.get_range(42, 1).unwrap()[0].name.contains('é'));
        assert!(snapshot.get_range(4242, 1).unwrap()[0].name.chars().count() > 260);
    }
}
