namespace OpenExplorer.Interop;

public sealed class NativeInteropException : Exception
{
    public NativeInteropException(string message) : base(message)
    {
    }

    public NativeInteropException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
