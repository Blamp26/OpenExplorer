# ShellHost icon protocol v1

ShellHost uses a local Named Pipe named `openexplorer-shell-{instance}`. Each frame is a little-endian four-byte payload length followed by a bounded payload. The payload contains protocol version `1`, a message type, a batch count capped at 64, and request or response records. Paths are capped at 32 KiB; icon pixels are capped at 32×32 BGRA bytes.

The Rust protocol crate validates version, message type, batch count, lengths, UTF-8 paths, response identities, and trailing bytes before allocating. ShellHost extracts standard Windows association/folder icons in its own process, renders them to a 32px DIB, copies bounded BGRA pixels into the response, and releases HICON/GDI resources before replying. The managed infrastructure consumes the payload at the process boundary and exposes only icon presentation state to Application/UI.

The client starts ShellHost lazily, uses asynchronous batched requests, applies a one-restart bounded retry policy, and falls back to generic file/folder placeholders on failure. No Shell extension, process handle, pipe, native handle, or raw image buffer crosses into the UI.
