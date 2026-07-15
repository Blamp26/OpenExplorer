# Local file provider v1

The local provider opens one absolute local directory and creates an immutable snapshot of its immediate children. It enumerates once at snapshot creation, does not recurse, does not watch for changes, and does not retain an open iterator. The navigation controller can replace this snapshot when a directory is activated or when Back, Forward, or Up succeeds. The UI consumes each snapshot through the existing paged range ABI and bounded managed page cache.

Enumeration order is the operating system provider order. No sorting, filtering, navigation, activation, or Shell metadata lookup is performed. Files and directories are included, including hidden and extensionless entries. Regular files expose their byte size, including zero; directories have no size. Modification times are transferred as Unix milliseconds.

Valid Unicode filenames are preserved. Names that cannot be represented as valid Unicode are converted lossily with replacement characters and marked by the ABI flag; entries are not discarded and no per-item warning dialog is shown. Item IDs are non-zero, unique, sequential, and stable only for the lifetime of one snapshot.

Failure to read required metadata fails the complete snapshot. There is no partial-success policy. Future work includes navigation, refresh, watchers, sorting, Shell descriptions, and richer providers.
