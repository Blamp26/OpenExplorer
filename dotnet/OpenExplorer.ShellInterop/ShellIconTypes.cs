namespace OpenExplorer.ShellInterop;

public enum ShellIconKind : byte
{
    File,
    Directory,
}

public readonly record struct ShellIconKey(string Value)
{
    public static ShellIconKey ForPath(string path, ShellIconKind kind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (kind == ShellIconKind.Directory)
        {
            return new ShellIconKey("dir:" + (int)(Directory.Exists(full) ? File.GetAttributes(full) : 0));
        }

        var extension = Path.GetExtension(full).ToUpperInvariant();
        var attributes = File.Exists(full) ? File.GetAttributes(full) : 0;
        // Association icons are reusable by type. Embedded executable icons are path-sensitive.
        return extension is ".EXE" or ".ICO" or ".DLL"
            ? new ShellIconKey($"path:{full.ToUpperInvariant()}|{(int)attributes}")
            : new ShellIconKey($"file:{extension}|{(int)attributes}");
    }
}

public sealed class ShellIconImage : IDisposable
{
    public ShellIconImage(int width, int height, ReadOnlyMemory<byte> bgraPixels)
    {
        if (width <= 0 || height <= 0 || bgraPixels.Length != checked(width * height * 4))
            throw new ArgumentOutOfRangeException(nameof(bgraPixels));
        Width = width;
        Height = height;
        Pixels = bgraPixels.ToArray();
    }

    public int Width { get; }
    public int Height { get; }
    public ReadOnlyMemory<byte> Pixels { get; }
    public void Dispose() { }
}

public interface IShellIconExtractor
{
    ShellIconImage Extract(string path, ShellIconKind kind);
}
