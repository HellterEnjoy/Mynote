namespace Mynote.Models;

public sealed class ProjectProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "New Project";
    public string RootPath { get; set; } = string.Empty;
    public DateTime LastOpenedAt { get; set; } = DateTime.UtcNow;
}

