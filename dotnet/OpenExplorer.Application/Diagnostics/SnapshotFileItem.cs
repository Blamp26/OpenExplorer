using System.Globalization;
using OpenExplorer.Contracts;

namespace OpenExplorer.Application.Diagnostics;

public sealed class SnapshotFileItem
{
    internal SnapshotFileItem(ExplorerItem item)
    {
        SourceItem = item;
        ItemId = item.ItemId;
        Name = item.Name;
        DateModifiedText = item.DateModified.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        IsDirectory = item.Kind == ExplorerItemKind.Directory;
        TypeText = IsDirectory ? "File folder" : GetTypeText(item.Name);
        SizeText = item.Size.HasValue ? FormatSize(item.Size.Value) : string.Empty;
    }

    public ulong ItemId { get; }

    public ExplorerItem SourceItem { get; }

    public string Name { get; }

    public string DateModifiedText { get; }

    public string TypeText { get; }

    public string SizeText { get; }

    public bool IsDirectory { get; }

    private static string GetTypeText(string name)
    {
        string extension = Path.GetExtension(name).ToLowerInvariant();
        return extension switch
        {
            ".txt" => "Text document",
            ".pdf" => "PDF document",
            ".jpg" or ".png" => "Image file",
            ".zip" => "Compressed archive",
            ".exe" => "Application",
            ".dll" => "Application extension",
            ".mp4" => "Video file",
            _ => "File",
        };
    }

    private static string FormatSize(ulong bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.0} KB",
        _ => $"{bytes / (1024.0 * 1024.0):0.0} MB",
    };
}
