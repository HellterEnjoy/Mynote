using System.Collections.ObjectModel;
using Mynote.Models;
using Mynote.Services;

namespace MyAvaloniaApp.ViewModels;

public sealed class ProjectPickerViewModel : ViewModelBase
{
    private readonly ProjectRegistry _registry;

    public ObservableCollection<ProjectProfile> Projects { get; }

    private ProjectProfile? _selectedProject;
    public ProjectProfile? SelectedProject
    {
        get => _selectedProject;
        set => Set(ref _selectedProject, value);
    }

    private string _newProjectName = "New Project";
    public string NewProjectName
    {
        get => _newProjectName;
        set => Set(ref _newProjectName, value);
    }

    private string _newProjectPath = string.Empty;
    public string NewProjectPath
    {
        get => _newProjectPath;
        set => Set(ref _newProjectPath, value);
    }

    private bool _isPasswordEnabled;
    public bool IsPasswordEnabled
    {
        get => _isPasswordEnabled;
        set => Set(ref _isPasswordEnabled, value);
    }

    private string _newProjectPassword = string.Empty;
    public string NewProjectPassword
    {
        get => _newProjectPassword;
        set => Set(ref _newProjectPassword, value);
    }

    private string _newProjectPasswordConfirm = string.Empty;
    public string NewProjectPasswordConfirm
    {
        get => _newProjectPasswordConfirm;
        set => Set(ref _newProjectPasswordConfirm, value);
    }

    private string _newProjectPasswordHint = string.Empty;
    public string NewProjectPasswordHint
    {
        get => _newProjectPasswordHint;
        set => Set(ref _newProjectPasswordHint, value);
    }

    public ProjectPickerViewModel(ProjectRegistry registry)
    {
        _registry = registry;
        Projects = new ObservableCollection<ProjectProfile>(_registry.Load());
        SelectedProject = Projects.FirstOrDefault();
    }

    public void Reload()
    {
        Projects.Clear();
        foreach (var p in _registry.Load())
        {
            Projects.Add(p);
        }

        if (SelectedProject is null && Projects.Count > 0)
        {
            SelectedProject = Projects[0];
        }
    }

    public void RemoveSelected()
    {
        if (SelectedProject is null)
        {
            return;
        }

        _registry.Remove(SelectedProject.Id);
        Reload();
    }

    public void Touch(ProjectProfile profile)
    {
        profile.LastOpenedAt = DateTime.UtcNow;
        _registry.Upsert(profile);
        Reload();
    }

    public void Upsert(ProjectProfile profile)
    {
        _registry.Upsert(profile);
        Reload();
    }
}
