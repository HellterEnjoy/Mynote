using System.Globalization;
using System.Text.Json;
using Mynote.Models;

namespace Mynote.Services;

public sealed class NoteStore
{
    private sealed class LegacyStoreData
    {
        public List<Project> Projects { get; set; } = [];
        public List<KanbanColumn> Columns { get; set; } = [];
        public List<Folder> Folders { get; set; } = [];
        public List<Note> Notes { get; set; } = [];
        public List<KanbanCard> Cards { get; set; } = [];
        public Dictionary<Guid, Guid> LastOpenedNoteByProjectId { get; set; } = [];
    }

    private sealed class SplitConfigData
    {
        public string Format { get; set; } = "mynote-split-config";
        public int Version { get; set; } = 1;
        public List<Project> Projects { get; set; } = [];
        public List<KanbanColumn> Columns { get; set; } = [];
        public List<Folder> Folders { get; set; } = [];
    }

    private sealed class SplitStateData
    {
        public string Format { get; set; } = "mynote-split-state";
        public int Version { get; set; } = 1;
        public Dictionary<Guid, Guid> LastOpenedNoteByProjectId { get; set; } = [];
    }

    private sealed class SplitKanbanData
    {
        public string Format { get; set; } = "mynote-kanban";
        public int Version { get; set; } = 1;
        public List<KanbanCard> Cards { get; set; } = [];
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    private readonly object _lock = new();
    private readonly bool _useSplitStore;
    private readonly string _legacyStorePath;
    private readonly string? _projectRootPath;
    private readonly string? _mynoteDir;
    private readonly string? _configPath;
    private readonly string? _statePath;
    private readonly string? _notesDir;
    private readonly string? _kanbanPath;
    private readonly string _initialProjectName;

    private List<Project> _projects = [];
    private List<KanbanColumn> _columns = [];
    private List<Folder> _folders = [];
    private List<Note> _notes = [];
    private List<KanbanCard> _cards = [];
    private Dictionary<Guid, Guid> _lastOpenedNoteByProjectId = [];
    private bool _loaded;

    public NoteStore()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var directory = Path.Combine(root, "Mynote");
        _legacyStorePath = Path.Combine(directory, "notes.json");
        _useSplitStore = false;
        _projectRootPath = null;
        _mynoteDir = null;
        _configPath = null;
        _statePath = null;
        _notesDir = null;
        _kanbanPath = null;
        _initialProjectName = "Default";
    }

    public NoteStore(string projectRootPath, string? initialProjectName = null)
    {
        if (string.IsNullOrWhiteSpace(projectRootPath))
        {
            throw new ArgumentException("Project root path is required.", nameof(projectRootPath));
        }

        _projectRootPath = projectRootPath.Trim();
        _legacyStorePath = Path.Combine(_projectRootPath, "mynote.json");
        _useSplitStore = true;
        _mynoteDir = Path.Combine(_projectRootPath, ".mynote");
        _configPath = Path.Combine(_mynoteDir, "config.json");
        _statePath = Path.Combine(_mynoteDir, "state.json");
        _notesDir = Path.Combine(_projectRootPath, "notes");
        _kanbanPath = Path.Combine(_mynoteDir, "kanban.json");
        _initialProjectName = string.IsNullOrWhiteSpace(initialProjectName) ? "Default" : initialProjectName.Trim();
    }

    public void Load()
    {
        lock (_lock)
        {
            if (_loaded)
            {
                return;
            }

            if (_useSplitStore)
            {
                LoadSplit();
            }
            else
            {
                LoadLegacy(_legacyStorePath);
                PersistLegacy();
            }

            _loaded = true;
        }
    }

    public IReadOnlyList<Project> GetProjects()
    {
        lock (_lock)
        {
            return _projects.ToList();
        }
    }

    public IReadOnlyList<KanbanColumn> GetColumns(Guid projectId)
    {
        lock (_lock)
        {
            return _columns.Where(c => c.ProjectId == projectId).ToList();
        }
    }

    public IReadOnlyList<Note> GetNotes(Guid projectId)
    {
        lock (_lock)
        {
            return _notes.Where(n => n.ProjectId == projectId).ToList();
        }
    }

    public IReadOnlyList<KanbanCard> GetCards(Guid projectId)
    {
        lock (_lock)
        {
            return _cards.Where(c => c.ProjectId == projectId).ToList();
        }
    }

    public IReadOnlyList<Folder> GetFolders(Guid projectId)
    {
        lock (_lock)
        {
            return _folders.Where(f => f.ProjectId == projectId).OrderBy(f => f.CreatedAt).ToList();
        }
    }

    public Guid? GetLastOpenedNoteId(Guid projectId)
    {
        lock (_lock)
        {
            return _lastOpenedNoteByProjectId.TryGetValue(projectId, out var noteId) ? noteId : null;
        }
    }

    public void SetLastOpenedNoteId(Guid projectId, Guid noteId)
    {
        lock (_lock)
        {
            if (_lastOpenedNoteByProjectId.TryGetValue(projectId, out var existing) && existing == noteId)
            {
                return;
            }

            _lastOpenedNoteByProjectId[projectId] = noteId;
            if (_useSplitStore)
            {
                PersistSplitState();
            }
            else
            {
                PersistLegacy();
            }
        }
    }

    public Project CreateProject()
    {
        lock (_lock)
        {
            var project = new Project
            {
                Name = NextProjectName(),
                CreatedAt = DateTime.UtcNow,
            };

            _projects.Add(project);
            CreateDefaultColumns(project.Id);
            PersistStructure();
            return project;
        }
    }

    public KanbanColumn CreateColumn(Guid projectId)
    {
        lock (_lock)
        {
            var column = new KanbanColumn
            {
                ProjectId = projectId,
                Title = NextColumnTitle(projectId),
                Order = NextColumnOrder(projectId),
            };

            _columns.Add(column);
            PersistStructure();
            return column;
        }
    }

    public void RenameColumn(Guid columnId, string title)
    {
        lock (_lock)
        {
            var column = _columns.FirstOrDefault(c => c.Id == columnId);
            if (column is null)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(title))
            {
                column.Title = title.Trim();
                PersistStructure();
            }
        }
    }

    public void DeleteColumn(Guid columnId)
    {
        lock (_lock)
        {
            var column = _columns.FirstOrDefault(c => c.Id == columnId);
            if (column is null)
            {
                return;
            }

            var projectColumns = _columns.Where(c => c.ProjectId == column.ProjectId).OrderBy(c => c.Order).ToList();
            if (projectColumns.Count <= 1)
            {
                return;
            }

            _columns.RemoveAll(c => c.Id == columnId);
            var fallback = projectColumns.FirstOrDefault(c => c.Id != columnId);
            if (fallback is not null)
            {
                foreach (var note in _notes.Where(n => n.ColumnId == columnId))
                {
                    note.ColumnId = fallback.Id;
                    note.Order = NextNoteOrder(fallback.Id);
                    PersistNoteFileIfSplit(note);
                }

                foreach (var card in _cards.Where(c => c.ColumnId == columnId))
                {
                    card.ColumnId = fallback.Id;
                    card.Order = NextCardOrder(fallback.Id);
                }
            }

            PersistStructure();
            PersistKanban();
        }
    }

    public Note CreateNote(Guid projectId)
    {
        lock (_lock)
        {
            var column = GetOrCreateFirstColumn(projectId);
            var note = new Note
            {
                ProjectId = projectId,
                ColumnId = column.Id,
                FolderId = null,
                Title = NextUntitledTitle(projectId),
                Content = string.Empty,
                UpdatedAt = DateTime.UtcNow,
                Order = NextNoteOrder(column.Id),
            };

            _notes.Add(note);
            PersistNoteChange(note);

            return note;
        }
    }

    public KanbanCard CreateCard(Guid projectId, Guid? columnId = null)
    {
        lock (_lock)
        {
            var column = columnId.HasValue
                ? _columns.FirstOrDefault(c => c.Id == columnId.Value) ?? GetOrCreateFirstColumn(projectId)
                : GetOrCreateFirstColumn(projectId);

            var card = new KanbanCard
            {
                ProjectId = projectId,
                ColumnId = column.Id,
                Title = "New Card",
                UpdatedAt = DateTime.UtcNow,
                Order = NextCardOrder(column.Id),
            };

            _cards.Add(card);
            PersistKanban();
            return card;
        }
    }

    public void UpdateCard(KanbanCard card)
    {
        lock (_lock)
        {
            var existing = _cards.FirstOrDefault(c => c.Id == card.Id);
            if (existing is null)
            {
                _cards.Add(card);
            }
            else
            {
                existing.ProjectId = card.ProjectId;
                existing.ColumnId = card.ColumnId;
                existing.Title = card.Title;
                existing.Content = card.Content;
                existing.LinkedNoteId = card.LinkedNoteId;
                existing.UpdatedAt = card.UpdatedAt;
                existing.Order = card.Order;
            }

            PersistKanban();
        }
    }

    public void DeleteCard(Guid cardId)
    {
        lock (_lock)
        {
            _cards.RemoveAll(c => c.Id == cardId);
            PersistKanban();
        }
    }

    public void MoveCard(Guid cardId, Guid columnId, Guid? beforeCardId)
    {
        lock (_lock)
        {
            var card = _cards.FirstOrDefault(c => c.Id == cardId);
            if (card is null)
            {
                return;
            }

            var column = _columns.FirstOrDefault(c => c.Id == columnId);
            if (column is null)
            {
                return;
            }

            card.ProjectId = column.ProjectId;
            card.ColumnId = column.Id;

            var columnCards = _cards
                .Where(c => c.ColumnId == column.Id && c.Id != card.Id)
                .OrderBy(c => c.Order)
                .ThenByDescending(c => c.UpdatedAt)
                .ToList();

            if (beforeCardId.HasValue)
            {
                var index = columnCards.FindIndex(c => c.Id == beforeCardId.Value);
                if (index < 0)
                {
                    columnCards.Add(card);
                }
                else
                {
                    columnCards.Insert(index, card);
                }
            }
            else
            {
                columnCards.Add(card);
            }

            for (var i = 0; i < columnCards.Count; i++)
            {
                columnCards[i].Order = i;
            }

            PersistKanban();
        }
    }

    public void UpdateNote(Note note)
    {
        lock (_lock)
        {
            var existing = _notes.FirstOrDefault(n => n.Id == note.Id);
            if (existing is null)
            {
                _notes.Add(note);
                existing = note;
            }

            existing.Title = string.IsNullOrWhiteSpace(note.Title) ? "Untitled" : note.Title.Trim();
            existing.Content = note.Content ?? string.Empty;
            existing.ProjectId = note.ProjectId;
            existing.ColumnId = note.ColumnId;
            existing.FolderId = note.FolderId;
            existing.Order = note.Order;
            existing.UpdatedAt = DateTime.UtcNow;
            PersistNoteChange(existing);
        }
    }

    public Folder CreateFolder(Guid projectId, string name)
    {
        lock (_lock)
        {
            var folder = new Folder
            {
                ProjectId = projectId,
                Name = string.IsNullOrWhiteSpace(name) ? "New Folder" : name.Trim(),
                CreatedAt = DateTime.UtcNow,
            };

            _folders.Add(folder);
            PersistStructure();
            return folder;
        }
    }

    public void RenameFolder(Guid folderId, string name)
    {
        lock (_lock)
        {
            var folder = _folders.FirstOrDefault(f => f.Id == folderId);
            if (folder is null)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(name))
            {
                folder.Name = name.Trim();
                PersistStructure();
            }
        }
    }

    public void DeleteFolder(Guid folderId)
    {
        lock (_lock)
        {
            _folders.RemoveAll(f => f.Id == folderId);
            foreach (var note in _notes.Where(n => n.FolderId == folderId))
            {
                note.FolderId = null;
                PersistNoteFileIfSplit(note);
            }

            PersistStructure();
        }
    }

    public void DeleteNote(Note note)
    {
        lock (_lock)
        {
            _notes.RemoveAll(n => n.Id == note.Id);
            DeleteNoteFileIfSplit(note.Id);

            if (!_useSplitStore)
            {
                PersistLegacy();
            }
        }
    }

    public void MoveNote(Guid noteId, Guid columnId, Guid? beforeNoteId)
    {
        lock (_lock)
        {
            var note = _notes.FirstOrDefault(n => n.Id == noteId);
            if (note is null)
            {
                return;
            }

            var column = _columns.FirstOrDefault(c => c.Id == columnId);
            if (column is null)
            {
                return;
            }

            note.ProjectId = column.ProjectId;
            note.ColumnId = column.Id;

            var columnNotes = _notes
                .Where(n => n.ColumnId == column.Id && n.Id != note.Id)
                .OrderBy(n => n.Order)
                .ThenByDescending(n => n.UpdatedAt)
                .ToList();

            if (beforeNoteId.HasValue)
            {
                var index = columnNotes.FindIndex(n => n.Id == beforeNoteId.Value);
                if (index < 0)
                {
                    columnNotes.Add(note);
                }
                else
                {
                    columnNotes.Insert(index, note);
                }
            }
            else
            {
                columnNotes.Add(note);
            }

            for (var i = 0; i < columnNotes.Count; i++)
            {
                columnNotes[i].Order = i;
                PersistNoteFileIfSplit(columnNotes[i]);
            }

            if (!_useSplitStore)
            {
                PersistLegacy();
            }
        }
    }

    private void LoadSplit()
    {
        Directory.CreateDirectory(_projectRootPath!);
        Directory.CreateDirectory(_mynoteDir!);
        Directory.CreateDirectory(_notesDir!);

        if (File.Exists(_configPath!))
        {
            LoadSplitConfig();
            LoadSplitNotes();
            LoadSplitState();
            LoadSplitKanban();
        }
        else if (File.Exists(_legacyStorePath))
        {
            LoadLegacy(_legacyStorePath);
            MigrateLegacyToSplit();
        }
        else
        {
            ResetInMemoryData();
        }

        EnsureDefaultProject();
        EnsureColumnsForProjects();
        _folders ??= [];
        PersistSplitConfig();
        PersistSplitState();
        PersistKanban();
    }

    private void LoadLegacy(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (!File.Exists(path))
        {
            ResetInMemoryData();
            EnsureDefaultProject();
            EnsureColumnsForProjects();
            _folders ??= [];
            return;
        }

        try
        {
            var json = File.ReadAllText(path);
            var data = JsonSerializer.Deserialize<LegacyStoreData>(json, JsonOptions);
            _projects = data?.Projects ?? [];
            _columns = data?.Columns ?? [];
            _folders = data?.Folders ?? [];
            _notes = data?.Notes ?? [];
            _cards = data?.Cards ?? [];
            _lastOpenedNoteByProjectId = data?.LastOpenedNoteByProjectId ?? [];
        }
        catch
        {
            ResetInMemoryData();
        }

        EnsureDefaultProject();
        EnsureColumnsForProjects();
        _folders ??= [];
    }

    private void LoadSplitConfig()
    {
        var cfg = TryReadJson<SplitConfigData>(_configPath!);
        _projects = cfg?.Projects ?? [];
        _columns = cfg?.Columns ?? [];
        _folders = cfg?.Folders ?? [];
    }

    private void LoadSplitState()
    {
        if (!File.Exists(_statePath!))
        {
            _lastOpenedNoteByProjectId = [];
            return;
        }

        var state = TryReadJson<SplitStateData>(_statePath!);
        _lastOpenedNoteByProjectId = state?.LastOpenedNoteByProjectId ?? [];
    }

    private void LoadSplitNotes()
    {
        _notes = [];
        if (!Directory.Exists(_notesDir!))
        {
            return;
        }

        foreach (var path in Directory.EnumerateFiles(_notesDir!, "*.md", SearchOption.TopDirectoryOnly))
        {
            if (!TryReadNoteFile(path, out var note))
            {
                continue;
            }

            _notes.Add(note);
        }

        _notes = _notes
            .GroupBy(n => n.Id)
            .Select(g => g.OrderByDescending(n => n.UpdatedAt).First())
            .ToList();

        SanitizeSplitNotes();
    }

    private void LoadSplitKanban()
    {
        _cards = [];
        if (string.IsNullOrWhiteSpace(_kanbanPath) || !File.Exists(_kanbanPath))
        {
            // Migration: older versions used Notes as Kanban items. Create linked cards once.
            if (_notes.Count > 0)
            {
                _cards = _notes
                    .OrderBy(n => n.ColumnId)
                    .ThenBy(n => n.Order)
                    .ThenByDescending(n => n.UpdatedAt)
                    .Select(n => new KanbanCard
                    {
                        ProjectId = n.ProjectId,
                        ColumnId = n.ColumnId,
                        Title = n.Title ?? "Untitled",
                        Content = string.Empty,
                        LinkedNoteId = n.Id,
                        UpdatedAt = n.UpdatedAt,
                        Order = n.Order,
                    })
                    .ToList();
            }

            return;
        }

        var data = TryReadJson<SplitKanbanData>(_kanbanPath);
        _cards = data?.Cards ?? [];

        SanitizeSplitCards();
    }

    private void SanitizeSplitCards()
    {
        var defaultProject = _projects.FirstOrDefault();
        if (defaultProject is null)
        {
            return;
        }

        var projectIds = _projects.Select(p => p.Id).ToHashSet();
        var columnIds = _columns.Select(c => c.Id).ToHashSet();
        var touched = false;

        foreach (var card in _cards)
        {
            if (card.ProjectId == Guid.Empty || !projectIds.Contains(card.ProjectId))
            {
                card.ProjectId = defaultProject.Id;
                touched = true;
            }

            if (card.ColumnId == Guid.Empty || !columnIds.Contains(card.ColumnId))
            {
                card.ColumnId = GetOrCreateFirstColumn(card.ProjectId).Id;
                touched = true;
            }

            card.Title ??= "New Card";
        }

        if (touched)
        {
            PersistKanban();
        }
    }

    private void SanitizeSplitNotes()
    {
        var defaultProject = _projects.FirstOrDefault();
        if (defaultProject is null)
        {
            return;
        }

        var projectIds = _projects.Select(p => p.Id).ToHashSet();
        var columnIds = _columns.Select(c => c.Id).ToHashSet();
        var folderIds = _folders.Select(f => f.Id).ToHashSet();

        foreach (var note in _notes)
        {
            var changed = false;

            if (note.ProjectId == Guid.Empty || !projectIds.Contains(note.ProjectId))
            {
                note.ProjectId = defaultProject.Id;
                changed = true;
            }

            if (note.ColumnId == Guid.Empty || !columnIds.Contains(note.ColumnId))
            {
                note.ColumnId = GetOrCreateFirstColumn(note.ProjectId).Id;
                changed = true;
            }

            if (note.FolderId.HasValue && !folderIds.Contains(note.FolderId.Value))
            {
                note.FolderId = null;
                changed = true;
            }

            if (changed)
            {
                PersistNoteFileIfSplit(note);
            }
        }
    }

    private void MigrateLegacyToSplit()
    {
        Directory.CreateDirectory(_notesDir!);
        foreach (var note in _notes)
        {
            PersistNoteFileIfSplit(note);
        }

        PersistSplitConfig();
        PersistSplitState();
        PersistKanban();

        try
        {
            File.Move(_legacyStorePath, Path.Combine(_mynoteDir!, "legacy.mynote.json"), overwrite: true);
        }
        catch
        {
            // Ignore.
        }
    }

    private void PersistStructure()
    {
        if (_useSplitStore)
        {
            PersistSplitConfig();
        }
        else
        {
            PersistLegacy();
        }
    }

    private void PersistLegacy()
    {
        var data = new LegacyStoreData
        {
            Projects = _projects,
            Columns = _columns,
            Folders = _folders,
            Notes = _notes,
            Cards = _cards,
            LastOpenedNoteByProjectId = _lastOpenedNoteByProjectId,
        };
        WriteJson(_legacyStorePath, data);
    }

    private void PersistSplitConfig()
    {
        var cfg = new SplitConfigData
        {
            Projects = _projects,
            Columns = _columns,
            Folders = _folders,
        };
        WriteJson(_configPath!, cfg);
    }

    private void PersistSplitState()
    {
        var state = new SplitStateData
        {
            LastOpenedNoteByProjectId = _lastOpenedNoteByProjectId,
        };
        WriteJson(_statePath!, state);
    }

    private void PersistKanban()
    {
        if (_useSplitStore)
        {
            PersistSplitKanban();
        }
        else
        {
            PersistLegacy();
        }
    }

    private void PersistSplitKanban()
    {
        if (string.IsNullOrWhiteSpace(_kanbanPath))
        {
            return;
        }

        var data = new SplitKanbanData
        {
            Cards = _cards,
        };
        WriteJson(_kanbanPath, data);
    }

    private void PersistNoteFileIfSplit(Note note)
    {
        if (!_useSplitStore)
        {
            return;
        }

        Directory.CreateDirectory(_notesDir!);
        var path = Path.Combine(_notesDir!, $"{note.Id:D}.md");
        File.WriteAllText(path, BuildFrontMatter(note) + NormalizeNewlines(note.Content ?? string.Empty));
    }

    private void DeleteNoteFileIfSplit(Guid noteId)
    {
        if (!_useSplitStore)
        {
            return;
        }

        try
        {
            var path = Path.Combine(_notesDir!, $"{noteId:D}.md");
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Ignore.
        }
    }

    private static string NormalizeNewlines(string value)
        => value.Replace("\r\n", "\n").Replace("\r", "\n");

    private static string BuildFrontMatter(Note note)
    {
        var lines = new List<string>
        {
            "---",
            $"id: {note.Id:D}",
            $"projectId: {note.ProjectId:D}",
            $"columnId: {note.ColumnId:D}",
            $"folderId: {(note.FolderId is Guid id ? id.ToString("D") : "null")}",
            $"title: {YamlQuote(note.Title ?? "Untitled")}",
            $"updatedAt: {note.UpdatedAt.ToString("O", CultureInfo.InvariantCulture)}",
            $"order: {note.Order}",
            "---",
            string.Empty
        };
        return string.Join("\n", lines);
    }

    private static string YamlQuote(string value)
        => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    private static bool TryReadNoteFile(string path, out Note note)
    {
        note = new Note();

        try
        {
            var fileName = Path.GetFileNameWithoutExtension(path);
            if (!Guid.TryParse(fileName, out var idFromName))
            {
                return false;
            }

            var text = NormalizeNewlines(File.ReadAllText(path));
            note.Id = idFromName;

            if (!text.StartsWith("---\n", StringComparison.Ordinal))
            {
                note.Title = "Untitled";
                note.Content = text;
                note.UpdatedAt = File.GetLastWriteTimeUtc(path);
                return true;
            }

            var end = text.IndexOf("\n---\n", 4, StringComparison.Ordinal);
            if (end < 0)
            {
                return false;
            }

            var header = text.Substring(4, end - 4);
            var body = text[(end + "\n---\n".Length)..];
            var map = ParseFrontMatter(header);

            note.ProjectId = TryGetGuid(map, "projectId", out var projectId) ? projectId : Guid.Empty;
            note.ColumnId = TryGetGuid(map, "columnId", out var columnId) ? columnId : Guid.Empty;
            note.FolderId = TryGetNullableGuid(map, "folderId", out var folderId) ? folderId : null;
            note.Title = TryGetString(map, "title", out var title) ? title : "Untitled";
            note.UpdatedAt = TryGetDateTime(map, "updatedAt", out var updated) ? updated : File.GetLastWriteTimeUtc(path);
            note.Order = TryGetInt(map, "order", out var order) ? order : 0;
            note.Content = body;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static Dictionary<string, string> ParseFrontMatter(string header)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var lines = header.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var idx = line.IndexOf(':', StringComparison.Ordinal);
            if (idx <= 0)
            {
                continue;
            }

            var key = line[..idx].Trim();
            var value = line[(idx + 1)..].Trim();
            map[key] = value;
        }
        return map;
    }

    private static bool TryGetString(Dictionary<string, string> map, string key, out string value)
    {
        value = string.Empty;
        if (!map.TryGetValue(key, out var raw))
        {
            return false;
        }

        value = UnquoteYamlString(raw);
        return true;
    }

    private static string UnquoteYamlString(string raw)
    {
        if (raw.Length >= 2 && raw.StartsWith('\"') && raw.EndsWith('\"'))
        {
            var inner = raw[1..^1];
            return inner.Replace("\\\"", "\"").Replace("\\\\", "\\");
        }

        return raw;
    }

    private static bool TryGetGuid(Dictionary<string, string> map, string key, out Guid value)
    {
        value = Guid.Empty;
        return map.TryGetValue(key, out var raw) && Guid.TryParse(UnquoteYamlString(raw), out value);
    }

    private static bool TryGetNullableGuid(Dictionary<string, string> map, string key, out Guid? value)
    {
        value = null;
        if (!map.TryGetValue(key, out var raw))
        {
            return false;
        }

        var v = UnquoteYamlString(raw);
        if (string.Equals(v, "null", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(v))
        {
            value = null;
            return true;
        }

        if (Guid.TryParse(v, out var parsed))
        {
            value = parsed;
            return true;
        }

        return false;
    }

    private static bool TryGetDateTime(Dictionary<string, string> map, string key, out DateTime value)
    {
        value = default;
        if (!map.TryGetValue(key, out var raw))
        {
            return false;
        }

        return DateTime.TryParse(
            UnquoteYamlString(raw),
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out value);
    }

    private static bool TryGetInt(Dictionary<string, string> map, string key, out int value)
    {
        value = 0;
        return map.TryGetValue(key, out var raw) &&
               int.TryParse(UnquoteYamlString(raw), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private void EnsureDefaultProject()
    {
        if (_projects.Count > 0)
        {
            return;
        }

        _projects.Add(new Project
        {
            Name = _initialProjectName,
            CreatedAt = DateTime.UtcNow,
        });
    }

    private void EnsureColumnsForProjects()
    {
        foreach (var project in _projects)
        {
            if (_columns.Any(c => c.ProjectId == project.Id))
            {
                continue;
            }

            CreateDefaultColumns(project.Id);
        }

        if (_notes.Count == 0)
        {
            return;
        }

        foreach (var project in _projects)
        {
            ReindexOrders(project.Id);
        }
    }

    private List<KanbanColumn> CreateDefaultColumns(Guid projectId)
    {
        var existing = _columns.Where(c => c.ProjectId == projectId).ToList();
        if (existing.Count > 0)
        {
            return existing;
        }

        var columns = new List<KanbanColumn>
        {
            new() { ProjectId = projectId, Title = "Ideas", Order = 0 },
            new() { ProjectId = projectId, Title = "In Progress", Order = 1 },
            new() { ProjectId = projectId, Title = "Done", Order = 2 },
        };

        _columns.AddRange(columns);
        return columns;
    }

    private KanbanColumn GetOrCreateFirstColumn(Guid projectId)
    {
        var columns = _columns.Where(c => c.ProjectId == projectId).OrderBy(c => c.Order).ToList();
        if (columns.Count > 0)
        {
            return columns[0];
        }

        return CreateColumn(projectId);
    }

    private void ReindexOrders(Guid projectId)
    {
        foreach (var column in _columns.Where(c => c.ProjectId == projectId))
        {
            var ordered = _notes
                .Where(n => n.ProjectId == projectId && n.ColumnId == column.Id)
                .OrderBy(n => n.Order)
                .ThenByDescending(n => n.UpdatedAt)
                .ToList();

            for (var i = 0; i < ordered.Count; i++)
            {
                ordered[i].Order = i;
            }
        }
    }

    private string NextProjectName()
    {
        const string baseTitle = "New Project";
        if (_projects.All(p => !string.Equals(p.Name, baseTitle, StringComparison.OrdinalIgnoreCase)))
        {
            return baseTitle;
        }

        var index = 2;
        while (_projects.Any(p => string.Equals(p.Name, $"{baseTitle} {index}", StringComparison.OrdinalIgnoreCase)))
        {
            index++;
        }

        return $"{baseTitle} {index}";
    }

    private string NextColumnTitle(Guid projectId)
    {
        const string baseTitle = "New Column";
        var projectColumns = _columns.Where(c => c.ProjectId == projectId).ToList();
        if (projectColumns.All(c => !string.Equals(c.Title, baseTitle, StringComparison.OrdinalIgnoreCase)))
        {
            return baseTitle;
        }

        var index = 2;
        while (projectColumns.Any(c => string.Equals(c.Title, $"{baseTitle} {index}", StringComparison.OrdinalIgnoreCase)))
        {
            index++;
        }

        return $"{baseTitle} {index}";
    }

    private int NextColumnOrder(Guid projectId)
    {
        return NextOrder(_columns.Where(c => c.ProjectId == projectId).Select(c => c.Order));
    }

    private int NextNoteOrder(Guid columnId)
    {
        return NextOrder(_notes.Where(n => n.ColumnId == columnId).Select(n => n.Order));
    }

    private int NextCardOrder(Guid columnId)
    {
        return NextOrder(_cards.Where(c => c.ColumnId == columnId).Select(c => c.Order));
    }

    private void PersistNoteChange(Note note)
    {
        if (_useSplitStore)
        {
            PersistNoteFileIfSplit(note);
            return;
        }

        PersistLegacy();
    }

    private void ResetInMemoryData()
    {
        _projects = [];
        _columns = [];
        _folders = [];
        _notes = [];
        _cards = [];
        _lastOpenedNoteByProjectId = [];
    }

    private static int NextOrder(IEnumerable<int> orders)
    {
        var hasValue = false;
        var max = 0;
        foreach (var order in orders)
        {
            if (!hasValue || order > max)
            {
                max = order;
                hasValue = true;
            }
        }

        return hasValue ? max + 1 : 0;
    }

    private static TData? TryReadJson<TData>(string path)
        where TData : class
    {
        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<TData>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private static void WriteJson<TData>(string path, TData data)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(data, JsonOptions);
        File.WriteAllText(path, json);
    }

    private string NextUntitledTitle(Guid projectId)
    {
        const string baseTitle = "Untitled";
        var projectNotes = _notes.Where(n => n.ProjectId == projectId).ToList();
        if (projectNotes.All(n => !string.Equals(n.Title, baseTitle, StringComparison.OrdinalIgnoreCase)))
        {
            return baseTitle;
        }

        var index = 2;
        while (projectNotes.Any(n => string.Equals(n.Title, $"{baseTitle} {index}", StringComparison.OrdinalIgnoreCase)))
        {
            index++;
        }

        return $"{baseTitle} {index}";
    }
}
