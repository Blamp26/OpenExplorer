# 0004 Use provider-based locations

## Status
Accepted

## Context
File, Shell, archive, SFTP, and MTP locations do not share filesystem semantics.

## Decision
Represent locations by provider scheme and opaque identity rather than assuming a normal path.

## Consequences
Providers can evolve independently and domain contracts remain platform-neutral.

## Rejected alternatives
Making every location a `PathBuf` or Windows path.
