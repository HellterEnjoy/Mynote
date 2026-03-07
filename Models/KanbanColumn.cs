namespace Mynote.Models;

public sealed class KanbanColumn
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProjectId { get; set; }
    public string Title { get; set; } = "New Column";
    public int Order { get; set; }
}
