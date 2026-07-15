using OpenExplorer.Application.Diagnostics;

return RunSmokeTest();

static int RunSmokeTest()
{
    try
    {
        var source = new SyntheticFileItemList();
        Assert(source.LogicalItemCount == 100_000, "The default logical count is not 100,000.");
        Assert(source.TotalGeneratedItemCount == 0, "Construction generated synthetic rows.");

        int[] requiredIndexes = [0, 1, 999, 50_000, 99_999];
        foreach (int index in requiredIndexes)
        {
            SyntheticFileItem first = source[index];
            SyntheticFileItem second = new SyntheticFileItemList()[index];
            Assert(first.Index == index, $"Returned index did not match {index}.");
            Assert(first.Name == second.Name, $"Name at index {index} was not deterministic.");
        }

        for (int read = 0; read < 10_000; read++)
        {
            int index = (read * 7_919) % source.LogicalItemCount;
            SyntheticFileItem item = source[index];
            Assert(item.Index == index, $"Deterministic read {read} returned the wrong index.");
            Assert(item.Name == CreateExpectedName(index), $"Deterministic read {read} returned the wrong name.");
            Assert(source.CachedItemCount <= source.CacheCapacity, "The cache exceeded its configured capacity.");
        }

        Assert(source.PeakCachedItemCount <= 1_024, "The peak cache exceeded 1,024 items.");
        AssertThrows<ArgumentOutOfRangeException>(() => _ = source[-1]);
        AssertThrows<ArgumentOutOfRangeException>(() => _ = source[100_000]);
        AssertThrows<ArgumentOutOfRangeException>(() => _ = new SyntheticFileItemList(-1));
        AssertThrows<ArgumentOutOfRangeException>(() => _ = new SyntheticFileItemList(1, 0));

        var small = new SyntheticFileItemList(logicalItemCount: 3, cacheCapacity: 2);
        Assert(small.Count == 3, "The custom collection count was incorrect.");
        Assert(small[2].Index == 2, "The custom collection did not return its final item.");
        Assert(small.CachedItemCount <= 2, "The custom collection exceeded its cache capacity.");

        Console.WriteLine(
            $"Virtualization source: {source.LogicalItemCount} items, cache <= {source.CacheCapacity} " +
            $"(generated {source.TotalGeneratedItemCount}, peak {source.PeakCachedItemCount})");
        return 0;
    }
    catch (Exception exception)
    {
        Console.Error.WriteLine($"Virtualization smoke test failed: {exception.Message}");
        return 1;
    }
}

static string CreateExpectedName(int index)
{
    if (index % 17 == 0)
    {
        return $"Folder {index:D5}";
    }

    string[] extensions = ["txt", "pdf", "jpg", "png", "zip", "exe", "dll", "mp4", ""];
    string extension = extensions[index % extensions.Length];
    return extension.Length == 0
        ? $"Document {index:D5}"
        : $"Document {index:D5}.{extension}";
}

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static void AssertThrows<TException>(Action action)
    where TException : Exception
{
    try
    {
        action();
    }
    catch (TException)
    {
        return;
    }

    throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
}
