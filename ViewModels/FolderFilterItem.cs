namespace MyAvaloniaApp.ViewModels;

public sealed class FolderFilterItem
{
    public string Name { get; }
    public Guid? FolderId { get; }
    public bool IsUnfiled { get; }

    public FolderFilterItem(string name, Guid? folderId, bool isUnfiled = false)
    {
        Name = name;
        FolderId = folderId;
        IsUnfiled = isUnfiled;
    }

    public override string ToString() => Name;
}

