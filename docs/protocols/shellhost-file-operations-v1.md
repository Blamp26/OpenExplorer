# ShellHost file-operations protocol v1

Mutation frames use the existing little-endian four-byte length prefix and protocol version 1. Type 4 is a bounded mutation request and type 5 is a bounded mutation response. Requests contain one operation, an accepted location, zero to 64 target records, and an optional bounded destination name. Responses contain a status (`Succeeded`, `Cancelled`, `Failed`, or `Partial`) and bounded structured failures. The protocol rejects unsupported operations, invalid UTF-8, oversized frames or paths, malformed lengths, and invalid operation shapes before allocation or execution.

Only the ShellHost owns Windows Shell calls, native handles, and mutation resources. Managed infrastructure owns pipe lifecycle and exposes `IExplorerFileOperationProvider`; Application and UI see only contracts and operation state. Disconnects, crashes, timeouts, and malformed responses fail the batch safely and are eligible for one bounded restart.
