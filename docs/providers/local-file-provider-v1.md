# Local file provider v1

The local provider opens one absolute local directory and creates an immutable snapshot of its immediate children. It enumerates once at snapshot creation, does not recurse, does not watch for changes, and does not retain an open iterator. The navigation controller retains the base snapshot and creates native sorted views over it. A sort transition does not reopen or re-enumerate the directory. The controller replaces both snapshots when a directory is activated or when Back, Forward, or Up succeeds. The UI consumes each view through the existing paged range ABI and bounded managed page cache.

The base enumeration order is the operating system provider order. Sorting is a separate Rust snapshot-view operation; filtering, navigation, activation, and Shell metadata lookup are not performed by the provider. Files and directories are included, including hidden and extensionless entries. Regular files expose their byte size, including zero; directories have no size. Modification times are transferred as Unix milliseconds.

Valid Unicode filenames are preserved. Names that cannot be represented as valid Unicode are converted lossily with replacement characters and marked by the ABI flag; entries are not discarded and no per-item warning dialog is shown. Item IDs are non-zero, unique, sequential, and stable only for the lifetime of one snapshot.

Failure to read required metadata fails the complete snapshot. There is no partial-success policy. Future work includes refresh, watchers, filtering, Shell descriptions, and richer providers.
