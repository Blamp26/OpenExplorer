//! A deliberately small, crash-isolated Windows Shell icon worker.
use open_explorer_protocol::{
    decode_frame, frame, IconResponse, IconStatus, Message, PROTOCOL_VERSION,
};
use std::io::{Read, Write};

fn main() {
    if std::env::args().nth(1).as_deref() == Some("--version") {
        println!("OpenExplorer ShellHost protocol version {PROTOCOL_VERSION}");
        return;
    }
    #[cfg(windows)]
    {
        if let Err(e) = windows_host::run(std::env::args().skip(1).collect()) {
            eprintln!("ShellHost stopped: {e}");
            std::process::exit(1);
        }
    }
    #[cfg(not(windows))]
    {
        eprintln!("ShellHost is only available on Windows");
        std::process::exit(1);
    }
}

fn serve<S: Read + Write>(mut stream: S) -> std::io::Result<()> {
    loop {
        let mut length = [0; 4];
        if stream.read_exact(&mut length).is_err() {
            return Ok(());
        }
        let n = u32::from_le_bytes(length) as usize;
        if n == 0 || n > open_explorer_protocol::MAX_FRAME_SIZE {
            return Ok(());
        }
        let mut body = vec![0; n];
        stream.read_exact(&mut body)?;
        let mut bytes = length.to_vec();
        bytes.extend_from_slice(&body);
        let response = match decode_frame(&bytes) {
            Ok(Message::IconBatchRequest(items)) => Message::IconBatchResponse(
                items
                    .into_iter()
                    .map(|request| {
                        #[cfg(windows)]
                        {
                            windows_host::icon_for(&request)
                        }
                        #[cfg(not(windows))]
                        {
                            IconResponse {
                                request_id: request.request_id,
                                status: IconStatus::Unsupported,
                                pixels_bgra: Vec::new(),
                            }
                        }
                    })
                    .collect(),
            ),
            Ok(Message::Shutdown) => return Ok(()),
            Ok(Message::IconBatchResponse(_)) | Err(_) => return Ok(()),
        };
        let output = frame(&response).map_err(|_| {
            std::io::Error::new(std::io::ErrorKind::InvalidData, "response too large")
        })?;
        stream.write_all(&output)?;
        stream.flush()?;
    }
}

#[cfg(windows)]
mod windows_host {
    use super::*;
    use std::collections::{HashMap, VecDeque};
    use std::{
        ffi::OsStr,
        io,
        os::windows::ffi::OsStrExt,
        os::windows::io::{FromRawHandle, RawHandle},
    };
    use windows_sys::Win32::Graphics::Gdi::{
        CreateCompatibleDC, CreateDIBSection, DeleteDC, DeleteObject, SelectObject, BITMAPINFO,
        BITMAPINFOHEADER, DIB_RGB_COLORS,
    };
    use windows_sys::Win32::UI::WindowsAndMessaging::{DestroyIcon, HICON};
    use windows_sys::Win32::UI::WindowsAndMessaging::{DrawIconEx, DI_NORMAL};
    use windows_sys::Win32::{
        Foundation::{CloseHandle, ERROR_PIPE_CONNECTED, HANDLE, INVALID_HANDLE_VALUE},
        Storage::FileSystem::PIPE_ACCESS_DUPLEX,
        System::Pipes::{
            ConnectNamedPipe, CreateNamedPipeW, DisconnectNamedPipe, PIPE_READMODE_BYTE,
            PIPE_TYPE_BYTE, PIPE_WAIT,
        },
        UI::Shell::{SHGetFileInfoW, SHFILEINFOW, SHGFI_ICON},
    };

    const PIPE_PREFIX: &str = r"\\.\pipe\openexplorer-shell-";
    const ICON_CACHE_CAPACITY: usize = 256;
    type IconCacheState = (HashMap<String, Vec<u8>>, VecDeque<String>);
    type IconCache = std::sync::Mutex<IconCacheState>;
    static ICON_CACHE: std::sync::OnceLock<IconCache> = std::sync::OnceLock::new();
    pub fn run(args: Vec<String>) -> io::Result<()> {
        let name = args
            .windows(2)
            .find(|x| x[0] == "--pipe")
            .map(|x| x[1].clone())
            .unwrap_or_else(|| "default".into());
        if name.len() > 100 || name.contains(['\\', '/', ':']) {
            return Err(io::Error::new(
                io::ErrorKind::InvalidInput,
                "invalid pipe name",
            ));
        }
        let wide: Vec<u16> = OsStr::new(&format!("{PIPE_PREFIX}{name}"))
            .encode_wide()
            .chain(Some(0))
            .collect();
        let mut failures = 0u32;
        loop {
            let handle = unsafe {
                CreateNamedPipeW(
                    wide.as_ptr(),
                    PIPE_ACCESS_DUPLEX,
                    PIPE_TYPE_BYTE | PIPE_READMODE_BYTE | PIPE_WAIT,
                    1,
                    1024 * 1024,
                    1024 * 1024,
                    5000,
                    std::ptr::null(),
                )
            };
            if handle == INVALID_HANDLE_VALUE {
                failures += 1;
                if failures >= 3 {
                    return Err(io::Error::last_os_error());
                }
                std::thread::sleep(std::time::Duration::from_millis(50 * (1 << (failures - 1))));
                continue;
            }
            failures = 0;
            let connected = unsafe { ConnectNamedPipe(handle, std::ptr::null_mut()) } != 0
                || io::Error::last_os_error().raw_os_error() == Some(ERROR_PIPE_CONNECTED as i32);
            if connected {
                let mut file = unsafe { std::fs::File::from_raw_handle(handle as RawHandle) };
                let _ = serve(&mut file);
                unsafe {
                    DisconnectNamedPipe(handle as HANDLE);
                }
                drop(file);
            } else {
                unsafe {
                    CloseHandle(handle);
                }
            }
        }
    }
    pub fn icon_for(request: &open_explorer_protocol::IconRequest) -> IconResponse {
        let key = cache_key(request);
        let cache =
            ICON_CACHE.get_or_init(|| std::sync::Mutex::new((HashMap::new(), VecDeque::new())));
        if let Ok(cache) = cache.lock() {
            if let Some(pixels) = cache.0.get(&key) {
                return IconResponse {
                    request_id: request.request_id,
                    status: IconStatus::Ok,
                    pixels_bgra: pixels.clone(),
                };
            }
        }
        let mut path: Vec<u16> = OsStr::new(&request.path)
            .encode_wide()
            .chain(Some(0))
            .collect();
        let mut info: SHFILEINFOW = unsafe { std::mem::zeroed() };
        let ok = unsafe {
            SHGetFileInfoW(
                path.as_mut_ptr(),
                request.attributes,
                &mut info,
                std::mem::size_of::<SHFILEINFOW>() as u32,
                SHGFI_ICON,
            )
        } != 0;
        if !ok {
            return IconResponse {
                request_id: request.request_id,
                status: IconStatus::Missing,
                pixels_bgra: Vec::new(),
            };
        }
        let icon: HICON = info.hIcon;
        let pixels = unsafe { icon_pixels(icon) };
        unsafe {
            DestroyIcon(icon);
        }
        // Conversion is intentionally bounded at the ShellHost boundary. The UI receives either
        // a 32px BGRA payload or a placeholder; no HICON crosses the process boundary.
        if !pixels.is_empty() {
            if let Ok(mut cache) = cache.lock() {
                if !cache.0.contains_key(&key) {
                    cache.0.insert(key.clone(), pixels.clone());
                    cache.1.push_back(key);
                }
                while cache.0.len() > ICON_CACHE_CAPACITY {
                    if let Some(old) = cache.1.pop_front() {
                        cache.0.remove(&old);
                    } else {
                        break;
                    }
                }
            }
        }
        IconResponse {
            request_id: request.request_id,
            status: if pixels.is_empty() {
                IconStatus::Failed
            } else {
                IconStatus::Ok
            },
            pixels_bgra: pixels,
        }
    }

    fn cache_key(request: &open_explorer_protocol::IconRequest) -> String {
        if request.attributes & 0x10 != 0 {
            return format!("dir:{}", request.attributes);
        }
        let path = request.path.replace('/', "\\").to_ascii_uppercase();
        let extension = std::path::Path::new(&path)
            .extension()
            .and_then(|x| x.to_str())
            .unwrap_or("");
        if matches!(extension, "EXE" | "DLL" | "ICO") {
            format!("path:{path}|{}", request.attributes)
        } else {
            format!("file:{extension}|{}", request.attributes)
        }
    }

    unsafe fn icon_pixels(icon: HICON) -> Vec<u8> {
        let info = BITMAPINFO {
            bmiHeader: BITMAPINFOHEADER {
                biSize: std::mem::size_of::<BITMAPINFOHEADER>() as u32,
                biWidth: 32,
                biHeight: -32,
                biPlanes: 1,
                biBitCount: 32,
                biCompression: 0,
                biSizeImage: 0,
                biXPelsPerMeter: 0,
                biYPelsPerMeter: 0,
                biClrUsed: 0,
                biClrImportant: 0,
            },
            bmiColors: [std::mem::zeroed()],
        };
        let dc = CreateCompatibleDC(std::ptr::null_mut());
        if dc.is_null() {
            return Vec::new();
        }
        let mut pixels_ptr: *mut std::ffi::c_void = std::ptr::null_mut();
        let bitmap = CreateDIBSection(
            dc,
            &info,
            DIB_RGB_COLORS,
            &mut pixels_ptr,
            std::ptr::null_mut(),
            0,
        );
        if bitmap.is_null() || pixels_ptr.is_null() {
            DeleteDC(dc);
            return Vec::new();
        }
        let previous = SelectObject(dc, bitmap as _);
        let drawn = DrawIconEx(dc, 0, 0, icon, 32, 32, 0, std::ptr::null_mut(), DI_NORMAL);
        let pixels = if drawn != 0 {
            std::slice::from_raw_parts(pixels_ptr.cast::<u8>(), 32 * 32 * 4).to_vec()
        } else {
            Vec::new()
        };
        SelectObject(dc, previous);
        DeleteObject(bitmap as _);
        DeleteDC(dc);
        pixels
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn malformed_request_disconnects() {
        let mut x = std::io::Cursor::new(vec![1, 0, 0, 0, 0]);
        assert!(serve(&mut x).is_ok());
    }
}
