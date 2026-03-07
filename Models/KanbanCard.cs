namespace Mynote.Models;

public sealed class KanbanCard
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProjectId { get; set; }
    public Guid ColumnId { get; set; }
    public string Title { get; set; } = "New Card";
    public string Content { get; set; } = string.Empty;
    public Guid? LinkedNoteId { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public int Order { get; set; }
}
