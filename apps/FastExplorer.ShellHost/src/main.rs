use fast_explorer_protocol::PROTOCOL_VERSION;

fn main() {
    if std::env::args().nth(1).as_deref() == Some("--version") {
        println!("FastExplorer ShellHost protocol version {PROTOCOL_VERSION}");
    }
}
