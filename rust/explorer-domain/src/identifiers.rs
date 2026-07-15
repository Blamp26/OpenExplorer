//! Stable identifiers used by future engine APIs.

macro_rules! identifier {
    ($name:ident) => {
        #[derive(Debug, Clone, Copy, PartialEq, Eq, Hash)]
        #[repr(transparent)]
        pub struct $name(pub u64);
    };
}

identifier!(ItemId);
identifier!(LocationId);
identifier!(SnapshotId);
identifier!(OperationId);
identifier!(SearchSessionId);
