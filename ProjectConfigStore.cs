using System.Security.Cryptography;
using System.Text.Json;

namespace Mynote.Services;

public sealed class ProjectConfigStore
{
    public const string FileName = "mynote.project.json";

    public sealed class ProjectConfig
    {
        public int Version { get; set; } = 1;

        // Optional app-level access gate (NOT encryption). Stored as a salted hash.
        public string? PasswordSalt { get; set; }
        public string? PasswordHash { get; set; }
        public int PasswordIterations { get; set; } = 200_000;
        public string? PasswordHint { get; set; }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    public ProjectConfig Load(string projectRootPath)
    {
        if (string.IsNullOrWhiteSpace(projectRootPath))
        {
            throw new ArgumentException("Project root path is required.", nameof(projectRootPath));
        }

        var path = GetPath(projectRootPath);
        if (!File.Exists(path))
        {
            return new ProjectConfig();
        }

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<ProjectConfig>(json, JsonOptions) ?? new ProjectConfig();
        }
        catch
        {
            return new ProjectConfig();
        }
    }

    public void Save(string projectRootPath, ProjectConfig config)
    {
        if (config is null)
        {
            throw new ArgumentNullException(nameof(config));
        }

        Directory.CreateDirectory(projectRootPath);
        var json = JsonSerializer.Serialize(config, JsonOptions);
        File.WriteAllText(GetPath(projectRootPath), json);
    }

    public bool HasPassword(ProjectConfig config)
        => config is not null &&
           !string.IsNullOrWhiteSpace(config.PasswordHash) &&
           !string.IsNullOrWhiteSpace(config.PasswordSalt) &&
           config.PasswordIterations > 0;

    public void SetPassword(string projectRootPath, string password, string? hint)
    {
        if (string.IsNullOrEmpty(password))
        {
            throw new ArgumentException("Password cannot be empty.", nameof(password));
        }

        var cfg = Load(projectRootPath);
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = HashPassword(password, salt, cfg.PasswordIterations);

        cfg.PasswordSalt = Convert.ToBase64String(salt);
        cfg.PasswordHash = Convert.ToBase64String(hash);
        cfg.PasswordHint = string.IsNullOrWhiteSpace(hint) ? null : hint.Trim();
        Save(projectRootPath, cfg);
    }

    public void ClearPassword(string projectRootPath)
    {
        var cfg = Load(projectRootPath);
        cfg.PasswordSalt = null;
        cfg.PasswordHash = null;
        cfg.PasswordHint = null;
        Save(projectRootPath, cfg);
    }

    public bool VerifyPassword(ProjectConfig config, string password)
    {
        if (!HasPassword(config))
        {
            return true;
        }

        if (string.IsNullOrEmpty(password))
        {
            return false;
        }

        try
        {
            var salt = Convert.FromBase64String(config.PasswordSalt!);
            var expected = Convert.FromBase64String(config.PasswordHash!);
            var actual = HashPassword(password, salt, config.PasswordIterations);
            return CryptographicOperations.FixedTimeEquals(expected, actual);
        }
        catch
        {
            return false;
        }
    }

    private static byte[] HashPassword(string password, byte[] salt, int iterations)
    {
        // PBKDF2 is available out-of-the-box; for stronger protection we could move to Argon2id later.
        return Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            iterations,
            HashAlgorithmName.SHA256,
            32);
    }

    private static string GetPath(string projectRootPath)
        => Path.Combine(projectRootPath.Trim(), FileName);
}

