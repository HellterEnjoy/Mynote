using System;
using System.Text.Json;

namespace Mynote.Services;

public sealed class AppSettingsStore
{
    public sealed class AppSettings
    {
        public bool IsDarkTheme { get; set; }
        public string? LastProjectRootPath { get; set; }
        public bool AutoOpenLastProject { get; set; } = true;

        public bool SaveOnBlur { get; set; }
        public bool SaveOnClose { get; set; }
        public int AutoSaveIntervalSeconds { get; set; }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    private readonly object _lock = new();
    private readonly string _settingsPath;
    private AppSettings _settings = new();

    public AppSettingsStore()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var directory = Path.Combine(root, "Mynote");
        _settingsPath = Path.Combine(directory, "settings.json");
    }

    public AppSettings Load()
    {
        lock (_lock)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
            if (!File.Exists(_settingsPath))
            {
                _settings = new AppSettings();
                Persist();
                return Clone(_settings);
            }

            try
            {
                var json = File.ReadAllText(_settingsPath);
                _settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            }
            catch
            {
                _settings = new AppSettings();
            }

            return Clone(_settings);
        }
    }

    public AppSettings Current
    {
        get
        {
            lock (_lock)
            {
                return Clone(_settings);
            }
        }
    }

    public void SetTheme(bool isDark)
    {
        lock (_lock)
        {
            _settings.IsDarkTheme = isDark;
            Persist();
        }
    }

    public void SetLastProject(string? rootPath)
    {
        lock (_lock)
        {
            _settings.LastProjectRootPath = string.IsNullOrWhiteSpace(rootPath) ? null : rootPath.Trim();
            Persist();
        }
    }

    public void SetAutoSaveSettings(bool saveOnBlur, bool saveOnClose, int autoSaveIntervalSeconds)
    {
        lock (_lock)
        {
            _settings.SaveOnBlur = saveOnBlur;
            _settings.SaveOnClose = saveOnClose;
            _settings.AutoSaveIntervalSeconds = Math.Max(0, autoSaveIntervalSeconds);
            Persist();
        }
    }

    private void Persist()
    {
        var json = JsonSerializer.Serialize(_settings, JsonOptions);
        File.WriteAllText(_settingsPath, json);
    }

    private static AppSettings Clone(AppSettings settings) => new()
    {
        IsDarkTheme = settings.IsDarkTheme,
        LastProjectRootPath = settings.LastProjectRootPath,
        AutoOpenLastProject = settings.AutoOpenLastProject,
        SaveOnBlur = settings.SaveOnBlur,
        SaveOnClose = settings.SaveOnClose,
        AutoSaveIntervalSeconds = settings.AutoSaveIntervalSeconds,
    };
}
