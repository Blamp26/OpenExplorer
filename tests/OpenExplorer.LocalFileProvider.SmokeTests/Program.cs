using System.Diagnostics;
using OpenExplorer.Contracts;
using OpenExplorer.Interop;

string root = Path.Combine(Path.GetTempPath(), $"OpenExplorer-LocalProvider-{Environment.ProcessId}-{Stopwatch.GetTimestamp()}");
Exception? failure = null;
try
{
    Directory.CreateDirectory(root);
    File.WriteAllBytes(Path.Combine(root, "normal.txt"), new byte[] { 1, 2, 3, 4, 5 });
    File.WriteAllBytes(Path.Combine(root, "empty.bin"), Array.Empty<byte>());
    File.WriteAllText(Path.Combine(root, "extensionless"), "abc");
    File.WriteAllText(Path.Combine(root, "Unicode-Пример.txt"), "unicode");
    File.WriteAllText(Path.Combine(root, "日本語.txt"), "japanese");
    Directory.CreateDirectory(Path.Combine(root, "Subdirectory"));

    using var engine = new NativeExplorerEngine();
    Assert(engine.ApiVersion == 5, $"Expected API version 5, got {engine.ApiVersion}.");
    using IExplorerSnapshot snapshot = engine.OpenSnapshot(ExplorerLocation.File(Path.GetFullPath(root)));
    Assert(snapshot.Count == 6, $"Expected six immediate children, got {snapshot.Count}.");
    ExplorerItem[] items = snapshot.GetRange(0, 4096).Items.ToArray();
    var byName = items.ToDictionary(item => item.Name, StringComparer.Ordinal);
    Assert(byName["normal.txt"].Size == 5 && byName["normal.txt"].Kind == ExplorerItemKind.File, "normal.txt metadata was incorrect.");
    Assert(byName["empty.bin"].Size == 0, "The empty file size was not preserved.");
    Assert(byName["extensionless"].Kind == ExplorerItemKind.File, "The extensionless file kind was incorrect.");
    Assert(byName["Subdirectory"].Kind == ExplorerItemKind.Directory && byName["Subdirectory"].Size is null, "Directory metadata was incorrect.");
    Assert(byName.ContainsKey("Unicode-Пример.txt") && byName.ContainsKey("日本語.txt"), "Unicode names did not survive the native boundary.");
    ExpectStatus(() => snapshot.GetRange(snapshot.Count + 1, 0), "OUT_OF_RANGE");
    ExpectStatus(() => snapshot.GetRange(0, 4097), "INVALID_ARGUMENT");

    engine.Dispose();
    Assert(snapshot.GetRange(0, 1).Items.Count == 1, "The snapshot did not survive engine disposal.");

    using var errorEngine = new NativeExplorerEngine();
    ExpectStatus(() => errorEngine.OpenSnapshot(ExplorerLocation.File(Path.Combine(root, "missing"))), "NOT_FOUND");
    ExpectStatus(() => errorEngine.OpenSnapshot(ExplorerLocation.File(Path.Combine(root, "normal.txt"))), "NOT_DIRECTORY");
    ExpectStatus(() => errorEngine.OpenSnapshot(ExplorerLocation.File("relative-directory")), "INVALID_ARGUMENT");

    string empty = Path.Combine(root, "empty");
    Directory.CreateDirectory(empty);
    using (var emptyEngine = new NativeExplorerEngine())
    using (IExplorerSnapshot emptySnapshot = emptyEngine.OpenSnapshot(ExplorerLocation.File(empty)))
    {
        Assert(emptySnapshot.Count == 0 && emptySnapshot.GetRange(0, 10).Items.Count == 0, "Empty directory handling failed.");
    }

    Console.WriteLine("Local file provider API version: 5, directory snapshot passed");
}
catch (Exception exception)
{
    failure = exception;
}
finally
{
    try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    catch (Exception cleanup) { Console.Error.WriteLine($"Fixture cleanup failed: {cleanup.Message}"); if (failure is null) failure = cleanup; }
}

if (failure is not null)
{
    Console.Error.WriteLine($"Local file provider smoke test failed: {failure.Message}");
    return 1;
}
return 0;

static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

static void ExpectStatus(Action action, string status)
{
    try { action(); }
    catch (NativeInteropException exception) when (exception.Message.Contains(status, StringComparison.Ordinal)) { return; }
    throw new InvalidOperationException($"Expected native status {status}.");
}
