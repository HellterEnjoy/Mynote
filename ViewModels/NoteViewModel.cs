using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using Mynote.Models;
using Mynote.Services;

namespace MyAvaloniaApp.ViewModels;

public sealed class NoteViewModel : ViewModelBase
{
    private readonly NoteStore _store;
    private readonly Guid _projectId;
    private readonly Func<IReadOnlyList<KanbanColumn>> _getColumns;
    private readonly Func<IReadOnlyList<Folder>> _getFolders;
    private Guid _columnId;
    private Guid? _folderId;
    private string _title;
    private string _content;

    public Guid Id { get; }
    public Guid ColumnId { get => _columnId; set { _columnId = value; Raise(); Raise(nameof(SelectedColumn)); } }
    public Guid? FolderId { get => _folderId; set { _folderId = value; Raise(); Raise(nameof(SelectedFolder)); } }
    public string Title { get => _title; set => Set(ref _title, value); }
    public string Content { get => _content; set => Set(ref _content, value); }

    public KanbanColumn? SelectedColumn
    {
        get => _getColumns().FirstOrDefault(c => c.Id == ColumnId);
        set
        {
            if (value is null || value.Id == ColumnId)
            {
                return;
            }

            ColumnId = value.Id;
        }
    }

    public Folder? SelectedFolder
    {
        get => FolderId is null ? null : _getFolders().FirstOrDefault(f => f.Id == FolderId.Value);
        set
        {
            if (value?.Id == FolderId)
            {
                return;
            }

            FolderId = value?.Id;
        }
    }

    public ICommand SaveCommand { get; }
    public ICommand DeleteCommand { get; }

    public NoteViewModel(
        Note note,
        Guid projectId,
        NoteStore store,
        Func<IReadOnlyList<KanbanColumn>>? getColumns = null,
        Func<IReadOnlyList<Folder>>? getFolders = null)
    {
        _store = store;
        _projectId = projectId;
        _getColumns = getColumns ?? (() => Array.Empty<KanbanColumn>());
        _getFolders = getFolders ?? (() => Array.Empty<Folder>());
        Id = note.Id;
        _columnId = note.ColumnId;
        _folderId = note.FolderId;
        _title = note.Title;
        _content = note.Content ?? string.Empty;

        SaveCommand = new RelayCommand(Save);
        DeleteCommand = new RelayCommand(Delete);
    }

    private void Save()
    {
        _store.UpdateNote(new Note
        {
            Id = Id,
            ProjectId = _projectId,
            ColumnId = ColumnId,
            FolderId = FolderId,
            Title = Title,
            Content = Content,
            Order = 0,
            UpdatedAt = DateTime.UtcNow
        });
    }

    private void Delete() => _store.DeleteNote(new Note { Id = Id });
}
