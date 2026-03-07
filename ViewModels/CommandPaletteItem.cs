using System;

namespace MyAvaloniaApp.ViewModels;

public enum CommandPaletteItemKind
{
    Command,
    Note,
    Tag,
}

public sealed class CommandPaletteItem
{
    public CommandPaletteItemKind Kind { get; }
    public string Title { get; }
    public string? Subtitle { get; }
    public object? Payload { get; }
    public Action? Execute { get; }
    public bool ExecuteOnEnter { get; }

    public CommandPaletteItem(
        CommandPaletteItemKind kind,
        string title,
        string? subtitle,
        object? payload,
        Action? execute,
        bool executeOnEnter = true)
    {
        Kind = kind;
        Title = title;
        Subtitle = subtitle;
        Payload = payload;
        Execute = execute;
        ExecuteOnEnter = executeOnEnter;
    }
}
