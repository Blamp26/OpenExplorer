//! Engine facade for immutable synthetic and local-directory snapshots.

use std::cmp::Ordering;
use std::fs;
use std::path::Path;
use std::sync::Arc;
use std::time::{SystemTime, UNIX_EPOCH};

use open_explorer_domain::{ExplorerError, ExplorerErrorCode, ExplorerItem, ExplorerItemKind};

pub const API_VERSION: u32 = 5;
pub const MAX_RANGE_COUNT: u32 = 4096;

pub const SORT_FIELD_NAME: u32 = 1;
pub const SORT_FIELD_DATE_MODIFIED: u32 = 2;
pub const SORT_FIELD_TYPE: u32 = 3;
pub const SORT_FIELD_SIZE: u32 = 4;
pub const SORT_DIRECTION_ASCENDING: u32 = 1;
pub const SORT_DIRECTION_DESCENDING: u32 = 2;
pub const SORT_FLAG_FOLDERS_FIRST: u32 = 1;

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
    data: Arc<SnapshotData>,
    order: Option<Arc<Vec<u64>>>,
    inverse_order: Option<Arc<Vec<u64>>>,
}

enum SnapshotData {
    Synthetic { item_count: u64 },
    Local { items: Vec<ExplorerItem> },
}

impl ExplorerSnapshot {
    pub fn synthetic(item_count: u64) -> Self {
        Self {
            data: Arc::new(SnapshotData::Synthetic { item_count }),
            order: None,
            inverse_order: None,
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
            data: Arc::new(SnapshotData::Local { items }),
            order: None,
            inverse_order: None,
        })
    }

    pub fn count(&self) -> u64 {
        match self.data.as_ref() {
            SnapshotData::Synthetic { item_count } => *item_count,
            SnapshotData::Local { items } => items.len() as u64,
        }
    }

    pub fn materialized_item_count(&self) -> usize {
        match self.data.as_ref() {
            SnapshotData::Synthetic { .. } => 0,
            SnapshotData::Local { items } => items.len(),
        }
    }

    pub fn create_sorted_view(
        &self,
        field: u32,
        direction: u32,
        flags: u32,
    ) -> Result<Self, ExplorerError> {
        if !matches!(
            field,
            SORT_FIELD_NAME | SORT_FIELD_DATE_MODIFIED | SORT_FIELD_TYPE | SORT_FIELD_SIZE
        ) || !matches!(
            direction,
            SORT_DIRECTION_ASCENDING | SORT_DIRECTION_DESCENDING
        ) || flags & !SORT_FLAG_FOLDERS_FIRST != 0
        {
            return Err(error(ExplorerErrorCode::InvalidArgument));
        }

        let count =
            usize::try_from(self.count()).map_err(|_| error(ExplorerErrorCode::Internal))?;
        if count > isize::MAX as usize {
            return Err(error(ExplorerErrorCode::Internal));
        }
        let mut order = Vec::with_capacity(count);
        for index in 0..count {
            order.push(u64::try_from(index).map_err(|_| error(ExplorerErrorCode::Internal))?);
        }
        order.sort_by(|left, right| {
            let left_item = self.item_at(*left).expect("validated snapshot index");
            let right_item = self.item_at(*right).expect("validated snapshot index");
            compare_items(&left_item, &right_item, field, direction, flags)
        });

        let mut inverse_order = vec![0_u64; count];
        for (logical_index, source_index) in order.iter().copied().enumerate() {
            inverse_order[usize::try_from(source_index).expect("snapshot index fits order")] =
                u64::try_from(logical_index).map_err(|_| error(ExplorerErrorCode::Internal))?;
        }

        Ok(Self {
            data: Arc::clone(&self.data),
            order: Some(Arc::new(order)),
            inverse_order: Some(Arc::new(inverse_order)),
        })
    }

    pub fn find_item_index(&self, item_id: u64) -> Option<u64> {
        let source_index = item_id.checked_sub(1)?;
        if source_index >= self.count() {
            return None;
        }
        self.inverse_order
            .as_ref()
            .map_or(Some(source_index), |inverse| {
                inverse.get(usize::try_from(source_index).ok()?).copied()
            })
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
        let mut result = Vec::with_capacity(
            usize::try_from(actual).map_err(|_| error(ExplorerErrorCode::Internal))?,
        );
        for offset in 0..actual {
            let logical_index = start
                .checked_add(offset)
                .ok_or_else(|| error(ExplorerErrorCode::OutOfRange))?;
            let source_index = self.order.as_ref().map_or(logical_index, |order| {
                order[usize::try_from(logical_index).expect("snapshot index fits order")]
            });
            result.push(self.item_at(source_index)?);
        }
        Ok(result)
    }

    fn item_at(&self, index: u64) -> Result<ExplorerItem, ExplorerError> {
        if index >= self.count() {
            return Err(error(ExplorerErrorCode::OutOfRange));
        }
        match self.data.as_ref() {
            SnapshotData::Synthetic { .. } => Ok(create_synthetic_item(index)),
            SnapshotData::Local { items } => items
                .get(usize::try_from(index).map_err(|_| error(ExplorerErrorCode::OutOfRange))?)
                .cloned()
                .ok_or_else(|| error(ExplorerErrorCode::OutOfRange)),
        }
    }
}

fn compare_items(
    left: &ExplorerItem,
    right: &ExplorerItem,
    field: u32,
    direction: u32,
    flags: u32,
) -> Ordering {
    let folders_first = flags & SORT_FLAG_FOLDERS_FIRST != 0;
    if folders_first && left.kind != right.kind {
        return if left.kind == ExplorerItemKind::Directory {
            Ordering::Less
        } else {
            Ordering::Greater
        };
    }

    let primary = match field {
        SORT_FIELD_NAME => lower_name(&left.name).cmp(&lower_name(&right.name)),
        SORT_FIELD_DATE_MODIFIED => left.modified_unix_ms.cmp(&right.modified_unix_ms),
        SORT_FIELD_TYPE => type_key(left).cmp(&type_key(right)),
        SORT_FIELD_SIZE => compare_size(left, right, folders_first, direction),
        _ => Ordering::Equal,
    };
    let primary = if field != SORT_FIELD_SIZE && direction == SORT_DIRECTION_DESCENDING {
        primary.reverse()
    } else {
        primary
    };
    if primary != Ordering::Equal {
        return primary;
    }

    lower_name(&left.name)
        .cmp(&lower_name(&right.name))
        .then_with(|| left.name.cmp(&right.name))
        .then_with(|| left.item_id.cmp(&right.item_id))
}

fn compare_size(
    left: &ExplorerItem,
    right: &ExplorerItem,
    folders_first: bool,
    direction: u32,
) -> Ordering {
    if folders_first
        && left.kind == ExplorerItemKind::Directory
        && right.kind == ExplorerItemKind::Directory
    {
        return Ordering::Equal;
    }
    match (left.size, right.size) {
        (Some(left), Some(right)) => {
            let ordering = left.cmp(&right);
            if direction == SORT_DIRECTION_DESCENDING {
                ordering.reverse()
            } else {
                ordering
            }
        }
        (Some(_), None) => Ordering::Less,
        (None, Some(_)) => Ordering::Greater,
        (None, None) => Ordering::Equal,
    }
}

fn lower_name(name: &str) -> String {
    name.chars().flat_map(char::to_lowercase).collect()
}

fn type_key(item: &ExplorerItem) -> String {
    if item.kind == ExplorerItemKind::Directory {
        return "directory".to_string();
    }
    let Some(dot) = item.name.rfind('.') else {
        return String::new();
    };
    if dot == 0 || dot + 1 >= item.name.len() {
        return String::new();
    }
    item.name[dot + 1..]
        .chars()
        .flat_map(char::to_lowercase)
        .collect()
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

    #[test]
    fn sorted_views_are_immutable_independent_and_deterministic() {
        let source = ExplorerSnapshot {
            data: Arc::new(SnapshotData::Local {
                items: vec![
                    test_item(1, "zeta.txt", 30, Some(30), ExplorerItemKind::File),
                    test_item(2, "Alpha.txt", 10, Some(10), ExplorerItemKind::File),
                    test_item(3, "Bravo", 20, None, ExplorerItemKind::File),
                    test_item(4, "Folder", 5, None, ExplorerItemKind::Directory),
                ],
            }),
            order: None,
            inverse_order: None,
        };
        let name_ascending = source
            .create_sorted_view(
                SORT_FIELD_NAME,
                SORT_DIRECTION_ASCENDING,
                SORT_FLAG_FOLDERS_FIRST,
            )
            .unwrap();
        let name_descending = source
            .create_sorted_view(
                SORT_FIELD_NAME,
                SORT_DIRECTION_DESCENDING,
                SORT_FLAG_FOLDERS_FIRST,
            )
            .unwrap();
        assert_eq!(
            names(&name_ascending),
            ["Folder", "Alpha.txt", "Bravo", "zeta.txt"]
        );
        assert_eq!(
            names(&name_descending),
            ["Folder", "zeta.txt", "Bravo", "Alpha.txt"]
        );
        assert_eq!(names(&source), ["zeta.txt", "Alpha.txt", "Bravo", "Folder"]);

        let size_descending = source
            .create_sorted_view(SORT_FIELD_SIZE, SORT_DIRECTION_DESCENDING, 0)
            .unwrap();
        assert_eq!(
            names(&size_descending),
            ["zeta.txt", "Alpha.txt", "Bravo", "Folder"]
        );
        let size_ascending = source
            .create_sorted_view(SORT_FIELD_SIZE, SORT_DIRECTION_ASCENDING, 0)
            .unwrap();
        assert_eq!(
            names(&size_ascending),
            ["Alpha.txt", "zeta.txt", "Bravo", "Folder"]
        );

        let view_after_source = source
            .create_sorted_view(SORT_FIELD_TYPE, SORT_DIRECTION_ASCENDING, 0)
            .unwrap();
        drop(source);
        assert_eq!(view_after_source.count(), 4);
        assert_eq!(
            names(&view_after_source),
            ["Bravo", "Folder", "Alpha.txt", "zeta.txt"]
        );
    }

    fn test_item(
        id: u64,
        name: &str,
        modified: i64,
        size: Option<u64>,
        kind: ExplorerItemKind,
    ) -> ExplorerItem {
        ExplorerItem {
            item_id: id,
            name: name.to_string(),
            modified_unix_ms: modified,
            size,
            kind,
            name_was_lossy: false,
        }
    }

    fn names(snapshot: &ExplorerSnapshot) -> Vec<String> {
        snapshot
            .get_range(0, 4096)
            .unwrap()
            .into_iter()
            .map(|item| item.name)
            .collect()
    }
}
