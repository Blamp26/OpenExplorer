using OpenExplorer.Interop;

try
{
    using var engine = new NativeExplorerEngine();
    if (engine.ApiVersion != 1)
    {
        Console.Error.WriteLine($"Unexpected native API version: {engine.ApiVersion}");
        return 1;
    }

    Console.WriteLine("Native API version: 1");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"Interop smoke test failed: {exception.Message}");
    return 1;
}
