using System;
using Mynote.Models;
using Mynote.Services;

namespace MyAvaloniaApp.ViewModels;

public sealed class KanbanCardViewModel : ViewModelBase
{
    private readonly NoteStore _store;
    private readonly KanbanCard _card;

    public Guid Id => _card.Id;
    public Guid ColumnId => _card.ColumnId;

    public string Title
    {
        get => _card.Title;
        set
        {
            var next = string.IsNullOrWhiteSpace(value) ? "New Card" : value.Trim();
            if (string.Equals(_card.Title, next, StringComparison.Ordinal))
            {
                return;
            }

            _card.Title = next;
            _card.UpdatedAt = DateTime.UtcNow;
            Raise();
            _store.UpdateCard(_card);
        }
    }

    public Guid? LinkedNoteId
    {
        get => _card.LinkedNoteId;
        set
        {
            if (_card.LinkedNoteId == value)
            {
                return;
            }

            _card.LinkedNoteId = value;
            _card.UpdatedAt = DateTime.UtcNow;
            Raise();
            Raise(nameof(HasLinkedNote));
            _store.UpdateCard(_card);
        }
    }

    public bool HasLinkedNote => LinkedNoteId.HasValue;

    public KanbanCardViewModel(KanbanCard card, NoteStore store)
    {
        _card = card;
        _store = store;
    }

    public KanbanCard ToModel() => _card;
}

