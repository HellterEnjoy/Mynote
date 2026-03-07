using System;
using System.IO;
using System.Linq;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using MyAvaloniaApp.ViewModels;
using Mynote.Models;
using Mynote.Services;

namespace MyAvaloniaApp.Views;

public partial class ProjectPickerWindow : Window
{
    private readonly ProjectRegistry _registry = new();
    private readonly AppSettingsStore _settings;
    private readonly ProjectConfigStore _projectConfig = new();

    public ProjectPickerWindow() : this(null) { }

    public ProjectPickerWindow(AppSettingsStore? settings = null)
    {
        _settings = settings ?? new AppSettingsStore();
        InitializeComponent();
        DataContext = new ProjectPickerViewModel(_registry);
    }

    private ProjectPickerViewModel Vm => (ProjectPickerViewModel)DataContext!;

    private async void BrowseNewPath_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose a folder for this project",
            AllowMultiple = false
        });

        var folder = folders.FirstOrDefault();
        if (folder?.Path is null)
        {
            return;
        }

        Vm.NewProjectPath = folder.Path.LocalPath;
    }

    private async void OpenExisting_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Open existing project folder",
            AllowMultiple = false
        });

        var folder = folders.FirstOrDefault();
        if (folder?.Path is null)
        {
            return;
        }

        var rootPath = folder.Path.LocalPath;
        if (!Directory.Exists(rootPath))
        {
            return;
        }

        if (!await EnsureProjectUnlockedAsync(rootPath))
        {
            return;
        }

        var store = new NoteStore(rootPath);
        try
        {
            store.Load();
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("Cannot open project", ex.Message);
            return;
        }

        var projectName = store.GetProjects().FirstOrDefault()?.Name;
        var profile = new ProjectProfile
        {
            Name = string.IsNullOrWhiteSpace(projectName) ? Path.GetFileName(rootPath.TrimEnd(Path.DirectorySeparatorChar)) : projectName!,
            RootPath = rootPath,
            LastOpenedAt = DateTime.UtcNow
        };

        Vm.Upsert(profile);
        OpenMain(store, profile);
    }

    private async void Create_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var name = Vm.NewProjectName?.Trim();
        var rootPath = Vm.NewProjectPath?.Trim();

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(rootPath))
        {
            return;
        }

        var targetRoot = EnsureNewProjectRoot(rootPath, name);
        Directory.CreateDirectory(targetRoot);

        var store = new NoteStore(targetRoot, name);
        try
        {
            store.Load();
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("Cannot create project", ex.Message);
            return;
        }

        if (Vm.IsPasswordEnabled)
        {
            if (string.IsNullOrWhiteSpace(Vm.NewProjectPassword))
            {
                await ShowErrorAsync("Password required", "Please enter a password or disable password protection.");
                return;
            }

            if (!string.Equals(Vm.NewProjectPassword, Vm.NewProjectPasswordConfirm, StringComparison.Ordinal))
            {
                await ShowErrorAsync("Password mismatch", "The passwords do not match.");
                return;
            }

            _projectConfig.SetPassword(targetRoot, Vm.NewProjectPassword, Vm.NewProjectPasswordHint);
        }

        var profile = new ProjectProfile
        {
            Name = name,
            RootPath = targetRoot,
            LastOpenedAt = DateTime.UtcNow
        };

        Vm.Upsert(profile);
        OpenMain(store, profile);
    }

    private async void OpenSelected_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (Vm.SelectedProject is null)
        {
            return;
        }

        var profile = Vm.SelectedProject;
        if (string.IsNullOrWhiteSpace(profile.RootPath) || !Directory.Exists(profile.RootPath))
        {
            return;
        }

        if (!await EnsureProjectUnlockedAsync(profile.RootPath))
        {
            return;
        }

        var store = new NoteStore(profile.RootPath);
        try
        {
            store.Load();
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("Cannot open project", ex.Message);
            return;
        }

        profile.LastOpenedAt = DateTime.UtcNow;
        Vm.Touch(profile);

        OpenMain(store, profile);
    }

    private void Remove_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Vm.RemoveSelected();
    }

    private void OpenMain(NoteStore store, ProjectProfile profile)
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            return;
        }

        _settings.SetLastProject(profile.RootPath);
        var main = new MainWindow
        {
            DataContext = new MainViewModel(store, _settings),
            Title = $"Mynote - {profile.Name}"
        };

        desktop.MainWindow = main;
        main.Show();
        Close();
    }

    private static string EnsureNewProjectRoot(string chosenPath, string projectName)
    {
        // Users often pick a shared "Mynote Data" folder and expect separate project subfolders.
        // If the folder already contains an existing project store, create a subfolder for the new project.
        var chosen = chosenPath.Trim();
        var storePath = Path.Combine(chosen, "mynote.json");
        if (!File.Exists(storePath))
        {
            return chosen;
        }

        var baseName = SanitizeFolderName(projectName);
        if (string.IsNullOrWhiteSpace(baseName))
        {
            baseName = "New Project";
        }

        var candidate = Path.Combine(chosen, baseName);
        if (!Directory.Exists(candidate) && !File.Exists(Path.Combine(candidate, "mynote.json")))
        {
            return candidate;
        }

        for (var i = 2; i < 1000; i++)
        {
            var next = Path.Combine(chosen, $"{baseName} {i}");
            if (!Directory.Exists(next) && !File.Exists(Path.Combine(next, "mynote.json")))
            {
                return next;
            }
        }

        // Fallback: use a GUID suffix.
        return Path.Combine(chosen, $"{baseName} {Guid.NewGuid():N}");
    }

    private static string SanitizeFolderName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(name.Length);
        foreach (var ch in name.Trim())
        {
            if (invalid.Contains(ch))
            {
                continue;
            }

            sb.Append(ch);
        }

        var sanitized = sb.ToString().Trim();
        return sanitized.Length == 0 ? "New Project" : sanitized;
    }

    private async Task ShowErrorAsync(string title, string message)
    {
        var appBg = ResolveBrush("AppBg");
        var surfaceBg = ResolveBrush("SurfaceBg");
        var border = ResolveBrush("Border");

        var dialog = new Window
        {
            Title = title,
            Width = 560,
            Height = 240,
            Background = appBg,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            TransparencyLevelHint = new[] { WindowTransparencyLevel.None },
        };

        var ok = new Button
        {
            Content = "OK",
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Padding = new Avalonia.Thickness(12, 8),
        };
        ok.Click += (_, __) => dialog.Close();

        dialog.Content = new Border
        {
            Background = surfaceBg,
            BorderBrush = border,
            BorderThickness = new Avalonia.Thickness(1),
            CornerRadius = new Avalonia.CornerRadius(12),
            Padding = new Avalonia.Thickness(16),
            Margin = new Avalonia.Thickness(12),
            Child = new StackPanel
            {
                Spacing = 12,
                Children =
                {
                    new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                    ok
                }
            }
        };

        await dialog.ShowDialog(this);
    }

    private async Task<bool> EnsureProjectUnlockedAsync(string projectRootPath)
    {
        var config = _projectConfig.Load(projectRootPath);
        if (!_projectConfig.HasPassword(config))
        {
            return true;
        }

        // Give the user up to 3 tries; cancel returns false.
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var password = await PromptPasswordAsync("Enter project password", config.PasswordHint);
            if (password is null)
            {
                return false;
            }

            if (_projectConfig.VerifyPassword(config, password))
            {
                return true;
            }

            await ShowErrorAsync("Wrong password", "The password is incorrect.");
        }

        return false;
    }

    private async Task<string?> PromptPasswordAsync(string title, string? hint)
    {
        var appBg = ResolveBrush("AppBg");
        var surfaceBg = ResolveBrush("SurfaceBg");
        var border = ResolveBrush("Border");
        var borderSoft = ResolveBrush("BorderSoft");
        var textPrimary = ResolveBrush("TextPrimary");
        var textSecondary = ResolveBrush("TextSecondary");

        var dialog = new Window
        {
            Title = title,
            Width = 520,
            Height = 260,
            Background = appBg,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            TransparencyLevelHint = new[] { WindowTransparencyLevel.None },
        };

        var input = new TextBox
        {
            PasswordChar = '*',
            Watermark = "Password",
            Padding = new Avalonia.Thickness(10),
            CornerRadius = new Avalonia.CornerRadius(8),
            BorderBrush = borderSoft,
            BorderThickness = new Avalonia.Thickness(1),
            Background = surfaceBg,
            Foreground = textPrimary,
        };

        string? result = null;

        var ok = new Button
        {
            Content = "Open",
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Padding = new Avalonia.Thickness(12, 8),
            Background = textPrimary,
            Foreground = surfaceBg,
            BorderThickness = new Avalonia.Thickness(0),
        };
        ok.Click += (_, __) =>
        {
            result = input.Text ?? string.Empty;
            dialog.Close();
        };

        var cancel = new Button
        {
            Content = "Cancel",
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Padding = new Avalonia.Thickness(12, 8),
            Background = surfaceBg,
            BorderBrush = borderSoft,
            BorderThickness = new Avalonia.Thickness(1),
        };
        cancel.Click += (_, __) =>
        {
            result = null;
            dialog.Close();
        };

        var hintBlock = string.IsNullOrWhiteSpace(hint)
            ? null
            : new TextBlock
            {
                Text = $"Hint: {hint}",
                Foreground = textSecondary,
                TextWrapping = Avalonia.Media.TextWrapping.Wrap
            };

        var content = new StackPanel
        {
            Spacing = 12,
            Children =
            {
                new TextBlock { Text = "This project is password-protected.", Foreground = textPrimary },
                hintBlock ?? new TextBlock { Text = string.Empty },
                input,
                new StackPanel
                {
                    Orientation = Avalonia.Layout.Orientation.Horizontal,
                    Spacing = 10,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                    Children = { cancel, ok }
                }
            }
        };

        dialog.Content = new Border
        {
            Background = surfaceBg,
            BorderBrush = border,
            BorderThickness = new Avalonia.Thickness(1),
            CornerRadius = new Avalonia.CornerRadius(12),
            Padding = new Avalonia.Thickness(16),
            Margin = new Avalonia.Thickness(12),
            Child = content
        };

        await dialog.ShowDialog(this);
        return result;
    }

    private static IBrush ResolveBrush(string key)
    {
        if (Application.Current is { } app &&
            app.TryGetResource(key, app.RequestedThemeVariant, out var value) &&
            value is IBrush brush)
        {
            return brush;
        }

        return key switch
        {
            "AppBg" => new SolidColorBrush(ResolveFallbackColor("#f5f6f8", "#0f1116")),
            "SurfaceBg" => new SolidColorBrush(ResolveFallbackColor("#ffffff", "#161a22")),
            "Border" => new SolidColorBrush(ResolveFallbackColor("#dfe2e8", "#2b3242")),
            "BorderSoft" => new SolidColorBrush(ResolveFallbackColor("#e1e2e5", "#2b3242")),
            "TextPrimary" => new SolidColorBrush(ResolveFallbackColor("#1f232b", "#e8ecf4")),
            "TextSecondary" => new SolidColorBrush(ResolveFallbackColor("#6f7680", "#a6afbf")),
            _ => Brushes.Transparent
        };
    }

    private static Color ResolveFallbackColor(string lightHex, string darkHex)
    {
        var isDark = Application.Current?.RequestedThemeVariant == ThemeVariant.Dark;
        return Color.Parse(isDark ? darkHex : lightHex);
    }
}
