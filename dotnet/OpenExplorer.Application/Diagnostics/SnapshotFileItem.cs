using System.Globalization;
using OpenExplorer.Contracts;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace OpenExplorer.Application.Diagnostics;

public sealed class SnapshotFileItem : INotifyPropertyChanged
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

    public string IconGlyph { get; private set; } = "□";

    public event PropertyChangedEventHandler? PropertyChanged;

    public void SetIcon(OpenExplorer.Application.Icons.ExplorerIconResult result)
    {
        if (result.ItemId != ItemId) return;
        string glyph = result.Kind is OpenExplorer.Application.Icons.ExplorerIconKind.GenericFolder or OpenExplorer.Application.Icons.ExplorerIconKind.ShellFolder ? "📁" : "📄";
        if (IconGlyph == glyph) return;
        IconGlyph = glyph;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IconGlyph)));
    }

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
