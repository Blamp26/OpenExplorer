//! The bounded, versioned wire contract used by the isolated ShellHost.
//!
//! Frames are little-endian: a four byte payload length followed by a payload.
//! Payloads are deliberately small and contain no paths in responses.

use std::fmt;

pub const PROTOCOL_VERSION: u32 = 1;
pub const MAX_FRAME_SIZE: usize = 1024 * 1024;
pub const MAX_BATCH_ITEMS: usize = 64;
pub const MAX_PATH_BYTES: usize = 32 * 1024;
pub const ICON_EDGE: u16 = 32;
pub const MAX_NAME_BYTES: usize = 255;

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct IconRequest {
    pub request_id: u64,
    pub path: String,
    pub attributes: u32,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct IconResponse {
    pub request_id: u64,
    pub status: IconStatus,
    pub pixels_bgra: Vec<u8>,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct MutationItem {
    pub item_id: u64,
    pub path: String,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct MutationRequest {
    pub request_id: u64,
    pub operation: MutationOperation,
    pub location: String,
    pub items: Vec<MutationItem>,
    pub desired_name: Option<String>,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct MutationFailure {
    pub item_id: Option<u64>,
    pub name: Option<String>,
    pub message: String,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct MutationResponse {
    pub request_id: u64,
    pub status: MutationStatus,
    pub created_name: Option<String>,
    pub created_item_id: Option<u64>,
    pub failures: Vec<MutationFailure>,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
#[repr(u8)]
pub enum MutationOperation {
    Rename = 1,
    CreateFolder = 2,
    RecycleBinDelete = 3,
}

impl TryFrom<u8> for MutationOperation {
    type Error = ProtocolError;
    fn try_from(value: u8) -> Result<Self, Self::Error> {
        match value {
            1 => Ok(Self::Rename),
            2 => Ok(Self::CreateFolder),
            3 => Ok(Self::RecycleBinDelete),
            _ => Err(ProtocolError::Malformed),
        }
    }
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
#[repr(u8)]
pub enum MutationStatus {
    Succeeded = 0,
    Cancelled = 1,
    Failed = 2,
    Partial = 3,
}

impl TryFrom<u8> for MutationStatus {
    type Error = ProtocolError;
    fn try_from(value: u8) -> Result<Self, Self::Error> {
        match value {
            0 => Ok(Self::Succeeded),
            1 => Ok(Self::Cancelled),
            2 => Ok(Self::Failed),
            3 => Ok(Self::Partial),
            _ => Err(ProtocolError::Malformed),
        }
    }
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
#[repr(u8)]
pub enum IconStatus {
    Ok = 0,
    Missing = 1,
    Unsupported = 2,
    Failed = 3,
}

impl TryFrom<u8> for IconStatus {
    type Error = ProtocolError;
    fn try_from(value: u8) -> Result<Self, Self::Error> {
        match value {
            0 => Ok(Self::Ok),
            1 => Ok(Self::Missing),
            2 => Ok(Self::Unsupported),
            3 => Ok(Self::Failed),
            _ => Err(ProtocolError::Malformed),
        }
    }
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub enum Message {
    IconBatchRequest(Vec<IconRequest>),
    IconBatchResponse(Vec<IconResponse>),
    Shutdown,
    MutationRequest(MutationRequest),
    MutationResponse(MutationResponse),
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum ProtocolError {
    Truncated,
    Oversized,
    InvalidLength,
    InvalidVersion,
    InvalidType,
    Malformed,
    TooManyItems,
    InvalidPath,
}
impl fmt::Display for ProtocolError {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        write!(f, "invalid ShellHost protocol message: {self:?}")
    }
}
impl std::error::Error for ProtocolError {}

pub fn frame(message: &Message) -> Result<Vec<u8>, ProtocolError> {
    let body = encode(message)?;
    if body.len() > MAX_FRAME_SIZE {
        return Err(ProtocolError::Oversized);
    }
    let mut out = Vec::with_capacity(body.len() + 4);
    out.extend_from_slice(&(body.len() as u32).to_le_bytes());
    out.extend_from_slice(&body);
    Ok(out)
}

pub fn decode_frame(frame: &[u8]) -> Result<Message, ProtocolError> {
    if frame.len() < 4 {
        return Err(ProtocolError::Truncated);
    }
    let length = u32::from_le_bytes(frame[..4].try_into().unwrap()) as usize;
    if length > MAX_FRAME_SIZE {
        return Err(ProtocolError::Oversized);
    }
    if length != frame.len() - 4 {
        return Err(ProtocolError::InvalidLength);
    }
    decode(&frame[4..])
}

fn encode(message: &Message) -> Result<Vec<u8>, ProtocolError> {
    let (kind, items) = match message {
        Message::IconBatchRequest(v) => (1, v.len()),
        Message::IconBatchResponse(v) => (2, v.len()),
        Message::Shutdown => (3, 0),
        Message::MutationRequest(x) => (4, x.items.len()),
        Message::MutationResponse(x) => (5, x.failures.len()),
    };
    if items > MAX_BATCH_ITEMS {
        return Err(ProtocolError::TooManyItems);
    }
    let mut b = Vec::new();
    b.extend_from_slice(&(PROTOCOL_VERSION as u16).to_le_bytes());
    b.push(kind);
    b.extend_from_slice(&(items as u16).to_le_bytes());
    match message {
        Message::IconBatchRequest(v) => {
            for x in v {
                let p = x.path.as_bytes();
                if p.is_empty() || p.len() > MAX_PATH_BYTES {
                    return Err(ProtocolError::InvalidPath);
                }
                b.extend_from_slice(&x.request_id.to_le_bytes());
                b.extend_from_slice(&x.attributes.to_le_bytes());
                b.extend_from_slice(&(p.len() as u32).to_le_bytes());
                b.extend_from_slice(p);
            }
        }
        Message::IconBatchResponse(v) => {
            for x in v {
                if x.pixels_bgra.len() > (ICON_EDGE as usize * ICON_EDGE as usize * 4) {
                    return Err(ProtocolError::Oversized);
                }
                b.extend_from_slice(&x.request_id.to_le_bytes());
                b.push(x.status as u8);
                b.extend_from_slice(&(x.pixels_bgra.len() as u32).to_le_bytes());
                b.extend_from_slice(&x.pixels_bgra);
            }
        }
        Message::Shutdown => {}
        Message::MutationRequest(x) => {
            b.extend_from_slice(&x.request_id.to_le_bytes());
            b.push(x.operation as u8);
            let location = x.location.as_bytes();
            if location.is_empty() || location.len() > MAX_PATH_BYTES {
                return Err(ProtocolError::InvalidPath);
            }
            b.extend_from_slice(&(location.len() as u32).to_le_bytes());
            b.extend_from_slice(location);
            encode_optional_name(&mut b, x.desired_name.as_deref())?;
            for item in &x.items {
                let p = item.path.as_bytes();
                if p.is_empty() || p.len() > MAX_PATH_BYTES {
                    return Err(ProtocolError::InvalidPath);
                }
                b.extend_from_slice(&item.item_id.to_le_bytes());
                b.extend_from_slice(&(p.len() as u32).to_le_bytes());
                b.extend_from_slice(p);
            }
        }
        Message::MutationResponse(x) => {
            b.extend_from_slice(&x.request_id.to_le_bytes());
            b.push(x.status as u8);
            encode_optional_name(&mut b, x.created_name.as_deref())?;
            b.push(u8::from(x.created_item_id.is_some()));
            if let Some(item_id) = x.created_item_id {
                b.extend_from_slice(&item_id.to_le_bytes());
            }
            for failure in &x.failures {
                b.push(u8::from(failure.item_id.is_some()));
                if let Some(id) = failure.item_id {
                    b.extend_from_slice(&id.to_le_bytes());
                }
                encode_optional_name(&mut b, failure.name.as_deref())?;
                let msg = failure.message.as_bytes();
                if msg.is_empty() || msg.len() > MAX_NAME_BYTES {
                    return Err(ProtocolError::Malformed);
                }
                b.extend_from_slice(&(msg.len() as u16).to_le_bytes());
                b.extend_from_slice(msg);
            }
        }
    }
    if b.len() > MAX_FRAME_SIZE {
        return Err(ProtocolError::Oversized);
    }
    Ok(b)
}

fn encode_optional_name(b: &mut Vec<u8>, value: Option<&str>) -> Result<(), ProtocolError> {
    let bytes = value.unwrap_or("").as_bytes();
    if bytes.len() > MAX_NAME_BYTES {
        return Err(ProtocolError::Malformed);
    }
    b.extend_from_slice(&(bytes.len() as u16).to_le_bytes());
    b.extend_from_slice(bytes);
    Ok(())
}

fn take<'a>(b: &'a [u8], at: &mut usize, n: usize) -> Result<&'a [u8], ProtocolError> {
    if n > b.len().saturating_sub(*at) {
        return Err(ProtocolError::Truncated);
    }
    let x = &b[*at..*at + n];
    *at += n;
    Ok(x)
}
fn u16v(b: &[u8], a: &mut usize) -> Result<u16, ProtocolError> {
    Ok(u16::from_le_bytes(take(b, a, 2)?.try_into().unwrap()))
}
fn u32v(b: &[u8], a: &mut usize) -> Result<u32, ProtocolError> {
    Ok(u32::from_le_bytes(take(b, a, 4)?.try_into().unwrap()))
}
fn u64v(b: &[u8], a: &mut usize) -> Result<u64, ProtocolError> {
    Ok(u64::from_le_bytes(take(b, a, 8)?.try_into().unwrap()))
}
fn text16(b: &[u8], a: &mut usize) -> Result<Option<String>, ProtocolError> {
    let n = u16v(b, a)? as usize;
    if n > MAX_NAME_BYTES {
        return Err(ProtocolError::Malformed);
    }
    if n == 0 {
        return Ok(None);
    }
    Ok(Some(
        String::from_utf8(take(b, a, n)?.to_vec()).map_err(|_| ProtocolError::Malformed)?,
    ))
}
fn decode(b: &[u8]) -> Result<Message, ProtocolError> {
    if b.len() < 5 {
        return Err(ProtocolError::Truncated);
    }
    let mut a = 0;
    if u16v(b, &mut a)? as u32 != PROTOCOL_VERSION {
        return Err(ProtocolError::InvalidVersion);
    }
    let kind = take(b, &mut a, 1)?[0];
    let count = u16v(b, &mut a)? as usize;
    if count > MAX_BATCH_ITEMS {
        return Err(ProtocolError::TooManyItems);
    }
    let m = match kind {
        1 => {
            let mut v = Vec::with_capacity(count);
            for _ in 0..count {
                let id = u64v(b, &mut a)?;
                let attributes = u32v(b, &mut a)?;
                let n = u32v(b, &mut a)? as usize;
                if n == 0 || n > MAX_PATH_BYTES {
                    return Err(ProtocolError::InvalidPath);
                }
                let p = take(b, &mut a, n)?;
                v.push(IconRequest {
                    request_id: id,
                    attributes,
                    path: String::from_utf8(p.to_vec()).map_err(|_| ProtocolError::InvalidPath)?,
                });
            }
            Message::IconBatchRequest(v)
        }
        2 => {
            let mut v = Vec::with_capacity(count);
            for _ in 0..count {
                let id = u64v(b, &mut a)?;
                let status = IconStatus::try_from(take(b, &mut a, 1)?[0])?;
                let n = u32v(b, &mut a)? as usize;
                if n > (ICON_EDGE as usize * ICON_EDGE as usize * 4) {
                    return Err(ProtocolError::Oversized);
                }
                v.push(IconResponse {
                    request_id: id,
                    status,
                    pixels_bgra: take(b, &mut a, n)?.to_vec(),
                });
            }
            Message::IconBatchResponse(v)
        }
        3 if count == 0 => Message::Shutdown,
        4 => {
            let request_id = u64v(b, &mut a)?;
            let operation = MutationOperation::try_from(take(b, &mut a, 1)?[0])?;
            let location_length = u32v(b, &mut a)? as usize;
            if location_length == 0 || location_length > MAX_PATH_BYTES {
                return Err(ProtocolError::InvalidPath);
            }
            let location = String::from_utf8(take(b, &mut a, location_length)?.to_vec())
                .map_err(|_| ProtocolError::InvalidPath)?;
            let desired_name = text16(b, &mut a)?;
            let mut items = Vec::with_capacity(count);
            for _ in 0..count {
                let item_id = u64v(b, &mut a)?;
                let n = u32v(b, &mut a)? as usize;
                if n == 0 || n > MAX_PATH_BYTES {
                    return Err(ProtocolError::InvalidPath);
                }
                let path = String::from_utf8(take(b, &mut a, n)?.to_vec())
                    .map_err(|_| ProtocolError::InvalidPath)?;
                items.push(MutationItem { item_id, path });
            }
            if matches!(operation, MutationOperation::Rename)
                && (count != 1 || desired_name.is_none())
            {
                return Err(ProtocolError::Malformed);
            }
            if matches!(operation, MutationOperation::CreateFolder) && count != 0 {
                return Err(ProtocolError::Malformed);
            }
            if matches!(operation, MutationOperation::RecycleBinDelete) && count == 0 {
                return Err(ProtocolError::Malformed);
            }
            Message::MutationRequest(MutationRequest {
                request_id,
                operation,
                location,
                items,
                desired_name,
            })
        }
        5 => {
            let request_id = u64v(b, &mut a)?;
            let status = MutationStatus::try_from(take(b, &mut a, 1)?[0])?;
            let created_name = text16(b, &mut a)?;
            let created_item_id = if take(b, &mut a, 1)?[0] == 0 {
                None
            } else {
                Some(u64v(b, &mut a)?)
            };
            let mut failures = Vec::with_capacity(count);
            for _ in 0..count {
                let item_id = if take(b, &mut a, 1)?[0] == 0 {
                    None
                } else {
                    Some(u64v(b, &mut a)?)
                };
                let name = text16(b, &mut a)?;
                let n = u16v(b, &mut a)? as usize;
                if n == 0 || n > MAX_NAME_BYTES {
                    return Err(ProtocolError::Malformed);
                }
                let message = String::from_utf8(take(b, &mut a, n)?.to_vec())
                    .map_err(|_| ProtocolError::Malformed)?;
                failures.push(MutationFailure {
                    item_id,
                    name,
                    message,
                });
            }
            Message::MutationResponse(MutationResponse {
                request_id,
                status,
                created_name,
                created_item_id,
                failures,
            })
        }
        _ => return Err(ProtocolError::InvalidType),
    };
    if a != b.len() {
        return Err(ProtocolError::Malformed);
    }
    Ok(m)
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn round_trip_batch() {
        let m = Message::IconBatchRequest(vec![IconRequest {
            request_id: 7,
            path: String::from(r"C:\a.txt"),
            attributes: 2,
        }]);
        assert_eq!(decode_frame(&frame(&m).unwrap()).unwrap(), m);
    }
    #[test]
    fn rejects_bad_lengths_and_versions() {
        let mut x = frame(&Message::Shutdown).unwrap();
        x[0] = 99;
        assert_eq!(decode_frame(&x), Err(ProtocolError::InvalidLength));
        let mut x = frame(&Message::Shutdown).unwrap();
        x[4] = 2;
        assert_eq!(decode_frame(&x), Err(ProtocolError::InvalidVersion));
    }
    #[test]
    fn bounds_batches_and_paths() {
        assert!(frame(&Message::IconBatchRequest(
            (0..65)
                .map(|i| IconRequest {
                    request_id: i,
                    path: "x".into(),
                    attributes: 0
                })
                .collect()
        ))
        .is_err());
    }

    #[test]
    fn mutation_round_trip_is_bounded_and_typed() {
        let m = Message::MutationRequest(MutationRequest {
            request_id: 12,
            operation: MutationOperation::Rename,
            location: r"C:\".into(),
            items: vec![MutationItem {
                item_id: 88,
                path: r"C:\old.txt".into(),
            }],
            desired_name: Some("new.txt".into()),
        });
        assert_eq!(decode_frame(&frame(&m).unwrap()).unwrap(), m);
        let response = Message::MutationResponse(MutationResponse {
            request_id: 12,
            status: MutationStatus::Partial,
            created_name: None,
            created_item_id: None,
            failures: vec![MutationFailure {
                item_id: Some(88),
                name: Some("old.txt".into()),
                message: "denied".into(),
            }],
        });
        assert_eq!(decode_frame(&frame(&response).unwrap()).unwrap(), response);
    }

    #[test]
    fn mutation_rejects_invalid_shape_and_text() {
        let request = Message::MutationRequest(MutationRequest {
            request_id: 1,
            operation: MutationOperation::Rename,
            location: r"C:\".into(),
            items: vec![],
            desired_name: Some("x".into()),
        });
        let encoded = frame(&request).unwrap();
        assert_eq!(decode_frame(&encoded), Err(ProtocolError::Malformed));
        let response = Message::MutationResponse(MutationResponse {
            request_id: 1,
            status: MutationStatus::Failed,
            created_name: None,
            created_item_id: None,
            failures: vec![MutationFailure {
                item_id: None,
                name: None,
                message: "".into(),
            }],
        });
        assert_eq!(frame(&response), Err(ProtocolError::Malformed));
    }
}
