namespace FastExplorer.Interop;

public sealed class NativeInteropException : Exception
{
    public NativeInteropException(string message) : base(message)
    {
    }
}
