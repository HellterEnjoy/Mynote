using System.Text.Json;
using Mynote.Models;

namespace Mynote.Services;

public sealed class ProjectRegistry
{
    private sealed class RegistryData
    {
        public List<ProjectProfile> Projects { get; set; } = [];
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    private readonly object _lock = new();
    private readonly string _registryPath;
    private List<ProjectProfile> _projects = [];

    public ProjectRegistry()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var directory = Path.Combine(root, "Mynote");
        _registryPath = Path.Combine(directory, "projects.json");
    }

    public IReadOnlyList<ProjectProfile> Load()
    {
        lock (_lock)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_registryPath)!);
            if (!File.Exists(_registryPath))
            {
                _projects = [];
                Persist();
                return _projects.ToList();
            }

            try
            {
                var json = File.ReadAllText(_registryPath);
                var data = JsonSerializer.Deserialize<RegistryData>(json, JsonOptions) ?? new RegistryData();
                _projects = data.Projects ?? [];
            }
            catch
            {
                _projects = [];
            }

            return _projects
                .OrderByDescending(p => p.LastOpenedAt)
                .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }

    public void Upsert(ProjectProfile profile)
    {
        lock (_lock)
        {
            var existing = _projects.FirstOrDefault(p => p.Id == profile.Id);
            if (existing is null)
            {
                _projects.Add(profile);
            }
            else
            {
                existing.Name = profile.Name;
                existing.RootPath = profile.RootPath;
                existing.LastOpenedAt = profile.LastOpenedAt;
            }

            Persist();
        }
    }

    public void Remove(Guid projectId)
    {
        lock (_lock)
        {
            _projects.RemoveAll(p => p.Id == projectId);
            Persist();
        }
    }

    private void Persist()
    {
        var data = new RegistryData { Projects = _projects };
        var json = JsonSerializer.Serialize(data, JsonOptions);
        File.WriteAllText(_registryPath, json);
    }
}

