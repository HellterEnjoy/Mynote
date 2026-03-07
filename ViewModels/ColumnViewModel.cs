using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using Mynote.Models;
using Mynote.Services;

namespace MyAvaloniaApp.ViewModels;

public sealed class ColumnViewModel : ViewModelBase
{
    private readonly NoteStore _store;
    private readonly Guid _projectId;
    private readonly KanbanColumn _column;
    private readonly Action? _refresh;

    public Guid Id => _column.Id;

    public string Title
    {
        get => _column.Title;
        set
        {
            var next = string.IsNullOrWhiteSpace(value) ? _column.Title : value.Trim();
            if (string.Equals(_column.Title, next, StringComparison.Ordinal))
            {
                return;
            }

            _column.Title = next;
            Raise();
            _store.RenameColumn(_column.Id, next);
        }
    }

    public ObservableCollection<KanbanCardViewModel> Cards { get; }

    public ICommand AddCardCommand { get; }
    public ICommand DeleteColumnCommand { get; }

    public ColumnViewModel(
        KanbanColumn column,
        Guid projectId,
        NoteStore store,
        IEnumerable<KanbanCard> cards,
        Action? refresh = null)
    {
        _column = column;
        _projectId = projectId;
        _store = store;
        _refresh = refresh;

        Cards = new ObservableCollection<KanbanCardViewModel>(
            cards.Select(c => new KanbanCardViewModel(c, _store)));

        AddCardCommand = new RelayCommand(AddCard);
        DeleteColumnCommand = new RelayCommand(DeleteColumn);
    }

    private void AddCard()
    {
        var card = _store.CreateCard(_projectId, _column.Id);
        Cards.Add(new KanbanCardViewModel(card, _store));
    }

    private void DeleteColumn()
    {
        _store.DeleteColumn(_column.Id);
        _refresh?.Invoke();
    }
}

