using System.Text.Json;
using Windows.System.UserProfile;

namespace LauncherApp;

public sealed class LauncherSettings
{
    public string GameDirectoryPath { get; set; } = "";
    public string LanguageTag { get; set; } = "";

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
        GameDirectoryPath = GameDirectoryPath?.Trim() ?? "";
        LanguageTag = ResolveLanguageTag(LanguageTag);
    }

    private static string ResolveLanguageTag(string? languageTag)
    {
        if (!string.IsNullOrWhiteSpace(languageTag))
        {
            return NormalizeLanguageTag(languageTag);
        }

        var languages = GlobalizationPreferences.Languages;
        if (languages.Count > 0)
        {
            return NormalizeLanguageTag(languages[0]);
        }

        return "en-US";
    }

    private static string NormalizeLanguageTag(string languageTag)
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

        if (tag.StartsWith("en"))
        {
            return "en-US";
        }

        return "en-US";
    }
}
