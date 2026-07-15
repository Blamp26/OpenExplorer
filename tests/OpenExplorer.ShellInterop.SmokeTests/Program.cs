using OpenExplorer.ShellInterop;

try
{
    var extractor = new FakeExtractor();
    using var cache = new BoundedShellIconCache(extractor, capacity: 2, maxPending: 2);
    var first = await cache.GetAsync("report.txt", ShellIconKind.File);
    var same = await cache.GetAsync("other.txt", ShellIconKind.File);
    if (first is null || same is null || extractor.Calls != 1) throw new InvalidOperationException("Duplicate association requests were not coalesced.");

    var pending = Enumerable.Range(0, 4).Select(i => cache.GetAsync($"file{i}.bin", ShellIconKind.File)).ToArray();
    await Task.WhenAll(pending);
    if (cache.Count > 2 || cache.PendingCount != 0) throw new InvalidOperationException("Icon cache or queue exceeded its bound.");

    var malformed = new ShellIconImage(32, 32, new byte[32 * 32 * 4]);
    if (malformed.Pixels.Length != 4096) throw new InvalidOperationException("Icon payload size validation failed.");
    if (OperatingSystem.IsWindows())
    {
        using var real = new WindowsShellIconExtractor().Extract(Environment.SystemDirectory, ShellIconKind.Directory);
        if (real.Width != 32 || real.Height != 32 || real.Pixels.Length != 4096)
            throw new InvalidOperationException("Windows shell icon extraction did not return a 32px image.");
    }
    Console.WriteLine("Shell icon smoke: bounded cache, duplicate coalescing, 32px payload passed");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Shell icon smoke failed: {ex.Message}");
    return 1;
}

sealed class FakeExtractor : IShellIconExtractor
{
    private int _calls;
    public int Calls => _calls;
    public ShellIconImage Extract(string path, ShellIconKind kind)
    {
        Interlocked.Increment(ref _calls);
        Thread.Sleep(10);
        return new ShellIconImage(32, 32, new byte[32 * 32 * 4]);
    }
}
