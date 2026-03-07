using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using MyAvaloniaApp.Views;
using Avalonia.Styling;
using Mynote.Services;
namespace MyAvaloniaApp;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var settingsStore = new AppSettingsStore();
            var settings = settingsStore.Load();

            RequestedThemeVariant = settings.IsDarkTheme ? ThemeVariant.Dark : ThemeVariant.Light;

            if (settings.AutoOpenLastProject)
            {
                var rootPath = settings.LastProjectRootPath;
                if (string.IsNullOrWhiteSpace(rootPath))
                {
                    var registry = new ProjectRegistry();
                    rootPath = registry.Load().FirstOrDefault()?.RootPath;
                }

                if (!string.IsNullOrWhiteSpace(rootPath) && Directory.Exists(rootPath))
                {
                    try
                    {
                        var store = new NoteStore(rootPath);
                        store.Load();
                        var projectName = store.GetProjects().FirstOrDefault()?.Name ?? "Mynote";
                        desktop.MainWindow = new MainWindow
                        {
                            DataContext = new ViewModels.MainViewModel(store, settingsStore),
                            Title = $"Mynote - {projectName}"
                        };
                        base.OnFrameworkInitializationCompleted();
                        return;
                    }
                    catch
                    {
                        // Fallback to picker.
                    }
                }
            }

            desktop.MainWindow = new ProjectPickerWindow(settingsStore);
        }
        base.OnFrameworkInitializationCompleted();
    }
}
