namespace OpenExplorer.Interop;

internal enum NativeStatus : uint
{
    Ok = 0,
    NullPointer = 1,
    InvalidArgument = 2,
    OutOfRange = 3,
    BufferTooSmall = 4,
    InternalError = 5,
    Panic = 6,
    NotFound = 7,
    AccessDenied = 8,
    NotDirectory = 9,
    InvalidUtf8 = 10,
    IoError = 11,
}

internal static class NativeStatusExtensions
{
    public static void ThrowIfFailed(this uint value, string operation)
    {
        if (value == (uint)NativeStatus.Ok)
        {
            return;
        }

        string name = value switch
        {
            (uint)NativeStatus.NullPointer => "NULL_POINTER",
            (uint)NativeStatus.InvalidArgument => "INVALID_ARGUMENT",
            (uint)NativeStatus.OutOfRange => "OUT_OF_RANGE",
            (uint)NativeStatus.BufferTooSmall => "BUFFER_TOO_SMALL",
            (uint)NativeStatus.InternalError => "INTERNAL_ERROR",
            (uint)NativeStatus.Panic => "PANIC",
            (uint)NativeStatus.NotFound => "NOT_FOUND",
            (uint)NativeStatus.AccessDenied => "ACCESS_DENIED",
            (uint)NativeStatus.NotDirectory => "NOT_DIRECTORY",
            (uint)NativeStatus.InvalidUtf8 => "INVALID_UTF8",
            (uint)NativeStatus.IoError => "IO_ERROR",
            _ => "UNKNOWN",
        };
        throw new NativeInteropException($"{operation} failed with native status {value} ({name}).");
    }
}
