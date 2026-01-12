using System.Text;

namespace LauncherApp;

public static class Logger
{
    private static readonly object Gate = new();
    private static string? logPath;

    public static void Init()
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CastoriceLauncher");
            Directory.CreateDirectory(dir);
            logPath = Path.Combine(dir, "launcher.log");
            Info($"Logger initialized. Version={typeof(Logger).Assembly.GetName().Version}");
        }
        catch
        {
            logPath = null;
        }
    }

    public static void Info(string message) => Write("INFO", message);
    public static void Warn(string message) => Write("WARN", message);
    public static void Error(string message) => Write("ERROR", message);
    public static void Error(Exception ex, string message) => Write("ERROR", $"{message}\n{ex}");

    private static void Write(string level, string message)
    {
        var path = logPath;
        if (string.IsNullOrWhiteSpace(path)) return;

        lock (Gate)
        {
            try
            {
                var line = $"{DateTimeOffset.Now:O} [{level}] {message}\n";
                File.AppendAllText(path, line, Encoding.UTF8);
            }
            catch
            {
                // ignore
            }
        }
    }
}

