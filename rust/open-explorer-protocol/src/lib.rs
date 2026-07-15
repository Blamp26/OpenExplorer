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
    }
    if b.len() > MAX_FRAME_SIZE {
        return Err(ProtocolError::Oversized);
    }
    Ok(b)
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
}
