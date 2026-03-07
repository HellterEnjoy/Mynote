namespace Mynote.Models;

public sealed class Folder
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProjectId { get; set; }
    public string Name { get; set; } = "New Folder";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

