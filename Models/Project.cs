namespace Mynote.Models;

public sealed class Project
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "New Project";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
