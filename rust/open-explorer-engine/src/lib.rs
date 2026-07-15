//! Minimal engine façade; filesystem capabilities are intentionally deferred.

use open_explorer_domain::{ExplorerError, ExplorerItem, ExplorerItemKind};

pub const API_VERSION: u32 = 2;
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

/// An immutable synthetic source. It owns only the logical count; item values
/// are created for each requested range and are never retained by the source.
pub struct ExplorerSnapshot {
    item_count: u64,
}

impl ExplorerSnapshot {
    pub const fn synthetic(item_count: u64) -> Self {
        Self { item_count }
    }

    pub const fn count(&self) -> u64 {
        self.item_count
    }

    pub const fn materialized_item_count(&self) -> usize {
        0
    }

    pub fn get_range(
        &self,
        start: u64,
        requested_count: u32,
    ) -> Result<Vec<ExplorerItem>, ExplorerError> {
        if requested_count > MAX_RANGE_COUNT {
            return Err(ExplorerError::new(
                open_explorer_domain::ExplorerErrorCode::InvalidArgument,
                None,
                true,
            ));
        }
        if start > self.item_count {
            return Err(ExplorerError::new(
                open_explorer_domain::ExplorerErrorCode::InvalidArgument,
                None,
                true,
            ));
        }

        let available = self.item_count.saturating_sub(start);
        let actual = u64::from(requested_count).min(available);
        let mut items = Vec::with_capacity(actual as usize);
        for offset in 0..actual {
            items.push(create_synthetic_item(start + offset));
        }
        Ok(items)
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
        format!("Long-{}-{}", "x".repeat(280), index)
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
    }
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
        let unicode = snapshot.get_range(42, 1).unwrap();
        let long = snapshot.get_range(4242, 1).unwrap();
        assert!(unicode[0].name.contains('é'));
        assert!(long[0].name.chars().count() > 260);
    }
}
