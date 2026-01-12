using System.Text.Json;
using Windows.System.UserProfile;

namespace LauncherApp;

public sealed class LauncherSettings
{
    private const string DefaultBackgroundUri = "ms-appx:///Assets/DefaultBackground.webp";
    public string GameExePath { get; set; } = "";
    public string PatchSourcePath { get; set; } = "";
    public string ServerExePath { get; set; } = "";
    public string LanguageTag { get; set; } = "";
    public string BackgroundImagePath { get; set; } = "";
    public string NoticeUrl { get; set; } = "";
    public string UpdateUrl { get; set; } = "";

    public static string SettingsPath
    {
        get
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CastoriceLauncher");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "launcher-settings.json");
        }
    }

    public static LauncherSettings Load()
    {
        LauncherSettings settings = new();
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                settings = JsonSerializer.Deserialize<LauncherSettings>(json) ?? new LauncherSettings();
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to load launcher-settings.json; using defaults.");
            settings = new LauncherSettings();
        }

        settings.ApplyDefaults();
        return settings;
    }

    public void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions
            {
                WriteIndented = true,
            });
            File.WriteAllText(SettingsPath, json);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to save launcher-settings.json.");
        }
    }

    public void ApplyDefaults()
    {
        if (string.IsNullOrWhiteSpace(GameExePath))
        {
            var settingsPath = FindInParents("CastoricePS-settings.json", expectFile: true);
            if (settingsPath != null)
            {
                var fromSettings = TryReadLastSelected(settingsPath);
                if (!string.IsNullOrWhiteSpace(fromSettings))
                {
                    GameExePath = fromSettings;
                }
            }
        }

        if (string.IsNullOrWhiteSpace(PatchSourcePath))
        {
            PatchSourcePath = FindInParents("launcher\\patch", expectFile: false)
                ?? Path.Combine(AppContext.BaseDirectory, "patch");
        }

        if (string.IsNullOrWhiteSpace(ServerExePath))
        {
            ServerExePath =
                FindInParents("CastoricePS.exe", expectFile: true)
                ?? FindInParents("zig-out\\bin\\CastoricePS.exe", expectFile: true)
                ?? "";
        }

        if (string.IsNullOrWhiteSpace(BackgroundImagePath))
        {
            BackgroundImagePath = DefaultBackgroundUri;
        }

        if (string.IsNullOrWhiteSpace(NoticeUrl))
        {
            NoticeUrl = "111.170.35.230:5080";
        }

        if (string.IsNullOrWhiteSpace(UpdateUrl))
        {
            UpdateUrl = "111.170.35.230:5080";
        }

        LanguageTag = ResolveLanguageTag(LanguageTag);
    }

    private static string ResolveLanguageTag(string? languageTag)
    {
        if (!string.IsNullOrWhiteSpace(languageTag))
        {
            return NormalizeChineseTag(languageTag);
        }

        var languages = GlobalizationPreferences.Languages;
        if (languages.Count > 0)
        {
            return NormalizeChineseTag(languages[0]);
        }

        return "en-US";
    }

    private static string NormalizeChineseTag(string languageTag)
    {
        var tag = languageTag.Trim().ToLowerInvariant();
        if (tag.StartsWith("zh-hans") || tag.StartsWith("zh-cn") || tag.StartsWith("zh-sg") || tag.StartsWith("zh-my"))
        {
            return "zh-CN";
        }

        if (tag.StartsWith("zh-hant") || tag.StartsWith("zh-tw") || tag.StartsWith("zh-hk") || tag.StartsWith("zh-mo"))
        {
            return "zh-TW";
        }

        return "en-US";
    }

    private static string? FindInParents(string relativePath, bool expectFile)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, relativePath);
            if (expectFile)
            {
                if (File.Exists(candidate)) return candidate;
            }
            else
            {
                if (Directory.Exists(candidate)) return candidate;
            }
            dir = dir.Parent;
        }

        return null;
    }

    private static string? TryReadLastSelected(string settingsPath)
    {
        try
        {
            var json = File.ReadAllText(settingsPath);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("last_selected", out var prop))
            {
                if (prop.ValueKind == JsonValueKind.String)
                {
                    return prop.GetString();
                }
            }
        }
        catch
        {
            return null;
        }

        return null;
    }
}
