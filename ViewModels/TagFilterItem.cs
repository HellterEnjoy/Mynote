namespace MyAvaloniaApp.ViewModels;

public sealed class TagFilterItem
{
    public TagFilterItem(string label, string? tagName, int count, bool isAll = false)
    {
        Label = label;
        TagName = tagName;
        Count = count;
        IsAll = isAll;
    }

    public string Label { get; }
    public string? TagName { get; }
    public int Count { get; }
    public bool IsAll { get; }
}

