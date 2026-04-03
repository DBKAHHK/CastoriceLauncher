using System.Diagnostics;
using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.ApplicationModel.Resources;
using Windows.Storage.Pickers;
using Windows.System;
using WinRT.Interop;

namespace LauncherApp.Views;

public partial class MainPage : Page
{
    private const string PatchDirectory = @"D:\Projects\CastoriceLauncher\Patch";
    private const string ServerDirectory = @"D:\Projects\CastoriceLauncher\Server";
    private static readonly string[] SupportedLanguages = ["en-US", "zh-CN", "zh-TW"];
    private static readonly string[] SupportedGameLanguages = ["cn", "en", "kr", "jp"];
    private readonly ResourceLoader stringResources = new();

    private LauncherSettings settings = new();
    private Process? serverProcess;

    public MainPage()
    {
        InitializeComponent();
        settings = LauncherSettings.Load();
        settings.ApplyDefaults();
        settings.Save();
        ApplyLocalizedUiTexts();
        UpdateActionButtonState();
        RefreshHeaderInfo();
        UpdateStatus(R("UiStatusReady"));
    }

    private void UpdateActionButtonState()
    {
        var configured = HasValidGameDirectory();
        PrimaryActionTitleText.Text = configured ? R("UiStartTitleReady") : R("UiStartTitleNeedPath");
        PrimaryActionSubText.Text = configured ? R("UiStartSubReady") : R("UiStartSubNeedPath");
    }

    private bool HasValidGameDirectory()
    {
        return !string.IsNullOrWhiteSpace(settings.GameDirectoryPath) && Directory.Exists(settings.GameDirectoryPath);
    }

    private void UpdateStatus(string text)
    {
        StatusText.Text = text;
    }

    private async void OnPrimaryActionClicked(object sender, RoutedEventArgs e)
    {
        PrimaryActionButton.IsEnabled = false;
        try
        {
            if (!HasValidGameDirectory())
            {
                var picked = await PickGameDirectoryAsync();
                if (!picked)
                {
                    UpdateStatus(R("UiStatusPickGameCanceled"));
                    return;
                }
            }

            await LaunchWithLocalPatchAsync();
        }
        finally
        {
            PrimaryActionButton.IsEnabled = true;
            UpdateActionButtonState();
            RefreshHeaderInfo();
        }
    }

    private async void OnHomeClicked(object sender, RoutedEventArgs e)
    {
        UpdateStatus(R("UiStatusHomeReady"));
        await Task.CompletedTask;
    }

    private async void OnToolsClicked(object sender, RoutedEventArgs e)
    {
        await ShowToolsDialogAsync();
    }

    private async void OnPluginsClicked(object sender, RoutedEventArgs e)
    {
        await ShowPluginsDialogAsync();
    }

    private async void OnHowToClicked(object sender, RoutedEventArgs e)
    {
        await ShowInfoDialogAsync(R("UiTopHowTo"), R("UiHowToBody"));
    }

    private async void OnAboutClicked(object sender, RoutedEventArgs e)
    {
        var version = typeof(App).Assembly.GetName().Version?.ToString() ?? R("UiVersionUnknown");
        var text = Rf("UiAboutBodyFormat", version)
                   + "\n\n"
                   + R("UiAboutLinksTitle")
                   + "\nhttps://github.com/nie4/hdiff-apply"
                   + "\nhttps://github.com/nie4/hsr-lang-patcher";
        await ShowInfoDialogAsync(R("UiTopAbout"), text);
    }

    private async void OnOpenSettingsClicked(object sender, RoutedEventArgs e)
    {
        await ShowSettingsDialogAsync();
    }

    private async void OnOpenQuickMenuClicked(object sender, RoutedEventArgs e)
    {
        await ShowToolsDialogAsync();
    }

    private async void OnOpenPatchFolderClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            _ = await Launcher.LaunchFolderPathAsync(PatchDirectory);
            UpdateStatus(Rf("UiOpenPatchOkFormat", PatchDirectory));
        }
        catch (Exception ex)
        {
            UpdateStatus(Rf("UiOpenPatchFailFormat", ex.Message));
        }
    }

    private async void OnOpenSrToolsClicked(object sender, RoutedEventArgs e)
    {
        await OpenUrlAsync("https://srtools.neonteam.dev/");
    }

    private async void OnOpenDiscordClicked(object sender, RoutedEventArgs e)
    {
        await OpenUrlAsync("https://discord.gg/CastoricePS");
    }

    private void OnShowStatusClicked(object sender, RoutedEventArgs e)
    {
        if (HasValidGameDirectory())
        {
            UpdateStatus(Rf("UiStatusCurrentGameDirFormat", settings.GameDirectoryPath));
            return;
        }

        UpdateStatus(R("UiStatusGameDirNotSet"));
    }

    private async Task<bool> PickGameDirectoryAsync()
    {
        var selectedPath = await PickGameDirectoryPathAsync();
        if (string.IsNullOrWhiteSpace(selectedPath)) return false;

        settings.GameDirectoryPath = selectedPath;
        settings.Save();
        UpdateStatus(Rf("UiStatusGameDirSetFormat", settings.GameDirectoryPath));
        return true;
    }

    private async Task<string?> PickGameDirectoryPathAsync()
    {
        var picker = new FolderPicker();
        InitializeWithWindow.Initialize(picker, GetWindowHandle());
        picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
        picker.FileTypeFilter.Add("*");

        var folder = await picker.PickSingleFolderAsync();
        return folder?.Path;
    }

    private async Task LaunchWithLocalPatchAsync()
    {
        if (!Directory.Exists(PatchDirectory))
        {
            UpdateStatus(Rf("UiPatchDirMissingFormat", PatchDirectory));
            return;
        }

        var gameDir = settings.GameDirectoryPath?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(gameDir) || !Directory.Exists(gameDir))
        {
            UpdateStatus(R("UiGameDirInvalid"));
            return;
        }

        var serverStatus = EnsureServerRunning();
        if (!string.IsNullOrWhiteSpace(serverStatus))
        {
            UpdateStatus(serverStatus);
        }

        var launcherExe = await EnsureLauncherExistsAsync(gameDir);
        if (launcherExe == null)
        {
            UpdateStatus(R("UiLauncherEnsureFailed"));
            return;
        }

        StartAsAdmin(launcherExe);
        UpdateStatus(Rf("UiLauncherStartedFormat", launcherExe));
    }

    private string EnsureServerRunning()
    {
        try
        {
            if (serverProcess is { HasExited: false })
            {
                return Rf("UiServerAlreadyRunningFormat", serverProcess.ProcessName, serverProcess.Id);
            }

            if (!Directory.Exists(ServerDirectory))
            {
                return Rf("UiServerDirMissingFormat", ServerDirectory);
            }

            var serverExe = FindServerExe(ServerDirectory);
            if (string.IsNullOrWhiteSpace(serverExe))
            {
                return Rf("UiServerExeMissingInDirFormat", ServerDirectory);
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = serverExe,
                WorkingDirectory = Path.GetDirectoryName(serverExe) ?? ServerDirectory,
                UseShellExecute = true,
            };

            serverProcess = Process.Start(startInfo);
            if (serverProcess == null)
            {
                return Rf("UiServerStartFailedFormat", serverExe);
            }

            return Rf("UiServerStartedFormat", serverExe);
        }
        catch (Exception ex)
        {
            return Rf("UiServerStartFailedFormat", ex.Message);
        }
    }

    private async Task<string?> EnsureLauncherExistsAsync(string gameDir)
    {
        var launcherInGame = Path.Combine(gameDir, "launcher.exe");
        if (File.Exists(launcherInGame))
        {
            UpdateStatus(R("UiLauncherExistsSkipCopy"));
            return launcherInGame;
        }

        var launcherFromPatch = Path.Combine(PatchDirectory, "launcher.exe");
        if (!File.Exists(launcherFromPatch))
        {
            return null;
        }

        UpdateStatus(R("UiCopyLauncherToGame"));
        await Task.Run(() => File.Copy(launcherFromPatch, launcherInGame, overwrite: true));
        return File.Exists(launcherInGame) ? launcherInGame : null;
    }

    private static string? FindServerExe(string serverDir)
    {
        var preferred = Path.Combine(serverDir, "CastoricePS.exe");
        if (File.Exists(preferred)) return preferred;

        var files = Directory.GetFiles(serverDir, "*.exe", SearchOption.TopDirectoryOnly);
        if (files.Length == 0) return null;
        return files[0];
    }

    private static void StartAsAdmin(string exePath)
    {
        var psi = new ProcessStartInfo
        {
            FileName = exePath,
            UseShellExecute = true,
            Verb = "runas",
            WorkingDirectory = Path.GetDirectoryName(exePath) ?? AppContext.BaseDirectory,
        };

        Process.Start(psi);
    }

    private static nint GetWindowHandle()
    {
        return WindowNative.GetWindowHandle(App.MainWindow);
    }

    private async Task OpenUrlAsync(string url)
    {
        try
        {
            var ok = await Launcher.LaunchUriAsync(new Uri(url));
            UpdateStatus(ok ? Rf("UiOpenUrlOkFormat", url) : Rf("UiOpenUrlFailFormat", url));
        }
        catch (Exception ex)
        {
            UpdateStatus(Rf("UiOpenUrlFailFormat", ex.Message));
        }
    }

    private void RefreshHeaderInfo()
    {
        if (!HasValidGameDirectory())
        {
            SubTitleText.Text = R("UiPathNotSet");
            return;
        }

        SubTitleText.Text = Rf("UiPathValueFormat", settings.GameDirectoryPath);
    }

    private async Task ShowSettingsDialogAsync()
    {
        var pathBox = new TextBox
        {
            Text = settings.GameDirectoryPath,
            IsReadOnly = true,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        var browseButton = new Button
        {
            Content = R("UiPickGameDir"),
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        browseButton.Click += async (_, _) =>
        {
            var selected = await PickGameDirectoryPathAsync();
            if (!string.IsNullOrWhiteSpace(selected))
            {
                pathBox.Text = selected;
            }
        };

        var languageBox = new ComboBox
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        foreach (var tag in SupportedLanguages)
        {
            languageBox.Items.Add(new ComboBoxItem { Content = tag, Tag = tag });
        }
        SelectLanguageItem(languageBox, settings.LanguageTag);

        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(new TextBlock { Text = R("UiGameDirLabel") });
        panel.Children.Add(pathBox);
        panel.Children.Add(browseButton);
        panel.Children.Add(new TextBlock { Text = R("UiLanguageLabel") });
        panel.Children.Add(languageBox);

        var dialog = new ContentDialog
        {
            Title = R("UiSettingsTitle"),
            PrimaryButtonText = R("UiSave"),
            CloseButtonText = R("UiCancel"),
            DefaultButton = ContentDialogButton.Primary,
            Content = panel,
            XamlRoot = RootGrid.XamlRoot,
        };

        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary) return;

        var previousLanguage = settings.LanguageTag;
        settings.GameDirectoryPath = pathBox.Text.Trim();
        settings.LanguageTag = GetSelectedLanguageTag(languageBox);
        settings.ApplyDefaults();
        settings.Save();

        UpdateActionButtonState();
        RefreshHeaderInfo();
        UpdateStatus(R("UiSettingsSaved"));

        if (!string.Equals(previousLanguage, settings.LanguageTag, StringComparison.OrdinalIgnoreCase))
        {
            App.ApplyLanguageOverride(settings.LanguageTag);
            _ = DispatcherQueue.TryEnqueue(() => Frame?.Navigate(typeof(MainPage)));
        }
    }

    private async Task ShowToolsDialogAsync()
    {
        var setPathButton = new Button { Content = R("UiPickGameDir"), HorizontalAlignment = HorizontalAlignment.Left };
        setPathButton.Click += async (_, _) =>
        {
            var ok = await PickGameDirectoryAsync();
            if (ok)
            {
                UpdateActionButtonState();
                RefreshHeaderInfo();
            }
        };

        var openPatchButton = new Button { Content = R("UiOpenPatchDir"), HorizontalAlignment = HorizontalAlignment.Left };
        openPatchButton.Click += async (_, _) =>
        {
            try { _ = await Launcher.LaunchFolderPathAsync(PatchDirectory); } catch { }
        };

        var openServerButton = new Button { Content = R("UiOpenServerDir"), HorizontalAlignment = HorizontalAlignment.Left };
        openServerButton.Click += async (_, _) =>
        {
            try { _ = await Launcher.LaunchFolderPathAsync(ServerDirectory); } catch { }
        };

        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(setPathButton);
        panel.Children.Add(openPatchButton);
        panel.Children.Add(openServerButton);

        var dialog = new ContentDialog
        {
            Title = R("UiTopTools"),
            CloseButtonText = R("UiClose"),
            Content = panel,
            XamlRoot = RootGrid.XamlRoot,
        };

        await dialog.ShowAsync();
    }

    private async Task ShowInfoDialogAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            CloseButtonText = R("UiClose"),
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
            XamlRoot = RootGrid.XamlRoot,
        };
        await dialog.ShowAsync();
    }

    private async Task ShowPluginsDialogAsync()
    {
        var textLangBox = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        var voiceLangBox = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var lang in SupportedGameLanguages)
        {
            textLangBox.Items.Add(new ComboBoxItem { Content = lang, Tag = lang });
            voiceLangBox.Items.Add(new ComboBoxItem { Content = lang, Tag = lang });
        }
        textLangBox.SelectedIndex = 1;
        voiceLangBox.SelectedIndex = 1;

        var languagePatchButton = new Button
        {
            Content = R("UiPluginApplyLanguagePatch"),
            HorizontalAlignment = HorizontalAlignment.Left,
        };

        languagePatchButton.Click += async (_, _) =>
        {
            var textLang = GetSelectedPluginLanguage(textLangBox);
            var voiceLang = GetSelectedPluginLanguage(voiceLangBox);
            await ApplyLanguagePatchPluginAsync(textLang, voiceLang);
        };

        var hdiffButton = new Button
        {
            Content = R("UiPluginApplyHdiff"),
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        hdiffButton.Click += async (_, _) => { await ApplyHdiffPluginAsync(); };

        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(new TextBlock { Text = R("UiPluginLanguageSectionTitle"), FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        panel.Children.Add(new TextBlock { Text = R("UiPluginTextLanguageLabel") });
        panel.Children.Add(textLangBox);
        panel.Children.Add(new TextBlock { Text = R("UiPluginVoiceLanguageLabel") });
        panel.Children.Add(voiceLangBox);
        panel.Children.Add(languagePatchButton);
        panel.Children.Add(new Border { Height = 1, Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Gray), Margin = new Thickness(0, 4, 0, 4) });
        panel.Children.Add(new TextBlock { Text = R("UiPluginHdiffSectionTitle"), FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        panel.Children.Add(new TextBlock { Text = R("UiPluginHdiffDescription") });
        panel.Children.Add(hdiffButton);

        var dialog = new ContentDialog
        {
            Title = R("UiTopPlugins"),
            CloseButtonText = R("UiClose"),
            Content = panel,
            XamlRoot = RootGrid.XamlRoot,
        };

        await dialog.ShowAsync();
    }

    private string GetSelectedPluginLanguage(ComboBox box)
    {
        if (box.SelectedItem is ComboBoxItem item && item.Tag is string tag && SupportedGameLanguages.Contains(tag))
        {
            return tag;
        }
        return "en";
    }

    private async Task ApplyLanguagePatchPluginAsync(string textLang, string voiceLang)
    {
        if (!HasValidGameDirectory())
        {
            UpdateStatus(R("UiStatusGameDirNotSet"));
            return;
        }

        try
        {
            await Task.Run(() =>
            {
                var designDataPath = ResolveDesignDataPath(settings.GameDirectoryPath);
                var mDesignVPath = Path.Combine(designDataPath, "M_DesignV.bytes");
                var mDesignBytes = File.ReadAllBytes(mDesignVPath);
                var indexHash = GetDesignIndexHash(mDesignBytes);

                var designVPath = Path.Combine(designDataPath, $"DesignV_{indexHash}.bytes");
                var designIndexBytes = File.ReadAllBytes(designVPath);
                var index = ParseDesignIndex(designIndexBytes);
                var found = FindEntryByHash(index, -515329346);
                if (found == null) throw new InvalidOperationException(R("UiPluginLanguageEntryMissing"));

                var (entry, fileHash) = found.Value;
                var bytesPath = Path.Combine(designDataPath, $"{fileHash}.bytes");
                var rows = ParseAllowedLanguageRows(bytesPath, entry.Offset, entry.Size);

                PatchLanguageRows(rows, textLang, voiceLang);
                var serialized = SerializeAllowedLanguageRows(rows);
                WriteAtOffset(bytesPath, entry.Offset, entry.Size, serialized);
            });

            UpdateStatus(Rf("UiPluginLanguagePatchOkFormat", textLang, voiceLang));
        }
        catch (Exception ex)
        {
            UpdateStatus(Rf("UiPluginLanguagePatchFailedFormat", ex.Message));
        }
    }

    private async Task ApplyHdiffPluginAsync()
    {
        if (!HasValidGameDirectory())
        {
            UpdateStatus(R("UiStatusGameDirNotSet"));
            return;
        }

        try
        {
            await Task.Run(() =>
            {
                var gameDir = settings.GameDirectoryPath;
                var entries = LoadDiffEntries(gameDir);
                if (entries.Count == 0)
                {
                    throw new InvalidOperationException(R("UiPluginNoDiffEntries"));
                }

                foreach (var entry in entries)
                {
                    ApplySingleHdiff(gameDir, entry);
                }
            });

            UpdateStatus(R("UiPluginHdiffCompleted"));
        }
        catch (Exception ex)
        {
            UpdateStatus(Rf("UiPluginHdiffFailedFormat", ex.Message));
        }
    }

    private static string ResolveDesignDataPath(string gamePath)
    {
        var root = gamePath.Trim();
        if (File.Exists(Path.Combine(root, "StarRail.exe")))
        {
            return Path.Combine(root, "StarRail_Data", "StreamingAssets", "DesignData", "Windows");
        }

        if (File.Exists(Path.Combine(root, "M_DesignV.bytes")))
        {
            return root;
        }

        throw new DirectoryNotFoundException("DesignData path not found.");
    }

    private static string GetDesignIndexHash(byte[] mDesignData)
    {
        if (mDesignData.Length < 0x2C)
        {
            throw new InvalidDataException("Invalid M_DesignV.bytes data.");
        }

        Span<byte> hash = stackalloc byte[16];
        var index = 0;
        for (var i = 0; i < 4; i++)
        {
            var offset = 0x1C + i * 4;
            for (var j = 3; j >= 0; j--)
            {
                hash[index++] = mDesignData[offset + j];
            }
        }

        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private readonly record struct DesignDataEntry(int NameHash, int Size, int Offset);
    private sealed record DesignFileEntry(string FileHash, List<DesignDataEntry> Entries);

    private static List<DesignFileEntry> ParseDesignIndex(byte[] data)
    {
        var list = new List<DesignFileEntry>();
        var offset = 0;
        _ = ReadUInt64LE(data, ref offset);
        var fileCount = ReadUInt32BE(data, ref offset);
        _ = ReadUInt32LE(data, ref offset);

        for (var i = 0; i < fileCount; i++)
        {
            _ = ReadInt32BE(data, ref offset);
            var fileHashBytes = ReadBytes(data, ref offset, 16);
            var fileHash = Convert.ToHexString(fileHashBytes).ToLowerInvariant();
            _ = ReadUInt64BE(data, ref offset);
            var entryCount = ReadUInt32BE(data, ref offset);
            var entries = new List<DesignDataEntry>((int)entryCount);
            for (var j = 0; j < entryCount; j++)
            {
                var nameHash = ReadInt32BE(data, ref offset);
                var size = ReadInt32BE(data, ref offset);
                var entryOffset = ReadInt32BE(data, ref offset);
                entries.Add(new DesignDataEntry(nameHash, size, entryOffset));
            }
            _ = ReadByte(data, ref offset);
            list.Add(new DesignFileEntry(fileHash, entries));
        }

        return list;
    }

    private static (DesignDataEntry Entry, string FileHash)? FindEntryByHash(List<DesignFileEntry> files, int hash)
    {
        foreach (var file in files)
        {
            foreach (var entry in file.Entries)
            {
                if (entry.NameHash == hash)
                {
                    return (entry, file.FileHash);
                }
            }
        }
        return null;
    }

    private sealed class AllowedLanguageRow
    {
        public string? Area { get; set; }
        public byte? RowType { get; set; }
        public List<string>? LanguageList { get; set; }
        public string? DefaultLanguage { get; set; }

        public bool IsText => RowType is null;
        public bool IsVoice => RowType == 1;
    }

    private static List<AllowedLanguageRow> ParseAllowedLanguageRows(string bytesPath, int sectionOffset, int sectionSize)
    {
        using var fs = File.OpenRead(bytesPath);
        fs.Position = sectionOffset;
        var buffer = new byte[sectionSize];
        _ = fs.Read(buffer, 0, buffer.Length);

        var offset = 0;
        _ = ReadByte(buffer, ref offset);
        var count = ReadSignedVarInt(buffer, ref offset);
        var rows = new List<AllowedLanguageRow>(count);

        for (var i = 0; i < count; i++)
        {
            var bitmask = ReadByte(buffer, ref offset);
            var row = new AllowedLanguageRow();
            if ((bitmask & (1 << 0)) != 0) row.Area = ReadSmallString(buffer, ref offset);
            if ((bitmask & (1 << 1)) != 0) row.RowType = ReadByte(buffer, ref offset);
            if ((bitmask & (1 << 2)) != 0) row.LanguageList = ReadStringArray(buffer, ref offset);
            if ((bitmask & (1 << 3)) != 0) row.DefaultLanguage = ReadSmallString(buffer, ref offset);
            rows.Add(row);
        }

        return rows;
    }

    private static void PatchLanguageRows(List<AllowedLanguageRow> rows, string textLang, string voiceLang)
    {
        UpdateLanguage(rows, "os", textLang, voice: false);
        UpdateLanguage(rows, "cn", voiceLang, voice: true);
        UpdateLanguage(rows, "os", voiceLang, voice: true);
        UpdateLanguage(rows, "cn", textLang, voice: false);
    }

    private static void UpdateLanguage(List<AllowedLanguageRow> rows, string area, string lang, bool voice)
    {
        var row = rows.FirstOrDefault(r => string.Equals(r.Area, area, StringComparison.OrdinalIgnoreCase) && (voice ? r.IsVoice : r.IsText));
        if (row == null)
        {
            throw new InvalidDataException($"AllowedLanguage row not found: area={area}, voice={voice}");
        }

        row.DefaultLanguage = lang;
        row.LanguageList = [lang];
    }

    private static byte[] SerializeAllowedLanguageRows(List<AllowedLanguageRow> rows)
    {
        using var ms = new MemoryStream();
        ms.WriteByte(0);
        WriteSignedVarInt(ms, rows.Count);
        foreach (var row in rows)
        {
            byte bitmask = 0;
            if (row.Area != null) bitmask |= 1 << 0;
            if (row.RowType != null) bitmask |= 1 << 1;
            if (row.LanguageList != null) bitmask |= 1 << 2;
            if (row.DefaultLanguage != null) bitmask |= 1 << 3;
            ms.WriteByte(bitmask);

            if (row.Area != null) WriteSmallString(ms, row.Area);
            if (row.RowType != null) ms.WriteByte(row.RowType.Value);
            if (row.LanguageList != null) WriteStringArray(ms, row.LanguageList);
            if (row.DefaultLanguage != null) WriteSmallString(ms, row.DefaultLanguage);
        }
        return ms.ToArray();
    }

    private static void WriteAtOffset(string filePath, int offset, int originalSize, byte[] data)
    {
        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
        fs.Position = offset;
        fs.Write(data, 0, data.Length);
        if (data.Length < originalSize)
        {
            fs.Write(new byte[originalSize - data.Length], 0, originalSize - data.Length);
        }
    }

    private sealed class DiffEntry
    {
        public string source_file_name { get; set; } = "";
        public string target_file_name { get; set; } = "";
        public string patch_file_name { get; set; } = "";
    }

    private sealed class HDiffMap
    {
        public List<DiffEntry> diff_map { get; set; } = [];
    }

    private sealed class CustomDiffMap
    {
        public string remoteName { get; set; } = "";
    }

    private static List<DiffEntry> LoadDiffEntries(string gameDir)
    {
        var hdiffMapPath = Path.Combine(gameDir, "hdiffmap.json");
        if (File.Exists(hdiffMapPath))
        {
            var json = File.ReadAllText(hdiffMapPath);
            var map = JsonSerializer.Deserialize<HDiffMap>(json) ?? new HDiffMap();
            return map.diff_map ?? [];
        }

        var customMarker = Path.Combine(gameDir, "GameAssembly.dll.hdiff");
        if (File.Exists(customMarker))
        {
            var txt = Path.Combine(gameDir, "hdifffiles.txt");
            if (!File.Exists(txt)) return [];
            var list = new List<DiffEntry>();
            foreach (var line in File.ReadAllLines(txt))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var custom = JsonSerializer.Deserialize<CustomDiffMap>(line.Trim());
                if (custom == null || string.IsNullOrWhiteSpace(custom.remoteName)) continue;
                list.Add(new DiffEntry
                {
                    source_file_name = custom.remoteName,
                    target_file_name = custom.remoteName,
                    patch_file_name = custom.remoteName + ".hdiff",
                });
            }
            return list;
        }

        return [];
    }

    private static void ApplySingleHdiff(string gameDir, DiffEntry entry)
    {
        var hpatchzPath = ResolveBundledToolPath("hpatchz.exe");
        if (!File.Exists(hpatchzPath))
        {
            throw new FileNotFoundException($"Bundled hpatchz not found: {hpatchzPath}");
        }

        var source = string.IsNullOrWhiteSpace(entry.source_file_name) ? "" : Path.Combine(gameDir, entry.source_file_name);
        var patch = Path.Combine(gameDir, entry.patch_file_name);
        var target = Path.Combine(gameDir, entry.target_file_name);

        var targetDir = Path.GetDirectoryName(target);
        if (!string.IsNullOrWhiteSpace(targetDir))
        {
            Directory.CreateDirectory(targetDir);
        }

        var psi = new ProcessStartInfo
        {
            FileName = hpatchzPath,
            WorkingDirectory = gameDir,
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };
        if (!string.IsNullOrWhiteSpace(source)) psi.ArgumentList.Add(source);
        psi.ArgumentList.Add(patch);
        psi.ArgumentList.Add(target);

        using var p = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start hpatchz.");
        var stdOut = p.StandardOutput.ReadToEnd();
        var stdErr = p.StandardError.ReadToEnd();
        p.WaitForExit();
        if (p.ExitCode != 0)
        {
            throw new InvalidOperationException($"hpatchz failed for {entry.target_file_name}: {stdErr}\n{stdOut}");
        }
    }

    private static string ResolveBundledToolPath(string fileName)
    {
        var baseDir = AppContext.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(baseDir, "..", "Tools", fileName),
            Path.Combine(baseDir, "..", "tools", fileName),
            Path.Combine(baseDir, "tools", fileName),
            Path.Combine(baseDir, "Tools", fileName),
            Path.Combine(baseDir, fileName),
        };

        return candidates.FirstOrDefault(File.Exists) ?? candidates[0];
    }

    private static byte ReadByte(byte[] data, ref int offset)
    {
        if (offset >= data.Length) throw new EndOfStreamException();
        return data[offset++];
    }

    private static byte[] ReadBytes(byte[] data, ref int offset, int count)
    {
        if (offset + count > data.Length) throw new EndOfStreamException();
        var result = new byte[count];
        Buffer.BlockCopy(data, offset, result, 0, count);
        offset += count;
        return result;
    }

    private static uint ReadUInt32BE(byte[] data, ref int offset)
    {
        var span = ReadBytes(data, ref offset, 4);
        return BinaryPrimitives.ReadUInt32BigEndian(span);
    }

    private static uint ReadUInt32LE(byte[] data, ref int offset)
    {
        var span = ReadBytes(data, ref offset, 4);
        return BinaryPrimitives.ReadUInt32LittleEndian(span);
    }

    private static ulong ReadUInt64BE(byte[] data, ref int offset)
    {
        var span = ReadBytes(data, ref offset, 8);
        return BinaryPrimitives.ReadUInt64BigEndian(span);
    }

    private static ulong ReadUInt64LE(byte[] data, ref int offset)
    {
        var span = ReadBytes(data, ref offset, 8);
        return BinaryPrimitives.ReadUInt64LittleEndian(span);
    }

    private static int ReadInt32BE(byte[] data, ref int offset)
    {
        var span = ReadBytes(data, ref offset, 4);
        return BinaryPrimitives.ReadInt32BigEndian(span);
    }

    private static int ReadSignedVarInt(byte[] data, ref int offset)
    {
        var shift = 0;
        var result = 0;
        byte b;
        do
        {
            b = ReadByte(data, ref offset);
            result |= (b & 0x7F) << shift;
            shift += 7;
        } while ((b & 0x80) != 0 && shift < 35);

        return result;
    }

    private static void WriteSignedVarInt(Stream stream, int value)
    {
        uint v = (uint)value;
        while (v >= 0x80)
        {
            stream.WriteByte((byte)(v | 0x80));
            v >>= 7;
        }
        stream.WriteByte((byte)v);
    }

    private static string ReadSmallString(byte[] data, ref int offset)
    {
        var len = ReadByte(data, ref offset);
        var bytes = ReadBytes(data, ref offset, len);
        return Encoding.UTF8.GetString(bytes);
    }

    private static void WriteSmallString(Stream stream, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        if (bytes.Length > 255) throw new InvalidDataException("String too long.");
        stream.WriteByte((byte)bytes.Length);
        stream.Write(bytes, 0, bytes.Length);
    }

    private static List<string> ReadStringArray(byte[] data, ref int offset)
    {
        var count = ReadSignedVarInt(data, ref offset);
        var list = new List<string>(Math.Max(count, 0));
        for (var i = 0; i < count; i++)
        {
            list.Add(ReadSmallString(data, ref offset));
        }
        return list;
    }

    private static void WriteStringArray(Stream stream, List<string> values)
    {
        WriteSignedVarInt(stream, values.Count);
        foreach (var value in values)
        {
            WriteSmallString(stream, value);
        }
    }

    private static void SelectLanguageItem(ComboBox box, string languageTag)
    {
        foreach (var item in box.Items)
        {
            if (item is ComboBoxItem cbi && string.Equals(cbi.Tag?.ToString(), languageTag, StringComparison.OrdinalIgnoreCase))
            {
                box.SelectedItem = cbi;
                return;
            }
        }
        box.SelectedIndex = 0;
    }

    private static string GetSelectedLanguageTag(ComboBox box)
    {
        if (box.SelectedItem is ComboBoxItem cbi && cbi.Tag is string tag && !string.IsNullOrWhiteSpace(tag))
        {
            return tag;
        }
        return "en-US";
    }

    private void ApplyLocalizedUiTexts()
    {
        TopHomeButton.Content = R("UiTopHome");
        TopToolsButton.Content = R("UiTopTools");
        TopPluginsButton.Content = R("UiTopPlugins");
        TopHowToButton.Content = R("UiTopHowTo");
        TopAboutButton.Content = R("UiTopAbout");

        ToolTipService.SetToolTip(TopSettingsButton, R("UiTipSettings"));
        ToolTipService.SetToolTip(RightSettingsButton, R("UiTipSettings"));
        ToolTipService.SetToolTip(RightPatchButton, R("UiTipPatch"));
        ToolTipService.SetToolTip(RightStatusButton, R("UiTipStatus"));

        ToolTipService.SetToolTip(LeftToolsButton, "srtools.neonteam.dev");
        ToolTipService.SetToolTip(LeftDiscordButton, "discord.gg/CastoricePS");
        LeftToolsText.Text = R("UiLeftTools");
        LeftDiscordText.Text = R("UiLeftDiscord");
        WelcomeText.Text = R("UiWelcome");
    }

    private string R(string key)
    {
        var value = stringResources.GetString(key);
        return string.IsNullOrWhiteSpace(value) ? key : value;
    }

    private string Rf(string key, params object[] args)
    {
        return string.Format(R(key), args);
    }
}
