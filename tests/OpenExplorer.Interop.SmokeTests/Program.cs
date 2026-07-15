using OpenExplorer.Contracts;
using OpenExplorer.Interop;

try
{
    using var engine = new NativeExplorerEngine();
    if (engine.ApiVersion != 3)
    {
        Console.Error.WriteLine($"Unexpected native API version: {engine.ApiVersion}");
        return 1;
    }

    using IExplorerSnapshot snapshot = engine.CreateSyntheticSnapshot(100_000);
    if (snapshot.Count != 100_000)
    {
        throw new InvalidOperationException($"Unexpected snapshot count: {snapshot.Count}.");
    }

    ExplorerItem first = snapshot.GetRange(0, 4).Items[0];
    ExplorerItem middle = snapshot.GetRange(50_000, 4).Items[0];
    ExplorerItem final = snapshot.GetRange(99_999, 4).Items[0];
    if (first.ItemId != 1 || first.Name != "Folder 00000" || middle.ItemId != 50_001 || final.ItemId != 100_000)
    {
        throw new InvalidOperationException("Native snapshot range values were not deterministic.");
    }

    ExplorerItem unicode = snapshot.GetRange(42, 1).Items[0];
    ExplorerItem longName = snapshot.GetRange(4_242, 1).Items[0];
    if (!unicode.Name.Contains("Résumé", StringComparison.Ordinal) || !unicode.Name.Contains("директория", StringComparison.Ordinal))
    {
        throw new InvalidOperationException("UTF-8 Unicode text did not survive the native boundary.");
    }
    if (longName.Name.Length <= 260)
    {
        throw new InvalidOperationException("The long native name was truncated.");
    }

    Console.WriteLine("Native snapshot API version: 3, items: 100000, range paging passed");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"Interop smoke test failed: {exception.Message}");
    return 1;
}
