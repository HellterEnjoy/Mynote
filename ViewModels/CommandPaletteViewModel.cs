using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.RegularExpressions;

namespace MyAvaloniaApp.ViewModels;

public sealed class CommandPaletteViewModel : ViewModelBase
{
    private static readonly Regex TagRegex = new(@"(?<!\w)#(?<tag>[\p{L}\p{N}_-]+)", RegexOptions.Compiled);
    private readonly MainViewModel _main;
    private readonly List<CommandPaletteItem> _commands = new();

    public ObservableCollection<CommandPaletteItem> Items { get; } = new();

    private CommandPaletteItem? _selectedItem;
    public CommandPaletteItem? SelectedItem
    {
        get => _selectedItem;
        set => Set(ref _selectedItem, value);
    }

    private string _query = string.Empty;
    public string Query
    {
        get => _query;
        set
        {
            if (!Set(ref _query, value))
            {
                return;
            }

            RebuildItems();
        }
    }

    public CommandPaletteViewModel(MainViewModel main)
    {
        _main = main;
        BuildCommands();
        RebuildItems();
    }

    public void ExecuteSelected()
    {
        SelectedItem?.Execute?.Invoke();
    }

    private void BuildCommands()
    {
        _commands.Clear();

        _commands.Add(new CommandPaletteItem(
            CommandPaletteItemKind.Command,
            "New note",
            "Create a new note",
            null,
            () => _main.AddNoteCommand.Execute(null)));

        _commands.Add(new CommandPaletteItem(
            CommandPaletteItemKind.Command,
            "Save note",
            "Save current note (Ctrl+S)",
            null,
            () => _main.SaveSelectedNoteCommand.Execute(null)));

        _commands.Add(new CommandPaletteItem(
            CommandPaletteItemKind.Command,
            "Toggle Kanban",
            "Show/hide Kanban board",
            null,
            () => _main.IsKanbanSelected = !_main.IsKanbanSelected,
            executeOnEnter: false));

        _commands.Add(new CommandPaletteItem(
            CommandPaletteItemKind.Command,
            "Toggle theme",
            "Switch light/dark",
            null,
            () => _main.IsDarkTheme = !_main.IsDarkTheme,
            executeOnEnter: false));

        _commands.Add(new CommandPaletteItem(
            CommandPaletteItemKind.Command,
            "Clear search",
            "Reset note search filter",
            null,
            () => _main.SearchQuery = string.Empty));

        _commands.Add(new CommandPaletteItem(
            CommandPaletteItemKind.Command,
            "Clear tag filter",
            "Show notes from all tags",
            null,
            () => _main.ClearTagFilterCommand.Execute(null)));

        _commands.Add(new CommandPaletteItem(
            CommandPaletteItemKind.Command,
            "Autosave: manual only",
            "Disable autosave options",
            null,
            () =>
            {
                _main.SaveOnBlur = false;
                _main.SaveOnClose = false;
                _main.AutoSaveIntervalSeconds = 0;
            },
            executeOnEnter: false));

        _commands.Add(new CommandPaletteItem(
            CommandPaletteItemKind.Command,
            "Autosave: save on blur",
            _main.SaveOnBlur ? "On" : "Off",
            null,
            () => _main.SaveOnBlur = !_main.SaveOnBlur,
            executeOnEnter: false));

        _commands.Add(new CommandPaletteItem(
            CommandPaletteItemKind.Command,
            "Autosave: save on close",
            _main.SaveOnClose ? "On" : "Off",
            null,
            () => _main.SaveOnClose = !_main.SaveOnClose,
            executeOnEnter: false));

        _commands.Add(new CommandPaletteItem(
            CommandPaletteItemKind.Command,
            "Autosave: every 5s",
            _main.AutoSaveIntervalSeconds == 5 ? "On" : "Off",
            null,
            () => _main.AutoSaveIntervalSeconds = _main.AutoSaveIntervalSeconds == 5 ? 0 : 5,
            executeOnEnter: false));
    }

    private void RebuildItems()
    {
        Items.Clear();

        var q = (Query ?? string.Empty).Trim();
        var qLower = q.ToLowerInvariant();

        IEnumerable<CommandPaletteItem> results = _commands;

        if (!string.IsNullOrWhiteSpace(q))
        {
            results = results.Where(c =>
                c.Title.ToLowerInvariant().Contains(qLower) ||
                (c.Subtitle?.ToLowerInvariant().Contains(qLower) ?? false));
        }

        foreach (var cmd in results.Take(8))
        {
            Items.Add(cmd);
        }

        // Tags
        var tags = ExtractTags(_main.Notes.Select(n => n.Content ?? string.Empty));
        if (q.StartsWith("#", StringComparison.Ordinal))
        {
            var tagQ = q[1..].Trim();
            if (!string.IsNullOrWhiteSpace(tagQ))
            {
                tags = tags.Where(t => t.StartsWith(tagQ, StringComparison.OrdinalIgnoreCase)).ToList();
            }
        }
        else if (!string.IsNullOrWhiteSpace(q))
        {
            tags = tags.Where(t => t.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        foreach (var tag in tags.Take(10))
        {
            var label = "#" + tag;
            Items.Add(new CommandPaletteItem(
                CommandPaletteItemKind.Tag,
                label,
                "Filter notes by tag",
                tag,
                () => _main.ApplyTagFilter(tag)));
        }

        // Notes
        var noteCandidates = _main.Notes.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(q))
        {
            noteCandidates = noteCandidates.Where(n =>
                (!string.IsNullOrWhiteSpace(n.Title) && n.Title.Contains(q, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrWhiteSpace(n.Content) && n.Content.Contains(q, StringComparison.OrdinalIgnoreCase)));
        }

        foreach (var note in noteCandidates.Take(20))
        {
            Items.Add(new CommandPaletteItem(
                CommandPaletteItemKind.Note,
                note.Title,
                "Open note",
                note,
                () =>
                {
                    _main.SelectedNote = note;
                    _main.IsEditorSelected = true;
                }));
        }

        SelectedItem = Items.FirstOrDefault();
    }

    private static List<string> ExtractTags(IEnumerable<string> contents)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var content in contents)
        {
            foreach (Match match in TagRegex.Matches(content))
            {
                var tag = match.Groups["tag"].Value;
                if (!string.IsNullOrWhiteSpace(tag))
                {
                    set.Add(tag);
                }
            }
        }

        return set.OrderBy(t => t, StringComparer.OrdinalIgnoreCase).ToList();
    }
}
