namespace OpenExplorer.Application.Diagnostics;

public sealed class SyntheticFileItem
{
    internal SyntheticFileItem(
        int index,
        string name,
        string dateModifiedText,
        string typeText,
        string sizeText,
        bool isDirectory)
    {
        Index = index;
        Name = name;
        DateModifiedText = dateModifiedText;
        TypeText = typeText;
        SizeText = sizeText;
        IsDirectory = isDirectory;
    }

    public int Index { get; }

    public string Name { get; }

    public string DateModifiedText { get; }

    public string TypeText { get; }

    public string SizeText { get; }

    public bool IsDirectory { get; }
}
