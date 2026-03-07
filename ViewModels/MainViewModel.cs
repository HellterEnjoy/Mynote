using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Input;
using Avalonia;
using Avalonia.Styling;
using Avalonia.Threading;
using Mynote.Models;
using Mynote.Services;

namespace MyAvaloniaApp.ViewModels;

public sealed class MainViewModel : ViewModelBase
{
    private readonly NoteStore _store;
    private readonly AppSettingsStore? _appSettings;
    private Project? _selectedProject;
    private bool _isRefreshingFilteredNotes;

    public ObservableCollection<Project> Projects { get; }
    public ObservableCollection<ColumnViewModel> Columns { get; }
    public ObservableCollection<NoteViewModel> Notes { get; }
    public ObservableCollection<NoteViewModel> FilteredNotes { get; }
    public ObservableCollection<NoteViewModel> LinkedNotes { get; }
    public ObservableCollection<NoteViewModel> Backlinks { get; }
    public ObservableCollection<Folder> Folders { get; }
    public ObservableCollection<FolderFilterItem> FolderFilters { get; }
    public ObservableCollection<TagFilterItem> TagFilters { get; }

    /// <summary>
    /// Used by ComboBoxes to offer target folders.
    /// </summary>
    public IReadOnlyList<Folder> FolderChoices { get; private set; } = Array.Empty<Folder>();

    private string _linkedNotesFooter = string.Empty;
    public string LinkedNotesFooter
    {
        get => _linkedNotesFooter;
        private set => Set(ref _linkedNotesFooter, value);
    }

    /// <summary>
    /// Used by the ComboBox in Note cards to offer target columns.
    /// </summary>
    public IReadOnlyList<KanbanColumn> ColumnChoices { get; private set; } = Array.Empty<KanbanColumn>();

    public ICommand AddProjectCommand { get; }
    public ICommand AddColumnCommand { get; }
    public ICommand AddNoteCommand { get; }
    public ICommand AddFolderCommand { get; }
    public ICommand ClearSelectedNoteFolderCommand { get; }
    public ICommand CloseSelectedNoteCommand { get; }
    public ICommand SaveSelectedNoteCommand { get; }
    public ICommand DeleteSelectedNoteCommand { get; }
    public ICommand OpenNoteCommand { get; }
    public ICommand DeleteNoteCommand { get; }
    public ICommand OpenKanbanCardCommand { get; }
    public ICommand DeleteKanbanCardCommand { get; }
    public ICommand ShowEditorCommand { get; }
    public ICommand ShowKanbanCommand { get; }
    public ICommand PinSelectedNoteToKanbanCommand { get; }
    public ICommand DeleteFolderFilterCommand { get; }
    public ICommand RenameSelectedFolderCommand { get; }
    public ICommand DeleteSelectedFolderCommand { get; }
    public ICommand ClearTagFilterCommand { get; }

    private readonly DispatcherTimer _derivedRefreshTimer;
    private bool _derivedRefreshPending;
    private readonly DispatcherTimer _autoSaveTimer;

    private bool _isDarkTheme;
    public bool IsDarkTheme
    {
        get => _isDarkTheme;
        set
        {
            if (!Set(ref _isDarkTheme, value))
            {
                return;
            }

            ApplyThemeVariant();
            _appSettings?.SetTheme(value);
        }
    }

    private bool _saveOnBlur;
    public bool SaveOnBlur
    {
        get => _saveOnBlur;
        set
        {
            if (!Set(ref _saveOnBlur, value))
            {
                return;
            }

            PersistAutoSaveSettings();
        }
    }

    private bool _saveOnClose;
    public bool SaveOnClose
    {
        get => _saveOnClose;
        set
        {
            if (!Set(ref _saveOnClose, value))
            {
                return;
            }

            PersistAutoSaveSettings();
        }
    }

    private int _autoSaveIntervalSeconds;
    public int AutoSaveIntervalSeconds
    {
        get => _autoSaveIntervalSeconds;
        set
        {
            var normalized = Math.Max(0, value);
            if (!Set(ref _autoSaveIntervalSeconds, normalized))
            {
                return;
            }

            UpdateAutoSaveTimer();
            PersistAutoSaveSettings();
        }
    }

    private string _newFolderName = "New Folder";
    public string NewFolderName
    {
        get => _newFolderName;
        set => Set(ref _newFolderName, value);
    }

    private string _searchQuery = string.Empty;
    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            if (!Set(ref _searchQuery, value))
            {
                return;
            }

            RefreshFilteredNotes();
        }
    }

    private string _selectedNoteLastEdited = string.Empty;
    public string SelectedNoteLastEdited
    {
        get => _selectedNoteLastEdited;
        private set => Set(ref _selectedNoteLastEdited, value);
    }

    private string? _selectedNoteBaselineTitle;
    private string? _selectedNoteBaselineContent;
    private Guid? _selectedNoteBaselineFolderId;

    private bool _hasUnsavedChanges;
    public bool HasUnsavedChanges
    {
        get => _hasUnsavedChanges;
        private set => Set(ref _hasUnsavedChanges, value);
    }

    private FolderFilterItem? _selectedFolderFilter;
    public FolderFilterItem? SelectedFolderFilter
    {
        get => _selectedFolderFilter;
        set
        {
            if (!Set(ref _selectedFolderFilter, value))
            {
                return;
            }

            UpdateSelectedFolderState();
            RefreshFilteredNotes();
            RaiseCommandCanExecute();
        }
    }

    private TagFilterItem? _selectedTagFilter;
    public TagFilterItem? SelectedTagFilter
    {
        get => _selectedTagFilter;
        set
        {
            if (!Set(ref _selectedTagFilter, value))
            {
                return;
            }

            RefreshFilteredNotes();
            RaiseCommandCanExecute();
        }
    }

    private bool _isSpecificFolderSelected;
    public bool IsSpecificFolderSelected
    {
        get => _isSpecificFolderSelected;
        private set => Set(ref _isSpecificFolderSelected, value);
    }

    private string _selectedFolderName = string.Empty;
    public string SelectedFolderName
    {
        get => _selectedFolderName;
        set => Set(ref _selectedFolderName, value);
    }

    private bool _syncingSelectedFolder;
    private Folder? _selectedNoteFolder;
    public Folder? SelectedNoteFolder
    {
        get => _selectedNoteFolder;
        set
        {
            if (!Set(ref _selectedNoteFolder, value))
            {
                return;
            }

            if (_syncingSelectedFolder || SelectedNote is null)
            {
                return;
            }

            SelectedNote.FolderId = value?.Id;
        }
    }

    private NoteViewModel? _selectedNote;
    public NoteViewModel? SelectedNote
    {
        get => _selectedNote;
        set
        {
            if (_isRefreshingFilteredNotes && value is null)
            {
                // Avoid ListBox clearing selection mid-refresh from closing the editor.
                return;
            }

            if (value is not null && Notes.Count > 0 && !Notes.Contains(value))
            {
                // Notes coming from the Kanban column lists are separate instances; map them to the main list.
                var canonical = Notes.FirstOrDefault(n => n.Id == value.Id);
                if (canonical is not null)
                {
                    value = canonical;
                }
            }

            if (ReferenceEquals(_selectedNote, value))
            {
                return;
            }

            if (_selectedNote is not null)
            {
                _selectedNote.PropertyChanged -= SelectedNoteOnPropertyChanged;
            }

            Set(ref _selectedNote, value);
            Raise(nameof(HasSelectedNote));
            Raise(nameof(HasNoSelectedNote));

            if (_selectedNote is not null)
            {
                _selectedNote.PropertyChanged += SelectedNoteOnPropertyChanged;
            }

            RefreshLinkedNotes();
            RequestDerivedRefresh();
            SyncSelectedNoteFolder();
            UpdateSelectedNoteLastEdited();
            ResetUnsavedBaseline();
            UpdateUnsavedChanges();

            if (_selectedNote is not null && SelectedProject is not null)
            {
                // Persist selection so next launch restores the last opened note in this project.
                _store.SetLastOpenedNoteId(SelectedProject.Id, _selectedNote.Id);
            }

            RaiseCommandCanExecute();
        }
    }

    public bool HasSelectedNote => SelectedNote is not null;
    public bool HasNoSelectedNote => SelectedNote is null;

    private bool _isEditorSelected = true;
    public bool IsEditorSelected
    {
        get => _isEditorSelected;
        set
        {
            if (!Set(ref _isEditorSelected, value))
            {
                return;
            }

            if (value)
            {
                IsKanbanSelected = false;
            }
        }
    }

    private bool _isKanbanSelected;
    public bool IsKanbanSelected
    {
        get => _isKanbanSelected;
        set
        {
            if (!Set(ref _isKanbanSelected, value))
            {
                return;
            }

            IsEditorSelected = !value;
        }
    }

    public Project? SelectedProject
    {
        get => _selectedProject;
        set
        {
            if (!Set(ref _selectedProject, value))
            {
                return;
            }

            LoadColumns();
            RaiseCommandCanExecute();
        }
    }

    public MainViewModel(NoteStore store, AppSettingsStore? appSettingsStore = null)
    {
        _store = store;
        _appSettings = appSettingsStore;
        Projects = new ObservableCollection<Project>(_store.GetProjects());
        Columns = new ObservableCollection<ColumnViewModel>();
        Notes = new ObservableCollection<NoteViewModel>();
        FilteredNotes = new ObservableCollection<NoteViewModel>();
        LinkedNotes = new ObservableCollection<NoteViewModel>();
        Backlinks = new ObservableCollection<NoteViewModel>();
        Folders = new ObservableCollection<Folder>();
        FolderFilters = new ObservableCollection<FolderFilterItem>();
        TagFilters = new ObservableCollection<TagFilterItem>();

        AddProjectCommand = new RelayCommand(AddProject);
        AddColumnCommand = new RelayCommand(AddColumn, () => SelectedProject is not null);
        AddNoteCommand = new RelayCommand(AddNote, () => SelectedProject is not null);
        AddFolderCommand = new RelayCommand(AddFolder, () => SelectedProject is not null);
        ClearSelectedNoteFolderCommand = new RelayCommand(() => SelectedNoteFolder = null, () => SelectedNote is not null);
        CloseSelectedNoteCommand = new RelayCommand(CloseSelectedNote, () => SelectedNote is not null);
        SaveSelectedNoteCommand = new RelayCommand(SaveSelectedNote, () => SelectedProject is not null && SelectedNote is not null && HasUnsavedChanges);
        DeleteSelectedNoteCommand = new RelayCommand(DeleteSelectedNote, () => SelectedProject is not null && SelectedNote is not null);
        OpenNoteCommand = new RelayCommand(OpenNote, p => p is NoteViewModel);
        DeleteNoteCommand = new RelayCommand(DeleteNote, p => p is NoteViewModel);
        OpenKanbanCardCommand = new RelayCommand(OpenKanbanCard, p => p is KanbanCardViewModel);
        DeleteKanbanCardCommand = new RelayCommand(DeleteKanbanCard, p => p is KanbanCardViewModel);
        DeleteFolderFilterCommand = new RelayCommand(DeleteFolderFilter, p => p is FolderFilterItem f && f.FolderId.HasValue && !f.IsUnfiled);
        RenameSelectedFolderCommand = new RelayCommand(_ => RenameSelectedFolder(), _ => CanManageSelectedFolder());
        DeleteSelectedFolderCommand = new RelayCommand(_ => DeleteSelectedFolder(), _ => CanManageSelectedFolder());
        ClearTagFilterCommand = new RelayCommand(ClearTagFilter, () => SelectedTagFilter is not null && !SelectedTagFilter.IsAll);

        ShowEditorCommand = new RelayCommand(() => IsEditorSelected = true);
        ShowKanbanCommand = new RelayCommand(() => IsKanbanSelected = true);
        PinSelectedNoteToKanbanCommand = new RelayCommand(PinSelectedNoteToKanban, () => SelectedProject is not null && SelectedNote is not null);

        if (_appSettings is not null)
        {
            // Initialize theme from persisted settings.
            var settings = _appSettings.Load();
            _isDarkTheme = settings.IsDarkTheme;
            _saveOnBlur = settings.SaveOnBlur;
            _saveOnClose = settings.SaveOnClose;
            _autoSaveIntervalSeconds = settings.AutoSaveIntervalSeconds;
        }

        ApplyThemeVariant();

        _derivedRefreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(250),
        };
        _derivedRefreshTimer.Tick += (_, __) =>
        {
            _derivedRefreshTimer.Stop();
            if (!_derivedRefreshPending)
            {
                return;
            }

            _derivedRefreshPending = false;
            RefreshTags();
            RefreshBacklinks();
            RefreshFilteredNotes();
        };

        _autoSaveTimer = new DispatcherTimer();
        _autoSaveTimer.Tick += (_, __) =>
        {
            if (AutoSaveIntervalSeconds <= 0)
            {
                return;
            }

            if (SelectedProject is null || SelectedNote is null || !HasUnsavedChanges)
            {
                return;
            }

            SaveSelectedNote();
        };
        UpdateAutoSaveTimer();

        SelectedProject = Projects.FirstOrDefault();
    }

    private void ApplyThemeVariant()
    {
        // Keep it simple: one global theme for the whole app.
        if (Application.Current is null)
        {
            return;
        }

        Application.Current.RequestedThemeVariant = IsDarkTheme ? ThemeVariant.Dark : ThemeVariant.Light;
    }

    private void CloseSelectedNote()
    {
        SelectedNote = null;
    }

    private void OpenNote(object? parameter)
    {
        if (parameter is not NoteViewModel note)
        {
            return;
        }

        SelectedNote = note;
        if (IsKanbanSelected)
        {
            IsEditorSelected = true;
        }
    }

    private void DeleteNote(object? parameter)
    {
        if (parameter is not NoteViewModel note)
        {
            return;
        }

        SelectedNote = note;
        DeleteSelectedNote();
    }

    private void OpenKanbanCard(object? parameter)
    {
        if (SelectedProject is null || parameter is not KanbanCardViewModel card)
        {
            return;
        }

        if (card.LinkedNoteId is Guid linkedId)
        {
            var existing = Notes.FirstOrDefault(n => n.Id == linkedId);
            if (existing is not null)
            {
                SelectedNote = existing;
                IsEditorSelected = true;
                return;
            }

            // Linked note no longer exists.
            card.LinkedNoteId = null;
        }

        // Create a full note for the card (optional workflow).
        var created = _store.CreateNote(SelectedProject.Id);
        created.Title = string.IsNullOrWhiteSpace(card.Title) ? created.Title : card.Title.Trim();
        _store.UpdateNote(created);

        var vm = new NoteViewModel(created, SelectedProject.Id, _store, () => ColumnChoices, () => FolderChoices);
        Notes.Add(vm);
        SelectedNote = vm;
        IsEditorSelected = true;
        RefreshFilteredNotes();

        card.LinkedNoteId = created.Id;
    }

    private void DeleteKanbanCard(object? parameter)
    {
        if (parameter is not KanbanCardViewModel card)
        {
            return;
        }

        _store.DeleteCard(card.Id);

        foreach (var column in Columns)
        {
            var match = column.Cards.FirstOrDefault(c => c.Id == card.Id);
            if (match is null)
            {
                continue;
            }

            column.Cards.Remove(match);
            break;
        }
    }

    private void PinSelectedNoteToKanban()
    {
        if (SelectedProject is null || SelectedNote is null)
        {
            return;
        }

        var existing = _store.GetCards(SelectedProject.Id).FirstOrDefault(c => c.LinkedNoteId == SelectedNote.Id);
        if (existing is not null)
        {
            IsKanbanSelected = true;
            return;
        }

        var columns = _store.GetColumns(SelectedProject.Id).OrderBy(c => c.Order).ToList();
        var targetColumn = columns.FirstOrDefault();
        if (targetColumn is null)
        {
            return;
        }

        var card = _store.CreateCard(SelectedProject.Id, targetColumn.Id);
        card.Title = SelectedNote.Title ?? "Untitled";
        card.LinkedNoteId = SelectedNote.Id;
        card.UpdatedAt = DateTime.UtcNow;
        _store.UpdateCard(card);

        var columnVm = Columns.FirstOrDefault(c => c.Id == targetColumn.Id);
        columnVm?.Cards.Add(new KanbanCardViewModel(card, _store));

        IsKanbanSelected = true;
    }

    private void AddProject()
    {
        var project = _store.CreateProject();
        Projects.Add(project);
        SelectedProject = project;
    }

    private void AddColumn()
    {
        if (SelectedProject is null)
        {
            return;
        }

        var column = _store.CreateColumn(SelectedProject.Id);
        ColumnChoices = ColumnChoices.Concat(new[] { column }).ToList();
        Raise(nameof(ColumnChoices));

        Columns.Add(new ColumnViewModel(
            column,
            SelectedProject.Id,
            _store,
            Enumerable.Empty<KanbanCard>(),
            ReloadFromStore));
    }

    private void AddFolder()
    {
        if (SelectedProject is null)
        {
            return;
        }

        var folder = _store.CreateFolder(SelectedProject.Id, NewFolderName);
        RefreshFolders();

        var filter = FolderFilters.FirstOrDefault(f => f.FolderId == folder.Id);
        if (filter is not null)
        {
            SelectedFolderFilter = filter;
        }
    }

    private void AddNote()
    {
        if (SelectedProject is null)
        {
            return;
        }

        var note = CreateNoteForCurrentFilter(SelectedProject.Id);

        var vm = new NoteViewModel(note, SelectedProject.Id, _store, () => ColumnChoices, () => FolderChoices);
        AttachNoteHandlers(vm);
        Notes.Add(vm);
        SelectedNote = vm;
        IsEditorSelected = true;

        RefreshFilteredNotes();
        RequestDerivedRefresh();
    }

    private Note CreateNoteForCurrentFilter(Guid projectId)
    {
        var note = _store.CreateNote(projectId);

        // Apply current folder filter to new notes for faster "sort into folders" workflow.
        if (SelectedFolderFilter?.FolderId is Guid folderId && !SelectedFolderFilter.IsUnfiled)
        {
            note.FolderId = folderId;
            _store.UpdateNote(note);
        }

        return note;
    }

    private void SaveSelectedNote()
    {
        if (SelectedProject is null || SelectedNote is null)
        {
            return;
        }

        var existing = _store.GetNotes(SelectedProject.Id).FirstOrDefault(n => n.Id == SelectedNote.Id);
        _store.UpdateNote(new Note
        {
            Id = SelectedNote.Id,
            ProjectId = SelectedProject.Id,
            ColumnId = SelectedNote.ColumnId,
            FolderId = SelectedNote.FolderId,
            Title = SelectedNote.Title,
            Content = SelectedNote.Content,
            Order = existing?.Order ?? 0,
            UpdatedAt = DateTime.UtcNow
        });

        // Ensure any title normalization (Untitled/trim) is reflected in UI.
        var refreshed = _store.GetNotes(SelectedProject.Id).FirstOrDefault(n => n.Id == SelectedNote.Id);
        if (refreshed is not null && !string.Equals(SelectedNote.Title, refreshed.Title, StringComparison.Ordinal))
        {
            SelectedNote.Title = refreshed.Title;
        }

        RefreshLinkedNotes();
        RequestDerivedRefresh();
        RefreshFilteredNotes();
        UpdateSelectedNoteLastEdited();
        ResetUnsavedBaseline();
        UpdateUnsavedChanges();
    }

    private void DeleteSelectedNote()
    {
        if (SelectedProject is null || SelectedNote is null)
        {
            return;
        }

        var deleting = SelectedNote;
        DetachNoteHandlers(deleting);
        _store.DeleteNote(new Note { Id = deleting.Id });

        var index = Notes.IndexOf(deleting);
        if (index >= 0)
        {
            Notes.RemoveAt(index);
        }

        SelectedNote = Notes.Count == 0
            ? null
            : Notes[Math.Clamp(index, 0, Notes.Count - 1)];

        if (Notes.Count == 0)
        {
            // Avoid leaving the editor in a "fake editable but not saveable" state.
            AddNote();
        }

        RefreshLinkedNotes();
        RequestDerivedRefresh();
        RefreshFilteredNotes();
    }

    private void LoadColumns()
    {
        Columns.Clear();
        foreach (var note in Notes)
        {
            DetachNoteHandlers(note);
        }
        Notes.Clear();
        FilteredNotes.Clear();
        LinkedNotes.Clear();
        Backlinks.Clear();
        Folders.Clear();
        FolderFilters.Clear();
        TagFilters.Clear();
        SelectedFolderFilter = null;
        SelectedTagFilter = null;

        if (SelectedProject is null)
        {
            ColumnChoices = Array.Empty<KanbanColumn>();
            Raise(nameof(ColumnChoices));
            FolderChoices = Array.Empty<Folder>();
            Raise(nameof(FolderChoices));
            return;
        }

        var projectId = SelectedProject.Id;
        var columns = _store.GetColumns(projectId).OrderBy(c => c.Order).ToList();
        ColumnChoices = columns;
        Raise(nameof(ColumnChoices));

        var folders = _store.GetFolders(projectId).ToList();
        FolderChoices = folders;
        Raise(nameof(FolderChoices));
        foreach (var folder in folders)
        {
            Folders.Add(folder);
        }
        BuildFolderFilters();

        var notes = _store.GetNotes(projectId).OrderBy(n => n.Order).ThenByDescending(n => n.UpdatedAt).ToList();
        if (notes.Count == 0)
        {
            // Ensure there's always a real persisted note to edit (prevents "fake unsaveable template" editing).
            var created = CreateNoteForCurrentFilter(projectId);
            notes.Add(created);
        }
        foreach (var note in notes)
        {
            var vm = new NoteViewModel(note, projectId, _store, () => ColumnChoices, () => FolderChoices);
            AttachNoteHandlers(vm);
            Notes.Add(vm);
        }

        var lastOpenedId = _store.GetLastOpenedNoteId(projectId);
        SelectedNote = lastOpenedId is Guid id
            ? Notes.FirstOrDefault(n => n.Id == id) ?? Notes.FirstOrDefault()
            : Notes.FirstOrDefault();

        var cards = _store.GetCards(projectId).OrderBy(c => c.Order).ThenByDescending(c => c.UpdatedAt).ToList();
        foreach (var column in columns)
        {
            var columnCards = cards.Where(c => c.ColumnId == column.Id);
            Columns.Add(new ColumnViewModel(column, projectId, _store, columnCards, ReloadFromStore));
        }

        RefreshLinkedNotes();
        RefreshTags();
        RefreshBacklinks();
        RefreshFilteredNotes();
        RaiseCommandCanExecute();
    }

    private void SelectedNoteOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // When content changes, recompute the linked notes list.
        if (e.PropertyName is nameof(NoteViewModel.Content) or nameof(NoteViewModel.Title))
        {
            RefreshLinkedNotes();
            UpdateUnsavedChanges();
            RequestDerivedRefresh();
        }

        if (e.PropertyName is nameof(NoteViewModel.FolderId))
        {
            SyncSelectedNoteFolder();
            RefreshFilteredNotes();
            UpdateUnsavedChanges();
        }
    }

    private static readonly Regex WikiLinkRegex = new(@"\[\[(?<title>.+?)\]\]", RegexOptions.Compiled);
    private static readonly Regex TagRegex = new(@"(?<!\w)#(?<tag>[\p{L}\p{N}_-]+)", RegexOptions.Compiled);

    private void RefreshLinkedNotes()
    {
        LinkedNotes.Clear();
        LinkedNotesFooter = string.Empty;

        if (SelectedNote is null)
        {
            return;
        }

        var titles = WikiLinkRegex.Matches(SelectedNote.Content ?? string.Empty)
            .Select(m => m.Groups["title"].Value.Trim())
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (titles.Count == 0)
        {
            return;
        }

        foreach (var title in titles)
        {
            var match = Notes.FirstOrDefault(n =>
                n.Id != SelectedNote.Id &&
                string.Equals(n.Title, title, StringComparison.OrdinalIgnoreCase));

            if (match is not null)
            {
                LinkedNotes.Add(match);
            }
        }

        if (LinkedNotes.Count > 0)
        {
            LinkedNotesFooter = "Related to " + string.Join(" and ", LinkedNotes.Select(n => $"[[{n.Title}]]")) + ".";
        }
    }

    private void RefreshBacklinks()
    {
        Backlinks.Clear();
        if (SelectedNote is null)
        {
            return;
        }

        var selectedTitle = (SelectedNote.Title ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(selectedTitle))
        {
            return;
        }

        foreach (var note in Notes)
        {
            if (note.Id == SelectedNote.Id)
            {
                continue;
            }

            var links = WikiLinkRegex.Matches(note.Content ?? string.Empty)
                .Select(m => m.Groups["title"].Value.Trim())
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Distinct(StringComparer.OrdinalIgnoreCase);

            if (links.Contains(selectedTitle, StringComparer.OrdinalIgnoreCase))
            {
                Backlinks.Add(note);
            }
        }
    }

    private void RefreshTags()
    {
        var selected = SelectedTagFilter?.TagName;

        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var note in Notes)
        {
            var perNote = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match match in TagRegex.Matches(note.Content ?? string.Empty))
            {
                var tag = match.Groups["tag"].Value.Trim();
                if (!string.IsNullOrWhiteSpace(tag))
                {
                    perNote.Add(tag);
                }
            }

            foreach (var tag in perNote)
            {
                counts[tag] = counts.TryGetValue(tag, out var current) ? current + 1 : 1;
            }
        }

        TagFilters.Clear();
        TagFilters.Add(new TagFilterItem("All tags", null, 0, isAll: true));

        foreach (var tag in counts.Keys.OrderBy(t => t, StringComparer.OrdinalIgnoreCase))
        {
            TagFilters.Add(new TagFilterItem($"#{tag} ({counts[tag]})", tag, counts[tag]));
        }

        if (selected is null)
        {
            SelectedTagFilter ??= TagFilters.FirstOrDefault();
            return;
        }

        SelectedTagFilter = TagFilters.FirstOrDefault(t =>
            !t.IsAll &&
            string.Equals(t.TagName, selected, StringComparison.OrdinalIgnoreCase)) ?? TagFilters.FirstOrDefault();
    }

    private void ClearTagFilter()
    {
        SelectedTagFilter = TagFilters.FirstOrDefault();
        SearchQuery = string.Empty;
    }

    public void ApplyTagFilter(string tagName)
    {
        if (string.IsNullOrWhiteSpace(tagName))
        {
            ClearTagFilter();
            return;
        }

        var normalized = tagName.Trim().TrimStart('#');
        RefreshTags();
        SelectedTagFilter = TagFilters.FirstOrDefault(t =>
            !t.IsAll &&
            string.Equals(t.TagName, normalized, StringComparison.OrdinalIgnoreCase)) ?? TagFilters.FirstOrDefault();

        SearchQuery = string.Empty;
    }

    private void AttachNoteHandlers(NoteViewModel note)
    {
        note.PropertyChanged += AnyNoteOnPropertyChanged;
    }

    private void DetachNoteHandlers(NoteViewModel note)
    {
        note.PropertyChanged -= AnyNoteOnPropertyChanged;
    }

    private void AnyNoteOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(NoteViewModel.Content) or nameof(NoteViewModel.Title))
        {
            RequestDerivedRefresh();
        }
    }

    private void RequestDerivedRefresh()
    {
        _derivedRefreshPending = true;
        _derivedRefreshTimer.Stop();
        _derivedRefreshTimer.Start();
    }

    private void UpdateAutoSaveTimer()
    {
        if (AutoSaveIntervalSeconds <= 0)
        {
            _autoSaveTimer.Stop();
            return;
        }

        _autoSaveTimer.Interval = TimeSpan.FromSeconds(Math.Max(1, AutoSaveIntervalSeconds));
        if (!_autoSaveTimer.IsEnabled)
        {
            _autoSaveTimer.Start();
        }
    }

    private void PersistAutoSaveSettings()
    {
        _appSettings?.SetAutoSaveSettings(SaveOnBlur, SaveOnClose, AutoSaveIntervalSeconds);
    }

    private void RaiseCommandCanExecute()
    {
        ((RelayCommand)AddColumnCommand).RaiseCanExecuteChanged();
        ((RelayCommand)AddNoteCommand).RaiseCanExecuteChanged();
        ((RelayCommand)AddFolderCommand).RaiseCanExecuteChanged();
        ((RelayCommand)ClearSelectedNoteFolderCommand).RaiseCanExecuteChanged();
        ((RelayCommand)CloseSelectedNoteCommand).RaiseCanExecuteChanged();
        ((RelayCommand)SaveSelectedNoteCommand).RaiseCanExecuteChanged();
        ((RelayCommand)DeleteSelectedNoteCommand).RaiseCanExecuteChanged();
        ((RelayCommand)PinSelectedNoteToKanbanCommand).RaiseCanExecuteChanged();
        ((RelayCommand)DeleteFolderFilterCommand).RaiseCanExecuteChanged();
        ((RelayCommand)RenameSelectedFolderCommand).RaiseCanExecuteChanged();
        ((RelayCommand)DeleteSelectedFolderCommand).RaiseCanExecuteChanged();
        ((RelayCommand)ClearTagFilterCommand).RaiseCanExecuteChanged();
    }

    private void UpdateSelectedFolderState()
    {
        var folder = GetSelectedFolder();
        IsSpecificFolderSelected = folder is not null;
        SelectedFolderName = folder?.Name ?? string.Empty;
    }

    private Folder? GetSelectedFolder()
    {
        if (SelectedFolderFilter?.IsUnfiled == true)
        {
            return null;
        }

        return SelectedFolderFilter?.FolderId is Guid id
            ? FolderChoices.FirstOrDefault(f => f.Id == id)
            : null;
    }

    private bool CanManageSelectedFolder() => GetSelectedFolder() is not null && SelectedProject is not null;

    private void RenameSelectedFolder()
    {
        var folder = GetSelectedFolder();
        if (folder is null)
        {
            return;
        }

        _store.RenameFolder(folder.Id, SelectedFolderName);
        RefreshFolders();

        var filter = FolderFilters.FirstOrDefault(f => f.FolderId == folder.Id);
        if (filter is not null)
        {
            SelectedFolderFilter = filter;
        }
    }

    private void DeleteSelectedFolder()
    {
        var folder = GetSelectedFolder();
        if (folder is null)
        {
            return;
        }

        _store.DeleteFolder(folder.Id);

        // Keep in-memory VMs consistent with the store.
        foreach (var note in Notes.Where(n => n.FolderId == folder.Id))
        {
            note.FolderId = null;
        }

        RefreshFolders();
        SelectedFolderFilter = FolderFilters.FirstOrDefault(); // All Notes
    }

    private void DeleteFolderFilter(object? parameter)
    {
        if (SelectedProject is null || parameter is not FolderFilterItem filter)
        {
            return;
        }

        if (!filter.FolderId.HasValue || filter.IsUnfiled)
        {
            return;
        }

        var folderId = filter.FolderId.Value;
        _store.DeleteFolder(folderId);

        foreach (var note in Notes.Where(n => n.FolderId == folderId))
        {
            note.FolderId = null;
        }

        RefreshFolders();
        SelectedFolderFilter = FolderFilters.FirstOrDefault();
    }

    public void MoveNoteToFolder(Guid noteId, Guid? folderId)
    {
        if (SelectedProject is null)
        {
            return;
        }

        var noteVm = Notes.FirstOrDefault(n => n.Id == noteId);
        if (noteVm is null)
        {
            return;
        }

        noteVm.FolderId = folderId;
        if (SelectedNote?.Id == noteId)
        {
            SyncSelectedNoteFolder();
        }

        var existing = _store.GetNotes(SelectedProject.Id).FirstOrDefault(n => n.Id == noteId);
        if (existing is null)
        {
            return;
        }

        _store.UpdateNote(new Note
        {
            Id = existing.Id,
            ProjectId = existing.ProjectId,
            ColumnId = existing.ColumnId,
            FolderId = folderId,
            Title = existing.Title,
            Content = existing.Content,
            Order = existing.Order,
            UpdatedAt = existing.UpdatedAt
        });

        if (SelectedNote?.Id == noteId)
        {
            ResetUnsavedBaseline();
            UpdateUnsavedChanges();
        }

        RefreshFilteredNotes();
    }

    public void MoveCardToColumn(Guid cardId, Guid columnId)
    {
        if (SelectedProject is null)
        {
            return;
        }

        var sourceColumn = Columns.FirstOrDefault(c => c.Cards.Any(card => card.Id == cardId));
        var targetColumn = Columns.FirstOrDefault(c => c.Id == columnId);
        if (targetColumn is null)
        {
            return;
        }

        if (sourceColumn?.Id == targetColumn.Id)
        {
            return;
        }

        var cardVm = sourceColumn?.Cards.FirstOrDefault(c => c.Id == cardId);
        if (cardVm is null)
        {
            return;
        }

        _store.MoveCard(cardId, columnId, beforeCardId: null);

        sourceColumn!.Cards.Remove(cardVm);
        targetColumn.Cards.Add(cardVm);
    }

    private void ReloadFromStore()
    {
        if (SelectedProject is null)
        {
            return;
        }

        var selectedNoteId = SelectedNote?.Id;
        var filterSnapshot = SelectedFolderFilter is null
            ? null
            : new FolderFilterItem(SelectedFolderFilter.Name, SelectedFolderFilter.FolderId, SelectedFolderFilter.IsUnfiled);

        LoadColumns();

        if (selectedNoteId is not null)
        {
            SelectedNote = Notes.FirstOrDefault(n => n.Id == selectedNoteId.Value);
        }

        if (filterSnapshot is not null)
        {
            SelectedFolderFilter = FolderFilters.FirstOrDefault(f =>
                f.IsUnfiled == filterSnapshot.IsUnfiled &&
                f.FolderId == filterSnapshot.FolderId) ?? FolderFilters.FirstOrDefault();
        }
    }

    private void RefreshFolders()
    {
        if (SelectedProject is null)
        {
            return;
        }

        var folders = _store.GetFolders(SelectedProject.Id).ToList();
        FolderChoices = folders;
        Raise(nameof(FolderChoices));

        Folders.Clear();
        foreach (var folder in folders)
        {
            Folders.Add(folder);
        }

        BuildFolderFilters();
        SyncSelectedNoteFolder();
        RefreshFilteredNotes();
    }

    private void BuildFolderFilters()
    {
        FolderFilters.Clear();
        FolderFilters.Add(new FolderFilterItem("All Notes", null));
        FolderFilters.Add(new FolderFilterItem("Unfiled", null, isUnfiled: true));
        foreach (var folder in FolderChoices)
        {
            FolderFilters.Add(new FolderFilterItem(folder.Name, folder.Id));
        }

        SelectedFolderFilter ??= FolderFilters.FirstOrDefault();
    }

    private void RefreshFilteredNotes()
    {
        _isRefreshingFilteredNotes = true;
        try
        {
            FilteredNotes.Clear();

            IEnumerable<NoteViewModel> candidates = Notes;

            if (SelectedFolderFilter is null)
            {
                candidates = Notes;
            }
            else if (SelectedFolderFilter.IsUnfiled)
            {
                candidates = Notes.Where(n => n.FolderId is null);
            }
            else if (SelectedFolderFilter.FolderId is Guid folderId)
            {
                candidates = Notes.Where(n => n.FolderId == folderId);
            }

            if (SelectedTagFilter is { IsAll: false, TagName: { } tagName } && !string.IsNullOrWhiteSpace(tagName))
            {
                candidates = candidates.Where(n =>
                    TagRegex.Matches(n.Content ?? string.Empty).Any(m =>
                        string.Equals(m.Groups["tag"].Value, tagName, StringComparison.OrdinalIgnoreCase)));
            }

            var q = (SearchQuery ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(q))
            {
                candidates = candidates.Where(n =>
                    (!string.IsNullOrWhiteSpace(n.Title) && n.Title.Contains(q, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrWhiteSpace(n.Content) && n.Content.Contains(q, StringComparison.OrdinalIgnoreCase)));
            }

            var result = candidates.ToList();
            if (SelectedNote is not null && !result.Contains(SelectedNote))
            {
                // Keep current note visible to avoid the list selection clearing the editor
                // when filters/search don't match the opened note.
                result.Insert(0, SelectedNote);
            }

            foreach (var note in result)
            {
                FilteredNotes.Add(note);
            }
        }
        finally
        {
            _isRefreshingFilteredNotes = false;
        }

        // If ListBox tried to clear selection during refresh, push the current selection back.
        Raise(nameof(SelectedNote));
    }

    private void UpdateSelectedNoteLastEdited()
    {
        if (SelectedProject is null || SelectedNote is null)
        {
            SelectedNoteLastEdited = string.Empty;
            return;
        }

        var note = _store.GetNotes(SelectedProject.Id).FirstOrDefault(n => n.Id == SelectedNote.Id);
        if (note is null)
        {
            SelectedNoteLastEdited = string.Empty;
            return;
        }

        var suffix = HasUnsavedChanges ? " • Unsaved" : string.Empty;
        SelectedNoteLastEdited = $"Last edited {note.UpdatedAt.ToLocalTime():g}{suffix}";
    }

    private void ResetUnsavedBaseline()
    {
        _selectedNoteBaselineTitle = NormalizeText(SelectedNote?.Title);
        _selectedNoteBaselineContent = NormalizeContent(SelectedNote?.Content);
        _selectedNoteBaselineFolderId = SelectedNote?.FolderId;
    }

    private void UpdateUnsavedChanges()
    {
        if (SelectedNote is null)
        {
            HasUnsavedChanges = false;
            return;
        }

        HasUnsavedChanges =
            !string.Equals(_selectedNoteBaselineTitle, NormalizeText(SelectedNote.Title), StringComparison.Ordinal) ||
            !string.Equals(_selectedNoteBaselineContent, NormalizeContent(SelectedNote.Content), StringComparison.Ordinal) ||
            _selectedNoteBaselineFolderId != SelectedNote.FolderId;

        UpdateSelectedNoteLastEdited();
        RaiseCommandCanExecute();
    }

    private void SyncSelectedNoteFolder()
    {
        _syncingSelectedFolder = true;
        try
        {
            SelectedNoteFolder = SelectedNote?.FolderId is Guid folderId
                ? FolderChoices.FirstOrDefault(f => f.Id == folderId)
                : null;
        }
        finally
        {
            _syncingSelectedFolder = false;
        }
    }

    private static string NormalizeText(string? value) => value ?? string.Empty;

    private static string NormalizeContent(string? value)
        => (value ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal);
}
