using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Net.Http;
using System.Text.Json;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.Windows.ApplicationModel.Resources;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.UI;
using Windows.Storage.Pickers;
using Windows.System;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace LauncherApp.Views;

public partial class MainPage : Page
{
    private readonly ResourceLoader stringResources = new();
    private const string LanguageEnglishTag = "en-US";
    private readonly HttpClient httpClient = new();
    private bool isAnnouncementLoading;
    private LauncherSettings settings = new();
    private Process? launcherProcess;
    private Process? serverProcess;
    private readonly object serverLogLock = new();
    private readonly StringBuilder serverLog = new();
    private bool serverLogUpdateScheduled;
    private PsUpdateManifest? psUpdateManifest;
    private bool psUpdateChecking;
    private bool psUpdateInstalling;
    private string? psUpdateStatusOverride;
    private bool psUpdateDownloading;
    private long psUpdateDownloadedBytes;
    private long? psUpdateTotalBytes;
    private string? psUpdateDownloadPath;

    private LauncherUpdateManifest? launcherUpdateManifest;
    private bool launcherUpdateChecking;
    private bool launcherUpdateInstalling;
    private string? launcherUpdateStatusOverride;
    private bool launcherUpdateDownloading;
    private long launcherUpdateDownloadedBytes;
    private long? launcherUpdateTotalBytes;
    private string? launcherUpdateDownloadPath;

    public MainPage()
    {
        InitializeComponent();
        settings = LauncherSettings.Load();
        settings.ApplyDefaults();
        settings.Save();
        LoadSettingsIntoUi();
        Loaded += (_, _) => ApplyTheme();
        ActualThemeChanged += (_, _) => ApplyTheme();
        UpdateHomeSummary();
        UpdateHomeBackground();
        UpdateServerSummary();
        UpdateServerUi();
        UpdatePsUpdateUi();
        UpdateLauncherUpdateUi();
        UpdateStatus(R("StatusIdle"));
        NavView.SelectedItem = HomeNavItem;
        UpdateAnnouncementDefaults();
        _ = LoadAnnouncementAsync();
        _ = CheckPsUpdateAsync();
        _ = CheckLauncherUpdateAsync();
    }

    private void ApplyTheme()
    {
        var dark = ActualTheme == ElementTheme.Dark;

        var paneSurface = dark ? Color.FromArgb(0xB3, 0x10, 0x10, 0x10) : Color.FromArgb(0xB3, 0xFF, 0xFF, 0xFF);
        SetBrushColor("PaneSurfaceBrush", paneSurface);
        SetBrushColor("CardSurfaceBrush", dark ? Color.FromArgb(0x99, 0x10, 0x10, 0x10) : Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF));
        SetBrushColor("CardBorderBrush", dark ? Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x1A, 0x00, 0x00, 0x00));
        SetBrushColor("GhostButtonBackgroundBrush", dark ? Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x14, 0x00, 0x00, 0x00));
        SetBrushColor("IconButtonBackgroundBrush", dark ? Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x14, 0x00, 0x00, 0x00));
        SetNavViewBrushColor("NavigationViewDefaultPaneBackground", paneSurface);
        SetNavViewBrushColor("NavigationViewExpandedPaneBackground", paneSurface);

        if (Application.Current.Resources.TryGetValue("SystemAccentColor", out var accentObj) && accentObj is Color accent)
        {
            SetBrushColor("AccentBrush", accent);
            SetBrushColor("LinkForegroundBrush", accent);
        }

        HomeView.Background = dark ? CreateHomeGradientDark() : CreateHomeGradientLight();
    }

    private void SetBrushColor(string key, Color color)
    {
        if (RootGrid.Resources.TryGetValue(key, out var b) && b is SolidColorBrush sb)
        {
            sb.Color = color;
        }
    }

    private void SetNavViewBrushColor(string key, Color color)
    {
        if (NavView.Resources.TryGetValue(key, out var b) && b is SolidColorBrush sb)
        {
            sb.Color = color;
        }
    }

    private static LinearGradientBrush CreateHomeGradientLight()
    {
        return new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 1),
            GradientStops =
            {
                new GradientStop { Color = Color.FromArgb(0xB0, 0xF6, 0xF2, 0xEB), Offset = 0 },
                new GradientStop { Color = Color.FromArgb(0xA0, 0xE6, 0xF0, 0xFF), Offset = 0.55 },
                new GradientStop { Color = Color.FromArgb(0xB0, 0xFD, 0xFB, 0xF8), Offset = 1 },
            }
        };
    }

    private static LinearGradientBrush CreateHomeGradientDark()
    {
        return new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 1),
            GradientStops =
            {
                new GradientStop { Color = Color.FromArgb(0x80, 0x20, 0x20, 0x20), Offset = 0 },
                new GradientStop { Color = Color.FromArgb(0x60, 0x18, 0x18, 0x18), Offset = 0.55 },
                new GradientStop { Color = Color.FromArgb(0x80, 0x20, 0x20, 0x20), Offset = 1 },
            }
        };
    }

    private void OnNavSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        var tag = (args.SelectedItem as NavigationViewItem)?.Tag as string ?? "home";
        ShowView(tag);
    }

    private void ShowView(string tag)
    {
        HomeView.Visibility = tag == "home" ? Visibility.Visible : Visibility.Collapsed;
        SettingsView.Visibility = tag == "settings" ? Visibility.Visible : Visibility.Collapsed;
        LogsView.Visibility = tag == "logs" ? Visibility.Visible : Visibility.Collapsed;
        TutorialView.Visibility = tag == "tutorial" ? Visibility.Visible : Visibility.Collapsed;
    }

    private void LoadSettingsIntoUi()
    {
        GamePathBox.Text = settings.GameExePath;
        PatchPathBox.Text = settings.PatchSourcePath;
        ServerPathBox.Text = settings.ServerExePath;
        BackgroundPathBox.Text = settings.BackgroundImagePath;
        NoticeUrlBox.Text = settings.NoticeUrl;
        UpdateUrlBox.Text = settings.UpdateUrl;
        SelectLanguage(settings.LanguageTag);
    }

    private void ReadSettingsFromUi()
    {
        settings.GameExePath = GamePathBox.Text.Trim();
        settings.PatchSourcePath = PatchPathBox.Text.Trim();
        settings.ServerExePath = ServerPathBox.Text.Trim();
        settings.BackgroundImagePath = BackgroundPathBox.Text.Trim();
        settings.NoticeUrl = NoticeUrlBox.Text.Trim();
        settings.UpdateUrl = UpdateUrlBox.Text.Trim();
        settings.LanguageTag = GetSelectedLanguageTag();
    }

    private void UpdateHomeSummary()
    {
        var notSet = R("NotSetPlaceholder");
        GamePathSummaryText.Text = string.IsNullOrWhiteSpace(settings.GameExePath) ? notSet : settings.GameExePath;
        PatchPathSummaryText.Text = string.IsNullOrWhiteSpace(settings.PatchSourcePath) ? notSet : settings.PatchSourcePath;
    }

    private void UpdateServerSummary()
    {
        var notSet = R("NotSetPlaceholder");
        ServerExeSummaryText.Text = string.IsNullOrWhiteSpace(settings.ServerExePath) ? notSet : settings.ServerExePath;
    }

    private void UpdatePsUpdateUi()
    {
        PsLocalVersionText.Text = ReadLocalPsVersion() ?? R("UpdateNotInstalled");
        PsLatestVersionText.Text = psUpdateManifest?.Version ?? R("UpdateNotChecked");
        PsUpdateNotesText.Text = psUpdateManifest?.Notes ?? R("UpdateNotesNotAvailable");
        PsUpdateStatusText.Text = BuildPsUpdateStatusText();

        var downloadsDir = GetUpdateDownloadsDir();
        var cachedZip = GetLatestUpdateZipPath(downloadsDir);

        PsUpdateProgressPanel.Visibility = Visibility.Visible;
        if (psUpdateDownloading)
        {
            PsUpdateDownloadPathText.Text = psUpdateDownloadPath ?? downloadsDir;
        }
        else
        {
            PsUpdateDownloadPathText.Text = cachedZip == null
                ? downloadsDir
                : $"{downloadsDir}\n{R("UpdateCacheLatest")}: {Path.GetFileName(cachedZip)}";
        }

        PsUpdateProgressBar.Visibility = psUpdateDownloading ? Visibility.Visible : Visibility.Collapsed;
        PsUpdateProgressText.Visibility = psUpdateDownloading ? Visibility.Visible : Visibility.Collapsed;

        if (psUpdateDownloading)
        {
            if (psUpdateTotalBytes is > 0)
            {
                PsUpdateProgressBar.IsIndeterminate = false;
                PsUpdateProgressBar.Minimum = 0;
                PsUpdateProgressBar.Maximum = 1;
                PsUpdateProgressBar.Value = Math.Clamp((double)psUpdateDownloadedBytes / psUpdateTotalBytes.Value, 0, 1);
                PsUpdateProgressText.Text = RFormat("UpdateProgressFormat",
                    FormatBytes(psUpdateDownloadedBytes),
                    FormatBytes(psUpdateTotalBytes.Value));
            }
            else
            {
                PsUpdateProgressBar.IsIndeterminate = true;
                PsUpdateProgressText.Text = RFormat("UpdateProgressUnknownTotalFormat", FormatBytes(psUpdateDownloadedBytes));
            }
        }

        PsCheckUpdateButton.IsEnabled = !psUpdateChecking && !psUpdateInstalling;
        PsUpdateButton.IsEnabled = !psUpdateInstalling
                                   && psUpdateManifest?.Package?.HasValidUrl == true
                                   && IsRemoteNewerThanLocal();
    }

    private void UpdateLauncherUpdateUi()
    {
        LauncherLocalVersionText.Text = GetLauncherVersion() ?? R("UpdateVersionUnknown");
        LauncherLatestVersionText.Text = launcherUpdateManifest?.Version ?? R("UpdateNotChecked");
        LauncherUpdateNotesText.Text = launcherUpdateManifest?.Notes ?? R("UpdateNotesNotAvailable");
        LauncherUpdateStatusText.Text = BuildLauncherUpdateStatusText();

        var downloadsDir = GetUpdateDownloadsDir();
        var cachedZip = GetLatestUpdateZipPath(downloadsDir, "CastoriceLauncher-*.zip");

        LauncherUpdateProgressPanel.Visibility = Visibility.Visible;
        if (launcherUpdateDownloading)
        {
            LauncherUpdateDownloadPathText.Text = launcherUpdateDownloadPath ?? downloadsDir;
        }
        else
        {
            LauncherUpdateDownloadPathText.Text = cachedZip == null
                ? downloadsDir
                : $"{downloadsDir}\n{R("UpdateCacheLatest")}: {Path.GetFileName(cachedZip)}";
        }

        LauncherUpdateProgressBar.Visibility = launcherUpdateDownloading ? Visibility.Visible : Visibility.Collapsed;
        LauncherUpdateProgressText.Visibility = launcherUpdateDownloading ? Visibility.Visible : Visibility.Collapsed;

        if (launcherUpdateDownloading)
        {
            if (launcherUpdateTotalBytes is > 0)
            {
                LauncherUpdateProgressBar.IsIndeterminate = false;
                LauncherUpdateProgressBar.Minimum = 0;
                LauncherUpdateProgressBar.Maximum = 1;
                LauncherUpdateProgressBar.Value = Math.Clamp((double)launcherUpdateDownloadedBytes / launcherUpdateTotalBytes.Value, 0, 1);
                LauncherUpdateProgressText.Text = RFormat("UpdateProgressFormat",
                    FormatBytes(launcherUpdateDownloadedBytes),
                    FormatBytes(launcherUpdateTotalBytes.Value));
            }
            else
            {
                LauncherUpdateProgressBar.IsIndeterminate = true;
                LauncherUpdateProgressText.Text = RFormat("UpdateProgressUnknownTotalFormat", FormatBytes(launcherUpdateDownloadedBytes));
            }
        }

        LauncherCheckUpdateButton.IsEnabled = !launcherUpdateChecking && !launcherUpdateInstalling;
        LauncherUpdateButton.IsEnabled = !launcherUpdateInstalling
                                         && launcherUpdateManifest?.Package?.HasValidUrl == true
                                         && IsLauncherRemoteNewerThanLocal();
    }

    private string BuildLauncherUpdateStatusText()
    {
        if (launcherUpdateInstalling) return R("UpdateInstalling");
        if (launcherUpdateChecking) return R("UpdateChecking");
        if (!string.IsNullOrWhiteSpace(launcherUpdateStatusOverride)) return launcherUpdateStatusOverride!;
        if (string.IsNullOrWhiteSpace(settings.UpdateUrl)) return R("UpdateUrlNotSet");
        if (launcherUpdateManifest == null) return R("UpdateNotChecked");
        if (!IsLauncherRemoteNewerThanLocal()) return R("UpdateUpToDate");
        return R("UpdateAvailable");
    }

    private string? GetLauncherVersion()
    {
        try
        {
            var v = typeof(App).Assembly.GetName().Version;
            if (v == null) return null;
            return $"{v.Major}.{v.Minor}.{v.Build}";
        }
        catch
        {
            return null;
        }
    }

    private bool IsLauncherRemoteNewerThanLocal()
    {
        var remote = launcherUpdateManifest?.Version;
        if (string.IsNullOrWhiteSpace(remote)) return false;

        var local = GetLauncherVersion();
        if (string.IsNullOrWhiteSpace(local)) return true;

        return CompareVersion(remote.Trim(), local.Trim()) > 0;
    }

    private string BuildPsUpdateStatusText()
    {
        if (!string.IsNullOrWhiteSpace(psUpdateStatusOverride)) return psUpdateStatusOverride!;
        if (psUpdateInstalling) return R("UpdateInstalling");
        if (psUpdateChecking) return R("UpdateChecking");
        if (string.IsNullOrWhiteSpace(settings.UpdateUrl)) return R("UpdateUrlNotSet");
        if (psUpdateManifest == null) return R("UpdateNotChecked");
        if (!IsRemoteNewerThanLocal()) return R("UpdateUpToDate");
        return R("UpdateAvailable");
    }

    private bool IsRemoteNewerThanLocal()
    {
        var remote = psUpdateManifest?.Version;
        if (string.IsNullOrWhiteSpace(remote)) return false;

        var local = ReadLocalPsVersion();
        if (string.IsNullOrWhiteSpace(local)) return true;

        return CompareVersion(remote.Trim(), local.Trim()) > 0;
    }

    private static int CompareVersion(string a, string b)
    {
        if (TryParseNumericVersion(a, out var aa) && TryParseNumericVersion(b, out var bb))
        {
            var len = Math.Max(aa.Length, bb.Length);
            for (var i = 0; i < len; i++)
            {
                var av = i < aa.Length ? aa[i] : 0;
                var bv = i < bb.Length ? bb[i] : 0;
                if (av != bv) return av.CompareTo(bv);
            }
            return 0;
        }

        return string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryParseNumericVersion(string version, out int[] parts)
    {
        parts = Array.Empty<int>();
        var trimmed = version.Trim();
        if (trimmed.Length == 0) return false;

        var raw = trimmed.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (raw.Length == 0) return false;

        var list = new List<int>(raw.Length);
        foreach (var p in raw)
        {
            if (!int.TryParse(p, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)) return false;
            list.Add(n);
        }

        parts = list.ToArray();
        return true;
    }

    private string? ReadLocalPsVersion()
    {
        try
        {
            var serverExePath = ResolveServerExePath();
            if (string.IsNullOrWhiteSpace(serverExePath) || !File.Exists(serverExePath))
            {
                return null;
            }

            var dir = Path.GetDirectoryName(serverExePath);
            if (string.IsNullOrWhiteSpace(dir)) return null;

            var versionPath = Path.Combine(dir, "ps-version.json");
            if (File.Exists(versionPath))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(versionPath));
                if (doc.RootElement.TryGetProperty("version", out var v) && v.ValueKind == JsonValueKind.String)
                {
                    var s = v.GetString();
                    if (!string.IsNullOrWhiteSpace(s)) return s.Trim();
                }
            }

            var exeName = Path.GetFileNameWithoutExtension(serverExePath);
            if (!string.IsNullOrWhiteSpace(exeName))
            {
                var fromName = ExtractVersionFromString(exeName);
                if (!string.IsNullOrWhiteSpace(fromName)) return fromName!;
            }

            var vi = FileVersionInfo.GetVersionInfo(serverExePath);
            var fallback = vi.ProductVersion ?? vi.FileVersion;
            if (!string.IsNullOrWhiteSpace(fallback)) return fallback.Trim();

            return null;
        }
        catch
        {
            return null;
        }
    }

    private static string? ExtractVersionFromString(string input)
    {
        // Very small heuristic: find first a.b.c[.d] sequence.
        var span = input.AsSpan();
        for (var i = 0; i < span.Length; i++)
        {
            if (!char.IsDigit(span[i])) continue;
            var start = i;
            var dots = 0;
            while (i < span.Length && (char.IsDigit(span[i]) || span[i] == '.'))
            {
                if (span[i] == '.') dots++;
                i++;
            }
            var candidate = span.Slice(start, i - start).ToString().Trim('.');
            if (dots >= 1 && candidate.Contains('.') && candidate.Length <= 32)
            {
                return candidate;
            }
        }

        return null;
    }

    private void UpdateStatus(string message)
    {
        StatusText.Text = message;
    }

    private async void OnApplyPatchAndLaunch(object sender, RoutedEventArgs e)
    {
        StartButton.IsEnabled = false;
        try
        {
            await ApplyPatchAndLaunchAsync();
        }
        finally
        {
            StartButton.IsEnabled = true;
        }
    }

    private async Task ApplyPatchAndLaunchAsync()
    {
        ReadSettingsFromUi();
        settings.ApplyDefaults();

        if (string.IsNullOrWhiteSpace(settings.GameExePath) || !File.Exists(settings.GameExePath))
        {
            UpdateStatus(R("StatusGameExeMissing"));
            return;
        }

        if (string.IsNullOrWhiteSpace(settings.PatchSourcePath) || !Directory.Exists(settings.PatchSourcePath))
        {
            UpdateStatus(R("StatusPatchMissing"));
            return;
        }

        var gameDir = Path.GetDirectoryName(settings.GameExePath) ?? "";
        if (string.IsNullOrWhiteSpace(gameDir))
        {
            UpdateStatus(R("StatusGameDirInvalid"));
            return;
        }

        UpdateStatus(R("StatusCopyingPatch"));
        await Task.Run(() => CopyDirectory(settings.PatchSourcePath, gameDir));

        UpdateServerSummary();
        await EnsureServerRunningAsync();

        var pluginPath = GetAccountPlatNativePath(gameDir);
        if (pluginPath != null)
        {
            UpdateStatus(R("StatusRenamingPlugin"));
            if (!TryBackupAccountPlatNative(pluginPath, out var renameError))
            {
                UpdateStatus(RFormat("StatusPluginRenameFailedFormat", renameError ?? R("UnknownError")));
                return;
            }
        }

        var launcherPath = FindLauncherExe(gameDir);
        if (launcherPath == null)
        {
            UpdateStatus(R("StatusLauncherNotFound"));
            return;
        }

        if (launcherProcess is { HasExited: false })
        {
            UpdateStatus(R("StatusLauncherAlreadyRunning"));
            return;
        }

        try
        {
            launcherProcess = StartProcess(launcherPath, runAsAdmin: true);
        }
        catch (Win32Exception ex)
        {
            UpdateStatus(RFormat("StatusLaunchCanceledFormat", ex.Message));
            return;
        }

        if (launcherProcess == null)
        {
            UpdateStatus(R("StatusLauncherFailed"));
            return;
        }

        UpdateStatus(R("StatusLaunchSuccess"));
    }

    private static string? FindLauncherExe(string gameDir)
    {
        var candidate = Path.Combine(gameDir, "Launcher.exe");
        if (File.Exists(candidate)) return candidate;
        candidate = Path.Combine(gameDir, "launcher.exe");
        if (File.Exists(candidate)) return candidate;
        return null;
    }

    private static Process? StartProcess(string exePath, bool runAsAdmin)
    {
        var info = new ProcessStartInfo
        {
            FileName = exePath,
            WorkingDirectory = Path.GetDirectoryName(exePath) ?? Environment.CurrentDirectory,
            UseShellExecute = true,
        };
        if (runAsAdmin)
        {
            info.Verb = "runas";
        }
        return Process.Start(info);
    }

    private static string? GetAccountPlatNativePath(string gameDir)
    {
        var path = Path.Combine(gameDir, "StarRail_Data", "Plugins", "x86_64", "AccountPlatNative.dll");
        return File.Exists(path) ? path : null;
    }

    private static bool TryBackupAccountPlatNative(string dllPath, out string? errorMessage)
    {
        try
        {
            var backupPath = dllPath + ".backup";
            if (File.Exists(backupPath)) File.Delete(backupPath);
            File.Move(dllPath, backupPath);
            errorMessage = null;
            return true;
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            return false;
        }
    }

    private static void CopyDirectory(string sourceDir, string destDir)
    {
        var source = new DirectoryInfo(sourceDir);
        if (!source.Exists) throw new DirectoryNotFoundException(sourceDir);

        Directory.CreateDirectory(destDir);

        foreach (var file in source.GetFiles("*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceDir, file.FullName);
            var target = Path.Combine(destDir, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target) ?? destDir);
            file.CopyTo(target, true);
        }
    }

    private async void OnBrowseGameExe(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        InitializePicker(picker);
        picker.FileTypeFilter.Add(".exe");
        var file = await picker.PickSingleFileAsync();
        if (file != null) GamePathBox.Text = file.Path;
    }

    private async void OnBrowsePatchDir(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker();
        InitializePicker(picker);
        picker.FileTypeFilter.Add("*");
        var folder = await picker.PickSingleFolderAsync();
        if (folder != null) PatchPathBox.Text = folder.Path;
    }

    private async void OnBrowseServerExe(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        InitializePicker(picker);
        picker.FileTypeFilter.Add(".exe");
        var file = await picker.PickSingleFileAsync();
        if (file != null)
        {
            ServerPathBox.Text = file.Path;
            settings.ServerExePath = file.Path;
            UpdateServerSummary();
        }
    }

    private async void OnBrowseBackgroundImage(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        InitializePicker(picker);
        picker.FileTypeFilter.Add(".png");
        picker.FileTypeFilter.Add(".jpg");
        picker.FileTypeFilter.Add(".jpeg");
        picker.FileTypeFilter.Add(".webp");
        var file = await picker.PickSingleFileAsync();
        if (file != null)
        {
            BackgroundPathBox.Text = file.Path;
            settings.BackgroundImagePath = file.Path;
            UpdateHomeBackground();
        }
    }

    private void InitializePicker(object picker)
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
    }

    private void OnSaveSettings(object sender, RoutedEventArgs e)
    {
        var previousLanguage = settings.LanguageTag;
        var previousUpdateUrl = settings.UpdateUrl;
        ReadSettingsFromUi();
        settings.ApplyDefaults();
        settings.Save();
        UpdateHomeSummary();
        UpdateHomeBackground();
        UpdateServerSummary();
        UpdateServerUi();
        UpdatePsUpdateUi();
        UpdateLauncherUpdateUi();
        UpdateStatus(R("StatusSettingsSaved"));
        _ = LoadAnnouncementAsync();
        if (!string.Equals(previousUpdateUrl, settings.UpdateUrl, StringComparison.OrdinalIgnoreCase))
        {
            psUpdateManifest = null;
            _ = CheckPsUpdateAsync(force: true);
            launcherUpdateManifest = null;
            _ = CheckLauncherUpdateAsync(force: true);
        }

        if (!string.Equals(previousLanguage, settings.LanguageTag, StringComparison.OrdinalIgnoreCase))
        {
            App.ApplyLanguageOverride(settings.LanguageTag);
            ReloadForLanguageChange();
        }
    }

    private void OnOpenSettings(object sender, RoutedEventArgs e)
    {
        NavView.SelectedItem = SettingsNavItem;
        ShowView("settings");
    }

    private async void OnRefreshAnnouncement(object sender, RoutedEventArgs e)
    {
        ReadSettingsFromUi();
        settings.ApplyDefaults();
        await LoadAnnouncementAsync();
    }

    private async void OnOpenTools(object sender, RoutedEventArgs e)
    {
        await OpenUrlAsync("https://srtools.neonteam.dev/");
    }

    private async void OnOpenDiscord(object sender, RoutedEventArgs e)
    {
        await OpenUrlAsync("https://discord.gg/castoriceps");
    }

    private static async Task OpenUrlAsync(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            await Launcher.LaunchUriAsync(uri);
        }
    }

    private void SelectLanguage(string languageTag)
    {
        foreach (var item in LanguageBox.Items)
        {
            if (item is ComboBoxItem comboItem &&
                comboItem.Tag is string tag &&
                string.Equals(tag, languageTag, StringComparison.OrdinalIgnoreCase))
            {
                LanguageBox.SelectedItem = comboItem;
                return;
            }
        }

        LanguageBox.SelectedIndex = 0;
    }

    private string GetSelectedLanguageTag()
    {
        if (LanguageBox.SelectedItem is ComboBoxItem comboItem && comboItem.Tag is string tag)
        {
            return tag;
        }

        return LanguageEnglishTag;
    }

    private void ReloadForLanguageChange()
    {
        if (Frame == null) return;
        _ = DispatcherQueue.TryEnqueue(() => Frame.Navigate(typeof(MainPage)));
    }

    private void UpdateHomeBackground()
    {
        var path = settings.BackgroundImagePath;
        if (string.IsNullOrWhiteSpace(path))
        {
            HomeBackgroundBrush.ImageSource = null;
            return;
        }

        try
        {
            Uri uri;
            if (File.Exists(path))
            {
                uri = new Uri(path, UriKind.Absolute);
            }
            else if (Uri.TryCreate(path, UriKind.Absolute, out var parsed))
            {
                uri = parsed;
            }
            else if (Uri.TryCreate("ms-appx:///" + path.TrimStart('\\', '/'), UriKind.Absolute, out var msAppx))
            {
                uri = msAppx;
            }
            else
            {
                uri = new Uri("ms-appx:///Assets/DefaultBackground.webp");
            }

            HomeBackgroundBrush.ImageSource = new BitmapImage(uri);
        }
        catch
        {
            HomeBackgroundBrush.ImageSource = null;
        }
    }

    private void UpdateServerUi()
    {
        var isRunning = serverProcess is { HasExited: false };
        ServerStartButton.IsEnabled = !isRunning;
        ServerStopButton.IsEnabled = isRunning;
        ServerStatusText.Text = isRunning
            ? RFormat("ServerRunningFormat", serverProcess!.Id)
            : R("ServerStopped");
        UpdateLogViews();
    }

    private async void OnStartServer(object sender, RoutedEventArgs e)
    {
        ReadSettingsFromUi();
        settings.ApplyDefaults();
        settings.Save();
        UpdateServerSummary();
        await EnsureServerRunningAsync();
    }

    private void OnStopServer(object sender, RoutedEventArgs e)
    {
        StopServer();
    }

    private async Task EnsureServerRunningAsync()
    {
        if (serverProcess is { HasExited: false })
        {
            UpdateServerUi();
            return;
        }

        var serverExePath = ResolveServerExePath();
        if (!File.Exists(serverExePath))
        {
            if (!TryRestoreServerFromCache(serverExePath, out var restoredZip))
            {
                AppendServerLog(R("ServerExeMissing"));
                UpdateServerUi();
                return;
            }

            AppendServerLog(RFormat("ServerRestoredFromCacheFormat", Path.GetFileName(restoredZip)));
        }

        AppendServerLog(RFormat("ServerStartingFormat", serverExePath));
        try
        {
            serverProcess = StartServerProcess(serverExePath);
        }
        catch (Exception ex)
        {
            AppendServerLog(RFormat("ServerStartFailedFormat", ex.Message));
            serverProcess = null;
            UpdateServerUi();
            return;
        }

        UpdateServerUi();
        await Task.Delay(200);
        UpdateServerUi();
    }

    private Process StartServerProcess(string exePath)
    {
        var workdir = Path.GetDirectoryName(exePath) ?? Environment.CurrentDirectory;
        var info = new ProcessStartInfo
        {
            FileName = exePath,
            WorkingDirectory = workdir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        info.Environment["CASTORICEPS_TRACE_PACKETS"] = "1";
        info.Environment["CASTORICEPS_SCENE_DEBUG"] = "1";

        var process = new Process { StartInfo = info, EnableRaisingEvents = true };
        process.Exited += (_, _) =>
        {
            AppendServerLog(RFormat("ServerExitedFormat", process.ExitCode));
            _ = DispatcherQueue.TryEnqueue(UpdateServerUi);
        };
        process.OutputDataReceived += (_, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data)) AppendServerLog(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data)) AppendServerLog(e.Data);
        };

        if (!process.Start())
        {
            throw new InvalidOperationException("Failed to start process.");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return process;
    }

    private void StopServer()
    {
        if (serverProcess is not { HasExited: false })
        {
            serverProcess = null;
            UpdateServerUi();
            return;
        }

        try
        {
            serverProcess.Kill(entireProcessTree: true);
            AppendServerLog(R("ServerStoppedByUser"));
        }
        catch (Exception ex)
        {
            AppendServerLog(RFormat("ServerStopFailedFormat", ex.Message));
        }
        finally
        {
            serverProcess = null;
            UpdateServerUi();
        }
    }

    private async void OnCheckPsUpdate(object sender, RoutedEventArgs e)
    {
        ReadSettingsFromUi();
        settings.ApplyDefaults();
        settings.Save();

        await CheckPsUpdateAsync(force: true);
    }

    private async void OnCheckLauncherUpdate(object sender, RoutedEventArgs e)
    {
        ReadSettingsFromUi();
        settings.ApplyDefaults();
        settings.Save();

        await CheckLauncherUpdateAsync(force: true);
    }

    private async Task CheckLauncherUpdateAsync(bool force = false)
    {
        if (launcherUpdateChecking) return;
        if (!force && launcherUpdateManifest != null) return;

        launcherUpdateChecking = true;
        try
        {
            launcherUpdateStatusOverride = null;
            UpdateLauncherUpdateUi();

            if (string.IsNullOrWhiteSpace(settings.UpdateUrl))
            {
                launcherUpdateManifest = null;
                return;
            }

            var manifestUrl = BuildLauncherManifestUrl(settings.UpdateUrl);
            if (!Uri.TryCreate(manifestUrl, UriKind.Absolute, out var manifestUri) || string.IsNullOrWhiteSpace(manifestUri.Host))
            {
                launcherUpdateManifest = null;
                launcherUpdateStatusOverride = RFormat("UpdateInvalidUrlFormat", manifestUrl);
                return;
            }

            using var resp = await httpClient.GetAsync(manifestUri, HttpCompletionOption.ResponseHeadersRead);
            resp.EnsureSuccessStatusCode();
            var json = await resp.Content.ReadAsStringAsync();
            launcherUpdateManifest = JsonSerializer.Deserialize<LauncherUpdateManifest>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            });
        }
        catch (Exception ex)
        {
            launcherUpdateManifest = null;
            launcherUpdateStatusOverride = RFormat("UpdateCheckFailedFormat", ex.Message);
        }
        finally
        {
            launcherUpdateChecking = false;
            UpdateLauncherUpdateUi();
        }
    }

    private async void OnUpdateLauncher(object sender, RoutedEventArgs e)
    {
        ReadSettingsFromUi();
        settings.ApplyDefaults();
        settings.Save();

        await InstallLauncherUpdateAsync();
    }

    private async Task InstallLauncherUpdateAsync()
    {
        if (launcherUpdateInstalling) return;
        launcherUpdateInstalling = true;
        try
        {
            launcherUpdateStatusOverride = null;
            launcherUpdateDownloading = false;
            launcherUpdateDownloadedBytes = 0;
            launcherUpdateTotalBytes = null;
            launcherUpdateDownloadPath = null;
            UpdateLauncherUpdateUi();

            if (launcherUpdateManifest == null)
            {
                await CheckLauncherUpdateAsync(force: true);
            }

            if (launcherUpdateManifest?.Package?.HasValidUrl != true)
            {
                launcherUpdateStatusOverride = R("UpdateNoPackage");
                return;
            }

            var baseUri = GetUpdateBaseUri(settings.UpdateUrl);
            var packageUri = new Uri(baseUri, launcherUpdateManifest.Package!.Url!);

            var downloadsDir = GetUpdateDownloadsDir();
            Directory.CreateDirectory(downloadsDir);
            var tmpZip = Path.Combine(downloadsDir, $"CastoriceLauncher-{launcherUpdateManifest.Version}-{Guid.NewGuid():N}.zip");
            var tmpExtract = Path.Combine(Path.GetTempPath(), $"CastoriceLauncher-extract-{Guid.NewGuid():N}");

            launcherUpdateStatusOverride = R("UpdateDownloading");
            launcherUpdateDownloading = true;
            launcherUpdateDownloadPath = tmpZip;
            UpdateLauncherUpdateUi();

            await DownloadToFileAsync(packageUri, tmpZip, onProgress: (received, total) =>
            {
                launcherUpdateDownloadedBytes = received;
                launcherUpdateTotalBytes = total;
                _ = DispatcherQueue.TryEnqueue(UpdateLauncherUpdateUi);
            });

            if (launcherUpdateManifest.Package.Size > 0)
            {
                var actualSize = new FileInfo(tmpZip).Length;
                if (actualSize != launcherUpdateManifest.Package.Size)
                {
                    launcherUpdateStatusOverride = RFormat("UpdateSizeMismatchFormat", actualSize, launcherUpdateManifest.Package.Size);
                    return;
                }
            }

            if (!string.IsNullOrWhiteSpace(launcherUpdateManifest.Package.Sha256))
            {
                launcherUpdateStatusOverride = R("UpdateVerifying");
                UpdateLauncherUpdateUi();
                var actual = ComputeSha256Hex(tmpZip);
                if (!actual.Equals(launcherUpdateManifest.Package.Sha256.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    launcherUpdateStatusOverride = RFormat("UpdateHashMismatchFormat", actual);
                    return;
                }
            }

            launcherUpdateStatusOverride = R("UpdateExtracting");
            UpdateLauncherUpdateUi();
            Directory.CreateDirectory(tmpExtract);
            ExtractZipToDirectory(tmpZip, tmpExtract);

            var sourceRoot = TryGetSingleRootDir(tmpExtract) ?? tmpExtract;
            var targetDir = AppContext.BaseDirectory.TrimEnd('\\', '/');

            launcherUpdateStatusOverride = R("UpdateInstalling");
            UpdateLauncherUpdateUi();
            StartLauncherSelfUpdater(sourceRoot, targetDir);
        }
        catch (Exception ex)
        {
            launcherUpdateStatusOverride = RFormat("UpdateInstallFailedFormat", ex.Message);
            UpdateLauncherUpdateUi();
        }
        finally
        {
            launcherUpdateInstalling = false;
            launcherUpdateDownloading = false;
            try { CleanupOldUpdateZips(GetUpdateDownloadsDir(), keep: 3, pattern: "CastoriceLauncher-*.zip"); } catch { }
            UpdateLauncherUpdateUi();
        }
    }

    private async Task CheckPsUpdateAsync(bool force = false)
    {
        if (psUpdateChecking) return;
        if (!force && psUpdateManifest != null) return;

        psUpdateChecking = true;
        try
        {
            psUpdateStatusOverride = null;
            UpdatePsUpdateUi();

            if (string.IsNullOrWhiteSpace(settings.UpdateUrl))
            {
                psUpdateManifest = null;
                return;
            }

            var manifestUrl = BuildUpdateManifestUrl(settings.UpdateUrl);
            if (!Uri.TryCreate(manifestUrl, UriKind.Absolute, out var manifestUri) || string.IsNullOrWhiteSpace(manifestUri.Host))
            {
                psUpdateManifest = null;
                psUpdateStatusOverride = RFormat("UpdateInvalidUrlFormat", manifestUrl);
                return;
            }

            using var resp = await httpClient.GetAsync(manifestUri, HttpCompletionOption.ResponseHeadersRead);
            resp.EnsureSuccessStatusCode();
            var json = await resp.Content.ReadAsStringAsync();
            psUpdateManifest = JsonSerializer.Deserialize<PsUpdateManifest>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            });
        }
        catch (Exception ex)
        {
            psUpdateManifest = null;
            psUpdateStatusOverride = RFormat("UpdateCheckFailedFormat", ex.Message);
        }
        finally
        {
            psUpdateChecking = false;
            UpdatePsUpdateUi();
        }
    }

    private async void OnUpdatePs(object sender, RoutedEventArgs e)
    {
        ReadSettingsFromUi();
        settings.ApplyDefaults();
        settings.Save();

        await InstallPsUpdateAsync();
    }

    private async Task InstallPsUpdateAsync()
    {
        if (psUpdateInstalling) return;

        psUpdateInstalling = true;
        var wasRunning = serverProcess is { HasExited: false };
        try
        {
            psUpdateStatusOverride = null;
            psUpdateDownloading = false;
            psUpdateDownloadedBytes = 0;
            psUpdateTotalBytes = null;
            psUpdateDownloadPath = null;
            UpdatePsUpdateUi();

            if (psUpdateManifest == null)
            {
                await CheckPsUpdateAsync(force: true);
            }

            if (psUpdateManifest?.Package?.HasValidUrl != true)
            {
                psUpdateStatusOverride = R("UpdateNoPackage");
                return;
            }

            var serverExePath = ResolveServerExePath();
            var serverDir = Path.GetDirectoryName(serverExePath);
            if (string.IsNullOrWhiteSpace(serverDir))
            {
                psUpdateStatusOverride = R("UpdateServerDirInvalid");
                return;
            }

            Directory.CreateDirectory(serverDir);
            StopServer();

            var baseUri = GetUpdateBaseUri(settings.UpdateUrl);
            var packageUri = new Uri(baseUri, psUpdateManifest.Package!.Url!);

            var downloadsDir = GetUpdateDownloadsDir();
            Directory.CreateDirectory(downloadsDir);
            var tmpZip = Path.Combine(downloadsDir, $"CastoricePS-{psUpdateManifest.Version}-{Guid.NewGuid():N}.zip");
            var tmpExtract = Path.Combine(Path.GetTempPath(), $"CastoricePS-extract-{Guid.NewGuid():N}");

            try
            {
                psUpdateStatusOverride = R("UpdateDownloading");
                psUpdateDownloading = true;
                psUpdateDownloadPath = tmpZip;
                UpdatePsUpdateUi();
                await DownloadToFileAsync(packageUri, tmpZip, onProgress: (received, total) =>
                {
                    psUpdateDownloadedBytes = received;
                    psUpdateTotalBytes = total;
                    _ = DispatcherQueue.TryEnqueue(UpdatePsUpdateUi);
                });

                if (psUpdateManifest.Package.Size > 0)
                {
                    var actualSize = new FileInfo(tmpZip).Length;
                    if (actualSize != psUpdateManifest.Package.Size)
                    {
                        psUpdateStatusOverride = RFormat("UpdateSizeMismatchFormat", actualSize, psUpdateManifest.Package.Size);
                        return;
                    }
                }

                if (!string.IsNullOrWhiteSpace(psUpdateManifest.Package.Sha256))
                {
                    psUpdateStatusOverride = R("UpdateVerifying");
                    UpdatePsUpdateUi();
                    var actual = ComputeSha256Hex(tmpZip);
                    if (!actual.Equals(psUpdateManifest.Package.Sha256.Trim(), StringComparison.OrdinalIgnoreCase))
                    {
                        psUpdateStatusOverride = RFormat("UpdateHashMismatchFormat", actual);
                        return;
                    }
                }

                psUpdateStatusOverride = R("UpdateExtracting");
                UpdatePsUpdateUi();
                Directory.CreateDirectory(tmpExtract);
                ExtractZipToDirectory(tmpZip, tmpExtract);

                var sourceRoot = TryGetSingleRootDir(tmpExtract) ?? tmpExtract;

                psUpdateStatusOverride = R("UpdateInstalling");
                UpdatePsUpdateUi();
                CopyDirectory(sourceRoot, serverDir);

                psUpdateManifest = null;
                psUpdateStatusOverride = R("UpdateInstalled");
            }
            finally
            {
                psUpdateDownloading = false;
                _ = DispatcherQueue.TryEnqueue(UpdatePsUpdateUi);
                try { if (Directory.Exists(tmpExtract)) Directory.Delete(tmpExtract, recursive: true); } catch { }
                try { CleanupOldUpdateZips(GetUpdateDownloadsDir(), keep: 3); } catch { }
            }
        }
        catch (Exception ex)
        {
            psUpdateStatusOverride = RFormat("UpdateInstallFailedFormat", ex.Message);
        }
        finally
        {
            psUpdateInstalling = false;
            psUpdateDownloading = false;
            if (wasRunning)
            {
                try { await EnsureServerRunningAsync(); } catch { }
            }
            UpdatePsUpdateUi();
        }
    }

    private static string? TryGetSingleRootDir(string dir)
    {
        try
        {
            var files = Directory.GetFiles(dir);
            var dirs = Directory.GetDirectories(dir);
            if (files.Length == 0 && dirs.Length == 1) return dirs[0];
        }
        catch
        {
            return null;
        }

        return null;
    }

    private async Task DownloadToFileAsync(Uri uri, string destPath, Action<long, long?>? onProgress = null)
    {
        using var resp = await httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead);
        resp.EnsureSuccessStatusCode();
        var total = resp.Content.Headers.ContentLength;
        await using var src = await resp.Content.ReadAsStreamAsync();
        await using var dst = File.Create(destPath);

        var buffer = new byte[1024 * 64];
        long received = 0;
        var lastReport = Environment.TickCount64;
        while (true)
        {
            var read = await src.ReadAsync(buffer);
            if (read <= 0) break;
            await dst.WriteAsync(buffer.AsMemory(0, read));
            received += read;

            if (onProgress != null)
            {
                var now = Environment.TickCount64;
                if (now - lastReport >= 120)
                {
                    lastReport = now;
                    onProgress(received, total);
                }
            }
        }

        onProgress?.Invoke(received, total);
    }

    private static string ComputeSha256Hex(string path)
    {
        using var sha = SHA256.Create();
        using var fs = File.OpenRead(path);
        var hash = sha.ComputeHash(fs);
        return Convert.ToHexString(hash);
    }

    private string ResolveServerExePath()
    {
        if (!string.IsNullOrWhiteSpace(settings.ServerExePath))
        {
            return settings.ServerExePath;
        }

        var dir = Path.Combine(AppContext.BaseDirectory, "server");
        Directory.CreateDirectory(dir);
        var exe = Path.Combine(dir, "CastoricePS.exe");
        settings.ServerExePath = exe;
        ServerPathBox.Text = exe;
        UpdateServerSummary();
        return exe;
    }

    private static Uri GetUpdateBaseUri(string baseOrUrl)
    {
        var withScheme = EnsureHasScheme(baseOrUrl);
        if (!Uri.TryCreate(withScheme, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException("Invalid update URL.");
        }

        var builder = new UriBuilder(uri)
        {
            Path = "/",
            Query = "",
            Fragment = "",
        };
        return builder.Uri;
    }

    private static string EnsureHasScheme(string url)
    {
        var trimmed = url.Trim();
        return trimmed.Contains("://", StringComparison.Ordinal) ? trimmed : "http://" + trimmed;
    }

    private string BuildUpdateManifestUrl(string baseOrUrl)
    {
        var withScheme = EnsureHasScheme(baseOrUrl);
        if (!Uri.TryCreate(withScheme, UriKind.Absolute, out var uri))
        {
            return baseOrUrl;
        }

        // If host/root, use our manifest endpoint.
        if (string.IsNullOrWhiteSpace(uri.AbsolutePath) || uri.AbsolutePath == "/")
        {
            uri = new UriBuilder(uri) { Path = "/api/ps/manifest" }.Uri;
        }

        if (uri.AbsolutePath.EndsWith("/api/ps/manifest", StringComparison.OrdinalIgnoreCase))
        {
            var builder = new UriBuilder(uri);
            var query = (builder.Query ?? "").TrimStart('?');
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(query))
            {
                try
                {
                    var decoder = new WwwFormUrlDecoder(query);
                    foreach (var entry in decoder) map[entry.Name] = entry.Value;
                }
                catch { }
            }
            if (!map.ContainsKey("channel")) map["channel"] = "stable";
            builder.Query = string.Join("&", map.Select(kv =>
                $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value ?? "")}"));
            return builder.Uri.ToString();
        }

        return uri.ToString();
    }

    private static void ExtractZipToDirectory(string zipPath, string destDir)
    {
        using var fs = File.OpenRead(zipPath);
        using var archive = new ZipArchive(fs, ZipArchiveMode.Read);
        foreach (var entry in archive.Entries)
        {
            var fullPath = Path.GetFullPath(Path.Combine(destDir, entry.FullName));
            var root = Path.GetFullPath(destDir) + Path.DirectorySeparatorChar;
            if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Zip entry path traversal detected.");
            }

            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(fullPath);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            entry.ExtractToFile(fullPath, overwrite: true);
        }
    }

    private static string FormatBytes(long bytes)
    {
        const double kb = 1024;
        const double mb = kb * 1024;
        const double gb = mb * 1024;
        if (bytes >= gb) return $"{bytes / gb:0.##} GB";
        if (bytes >= mb) return $"{bytes / mb:0.##} MB";
        if (bytes >= kb) return $"{bytes / kb:0.##} KB";
        return $"{bytes} B";
    }

    private static string GetUpdateDownloadsDir()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CastoriceLauncher",
            "downloads");
    }

    private bool TryRestoreServerFromCache(string serverExePath, out string restoredFromZip)
    {
        restoredFromZip = "";
        try
        {
            var downloadsDir = GetUpdateDownloadsDir();
            var zip = GetLatestUpdateZipPath(downloadsDir);
            if (zip == null || !File.Exists(zip)) return false;

            var serverDir = Path.GetDirectoryName(serverExePath);
            if (string.IsNullOrWhiteSpace(serverDir)) return false;
            Directory.CreateDirectory(serverDir);

            var tmpExtract = Path.Combine(Path.GetTempPath(), $"CastoricePS-restore-{Guid.NewGuid():N}");
            Directory.CreateDirectory(tmpExtract);
            try
            {
                ExtractZipToDirectory(zip, tmpExtract);
                var sourceRoot = TryGetSingleRootDir(tmpExtract) ?? tmpExtract;
                CopyDirectory(sourceRoot, serverDir);
            }
            finally
            {
                try { if (Directory.Exists(tmpExtract)) Directory.Delete(tmpExtract, recursive: true); } catch { }
            }

            if (!File.Exists(serverExePath)) return false;
            restoredFromZip = zip;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string? GetLatestUpdateZipPath(string downloadsDir)
    {
        try
        {
            if (!Directory.Exists(downloadsDir)) return null;
            var files = Directory.GetFiles(downloadsDir, "CastoricePS-*.zip", SearchOption.TopDirectoryOnly);
            if (files.Length == 0) return null;
            return files
                .Select(p => new FileInfo(p))
                .OrderByDescending(fi => fi.LastWriteTimeUtc)
                .First()
                .FullName;
        }
        catch
        {
            return null;
        }
    }

    private static string? GetLatestUpdateZipPath(string downloadsDir, string pattern)
    {
        try
        {
            if (!Directory.Exists(downloadsDir)) return null;
            var files = Directory.GetFiles(downloadsDir, pattern, SearchOption.TopDirectoryOnly);
            if (files.Length == 0) return null;
            return files
                .Select(p => new FileInfo(p))
                .OrderByDescending(fi => fi.LastWriteTimeUtc)
                .First()
                .FullName;
        }
        catch
        {
            return null;
        }
    }

    private static void CleanupOldUpdateZips(string downloadsDir, int keep, string pattern)
    {
        if (keep < 0) keep = 0;
        if (!Directory.Exists(downloadsDir)) return;
        var files = Directory.GetFiles(downloadsDir, pattern, SearchOption.TopDirectoryOnly)
            .Select(p => new FileInfo(p))
            .OrderByDescending(fi => fi.LastWriteTimeUtc)
            .ToList();
        for (var i = keep; i < files.Count; i++)
        {
            try { files[i].Delete(); } catch { }
        }
    }

    private string BuildLauncherManifestUrl(string baseOrUrl)
    {
        var withScheme = EnsureHasScheme(baseOrUrl);
        if (!Uri.TryCreate(withScheme, UriKind.Absolute, out var uri))
        {
            return baseOrUrl;
        }

        if (string.IsNullOrWhiteSpace(uri.AbsolutePath) || uri.AbsolutePath == "/")
        {
            uri = new UriBuilder(uri) { Path = "/api/launcher/manifest" }.Uri;
        }

        if (uri.AbsolutePath.EndsWith("/api/launcher/manifest", StringComparison.OrdinalIgnoreCase))
        {
            var builder = new UriBuilder(uri);
            var query = (builder.Query ?? "").TrimStart('?');
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(query))
            {
                try
                {
                    var decoder = new WwwFormUrlDecoder(query);
                    foreach (var entry in decoder) map[entry.Name] = entry.Value;
                }
                catch { }
            }
            if (!map.ContainsKey("channel")) map["channel"] = "stable";
            builder.Query = string.Join("&", map.Select(kv =>
                $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value ?? "")}"));
            return builder.Uri.ToString();
        }

        return uri.ToString();
    }

    private void StartLauncherSelfUpdater(string sourceDir, string targetDir)
    {
        var pid = Process.GetCurrentProcess().Id;
        var scriptPath = Path.Combine(Path.GetTempPath(), $"CastoriceLauncher-update-{Guid.NewGuid():N}.cmd");

        var exeName = Path.GetFileName(Environment.ProcessPath ?? "LauncherApp.exe");
        if (string.IsNullOrWhiteSpace(exeName)) exeName = "LauncherApp.exe";

        var lines = new[]
        {
            "@echo off",
            "setlocal enableextensions",
            $"set \"SRC={sourceDir}\"",
            $"set \"DST={targetDir}\"",
            $"set \"EXE={exeName}\"",
            $"set \"PID={pid}\"",
            ":wait",
            "tasklist /FI \"PID eq %PID%\" 2>NUL | find \"%PID%\" >NUL",
            "if %ERRORLEVEL%==0 (timeout /t 1 /nobreak >NUL & goto wait)",
            "robocopy \"%SRC%\" \"%DST%\" /E /R:2 /W:1 /NP /NFL /NDL",
            "start \"\" \"%DST%\\%EXE%\"",
            "del \"%~f0\"",
        };

        File.WriteAllLines(scriptPath, lines, Encoding.UTF8);

        var startInfo = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c \"{scriptPath}\"",
            CreateNoWindow = true,
            UseShellExecute = false,
            WorkingDirectory = Path.GetTempPath(),
        };

        Process.Start(startInfo);
        Application.Current.Exit();
    }

    private sealed class LauncherUpdateManifest
    {
        public string? Product { get; set; }
        public string? Version { get; set; }
        public string? Notes { get; set; }
        public PsUpdatePackage? Package { get; set; }
    }

    private static void CleanupOldUpdateZips(string downloadsDir, int keep)
    {
        CleanupOldUpdateZips(downloadsDir, keep, "CastoricePS-*.zip");
    }

    private sealed class PsUpdateManifest
    {
        public string? Product { get; set; }
        public string? Version { get; set; }
        public string? Notes { get; set; }
        public PsUpdatePackage? Package { get; set; }
    }

    private sealed class PsUpdatePackage
    {
        public string? Url { get; set; }
        public string? Sha256 { get; set; }
        public long Size { get; set; }
        public bool HasValidUrl => !string.IsNullOrWhiteSpace(Url);
    }

    private void AppendServerLog(string line)
    {
        lock (serverLogLock)
        {
            serverLog.AppendLine(line);
            const int maxChars = 120_000;
            if (serverLog.Length > maxChars)
            {
                serverLog.Remove(0, serverLog.Length - maxChars);
            }
        }

        lock (serverLogLock)
        {
            if (serverLogUpdateScheduled) return;
            serverLogUpdateScheduled = true;
        }

        _ = DispatcherQueue.TryEnqueue(() =>
        {
            lock (serverLogLock)
            {
                serverLogUpdateScheduled = false;
            }
            UpdateLogViews();
        });
    }

    private void UpdateLogViews()
    {
        var smallAtBottom = IsTextBoxNearBottom(ServerLogBox);
        var largeAtBottom = IsTextBoxNearBottom(ServerLogBoxLarge);

        string text;
        lock (serverLogLock)
        {
            text = serverLog.ToString();
        }

        ServerLogBox.Text = text;
        if (smallAtBottom)
        {
            ServerLogBox.SelectionStart = text.Length;
            ServerLogBox.SelectionLength = 0;
            ScrollTextBoxToBottom(ServerLogBox);
        }

        ServerLogBoxLarge.Text = text;
        if (largeAtBottom)
        {
            ServerLogBoxLarge.SelectionStart = text.Length;
            ServerLogBoxLarge.SelectionLength = 0;
            ScrollTextBoxToBottom(ServerLogBoxLarge);
        }
    }

    private static bool IsTextBoxNearBottom(TextBox textBox)
    {
        var sv = FindDescendant<ScrollViewer>(textBox);
        if (sv == null) return true;
        return sv.ScrollableHeight <= 0 || sv.VerticalOffset >= sv.ScrollableHeight - 8;
    }

    private static void ScrollTextBoxToBottom(TextBox textBox)
    {
        var sv = FindDescendant<ScrollViewer>(textBox);
        if (sv == null) return;
        _ = sv.ChangeView(null, sv.ScrollableHeight, null, disableAnimation: true);
    }

    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T typed) return typed;
            var found = FindDescendant<T>(child);
            if (found != null) return found;
        }
        return null;
    }

    private void SetLogsStatus(string message)
    {
        LogsStatusText.Text = message;
    }

    private void OnCopyLogs(object sender, RoutedEventArgs e)
    {
        try
        {
            string text;
            lock (serverLogLock)
            {
                text = serverLog.ToString();
            }

            var data = new DataPackage();
            data.SetText(text);
            Clipboard.SetContent(data);
            SetLogsStatus(R("LogsCopied"));
        }
        catch (Exception ex)
        {
            SetLogsStatus(RFormat("LogsCopyFailedFormat", ex.Message));
        }
    }

    private async void OnExportLogs(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FileSavePicker();
            InitializePicker(picker);
            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            picker.SuggestedFileName = "logs";
            picker.FileTypeChoices.Add("Text", new List<string> { ".txt" });

            var file = await picker.PickSaveFileAsync();
            if (file == null)
            {
                SetLogsStatus(R("LogsExportCanceled"));
                return;
            }

            string text;
            lock (serverLogLock)
            {
                text = serverLog.ToString();
            }

            await File.WriteAllTextAsync(file.Path, text, Encoding.UTF8);
            SetLogsStatus(RFormat("LogsExportedFormat", file.Path));
        }
        catch (Exception ex)
        {
            SetLogsStatus(RFormat("LogsExportFailedFormat", ex.Message));
        }
    }

    private void OnClearLogs(object sender, RoutedEventArgs e)
    {
        lock (serverLogLock)
        {
            serverLog.Clear();
        }
        UpdateLogViews();
        SetLogsStatus(R("LogsCleared"));
    }

    private void UpdateAnnouncementDefaults()
    {
        AnnouncementTitleText.Text = R("AnnouncementDefaultTitle");
        AnnouncementBodyText.Text = R("AnnouncementDefaultBody");
        AnnouncementMetaText.Text = R("AnnouncementDefaultMeta");
    }

    private async Task LoadAnnouncementAsync()
    {
        if (isAnnouncementLoading) return;
        isAnnouncementLoading = true;
        try
        {
            var url = BuildNoticeUrl(settings.NoticeUrl);
            if (string.IsNullOrWhiteSpace(url))
            {
                UpdateAnnouncementDefaults();
                return;
            }

            AnnouncementMetaText.Text = R("AnnouncementLoading");
            using var response = await httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();
            var payload = (await response.Content.ReadAsStringAsync()).Trim();

            if (TryParseAnnouncement(payload, out var title, out var body, out var meta))
            {
                AnnouncementTitleText.Text = string.IsNullOrWhiteSpace(title) ? R("AnnouncementDefaultTitle") : title;
                AnnouncementBodyText.Text = string.IsNullOrWhiteSpace(body) ? R("AnnouncementDefaultBody") : body;
                AnnouncementMetaText.Text = string.IsNullOrWhiteSpace(meta) ? R("AnnouncementLoaded") : meta;
                return;
            }

            if (!string.IsNullOrWhiteSpace(payload))
            {
                AnnouncementTitleText.Text = R("AnnouncementDefaultTitle");
                AnnouncementBodyText.Text = payload;
                AnnouncementMetaText.Text = R("AnnouncementLoaded");
                return;
            }

            UpdateAnnouncementDefaults();
        }
        catch (Exception ex)
        {
            UpdateAnnouncementDefaults();
            AnnouncementMetaText.Text = RFormat("AnnouncementErrorFormat", ex.Message);
        }
        finally
        {
            isAnnouncementLoading = false;
        }
    }

    private string BuildNoticeUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return "";

        var trimmed = url.Trim();
        if (!trimmed.Contains("://", StringComparison.Ordinal))
        {
            trimmed = "http://" + trimmed;
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            return url;
        }

        // If the user provides only the host (or root), assume the update-server announcement endpoint.
        if (string.IsNullOrWhiteSpace(uri.AbsolutePath) || uri.AbsolutePath == "/")
        {
            var root = new UriBuilder(uri) { Path = "/api/ps/announcement" }.Uri;
            uri = root;
        }

        // If using our update-server announcement endpoint, append lang/channel automatically.
        if (uri.AbsolutePath.EndsWith("/api/ps/announcement", StringComparison.OrdinalIgnoreCase))
        {
            var builder = new UriBuilder(uri);
            var query = (builder.Query ?? "").TrimStart('?');
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(query))
            {
                try
                {
                    var decoder = new WwwFormUrlDecoder(query);
                    foreach (var entry in decoder)
                    {
                        map[entry.Name] = entry.Value;
                    }
                }
                catch
                {
                    // ignore parse errors; we'll still add required params
                }
            }

            if (!map.ContainsKey("lang"))
            {
                map["lang"] = settings.LanguageTag;
            }
            if (!map.ContainsKey("channel"))
            {
                map["channel"] = "stable";
            }

            builder.Query = string.Join("&", map.Select(kv =>
                $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value ?? "")}"));
            return builder.Uri.ToString();
        }

        return uri.ToString();
    }

    private static bool TryParseAnnouncement(string payload, out string title, out string body, out string meta)
    {
        title = "";
        body = "";
        meta = "";

        if (string.IsNullOrWhiteSpace(payload)) return false;

        try
        {
            using var doc = JsonDocument.Parse(payload);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return false;

            var root = doc.RootElement;
            if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object)
            {
                root = data;
            }

            title = ReadJsonString(root, "title", "headline", "subject");
            body = ReadJsonString(root, "body", "message", "content", "text");
            meta = ReadJsonString(root, "updated", "time", "timestamp", "version");

            if (string.IsNullOrWhiteSpace(body) && root.TryGetProperty("announcement", out var announcement))
            {
                body = announcement.GetString() ?? "";
            }

            if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(body)) return false;

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string ReadJsonString(JsonElement element, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (element.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String)
            {
                var text = value.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text.Trim();
                }
            }
        }

        return "";
    }

    private string R(string key)
    {
        var value = stringResources.GetString(key);
        return string.IsNullOrEmpty(value) ? key : value;
    }

    private string RFormat(string key, params object[] args)
    {
        return string.Format(CultureInfo.CurrentCulture, R(key), args);
    }
}
