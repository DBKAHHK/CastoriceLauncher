using System.Globalization;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Media;

namespace LauncherApp
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    public partial class App : Application
    {
        private Window? window;
        public static Window MainWindow { get; private set; } = null!;

        /// <summary>
        /// Initializes the singleton application object.  This is the first line of authored code
        /// executed, and as such is the logical equivalent of main() or WinMain().
        /// </summary>
        public App()
        {
            Logger.Init();
            UnhandledException += OnUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            {
                if (e.ExceptionObject is Exception ex) Logger.Error(ex, "AppDomain unhandled exception");
                else Logger.Error($"AppDomain unhandled exception: {e.ExceptionObject}");
            };
            TaskScheduler.UnobservedTaskException += (_, e) =>
            {
                Logger.Error(e.Exception, "Unobserved task exception");
                e.SetObserved();
            };
            this.InitializeComponent();
        }

        /// <summary>
        /// Invoked when the application is launched normally by the end user.  Other entry points
        /// will be used such as when the application is launched to open a specific file.
        /// </summary>
        /// <param name="e">Details about the launch request and process.</param>
        protected override void OnLaunched(LaunchActivatedEventArgs e)
        {
            try
            {
                var settings = LauncherSettings.Load();
                ApplyLanguageOverride(settings.LanguageTag);

                window ??= new Window();
                MainWindow = window;
                TryEnableMica(window);

                if (window.Content is not Frame rootFrame)
                {
                    rootFrame = new Frame();
                    rootFrame.NavigationFailed += OnNavigationFailed;
                    rootFrame.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
                    window.Content = rootFrame;
                }

                _ = rootFrame.Navigate(typeof(MainPage), e.Arguments);
                window.Activate();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Fatal error during OnLaunched");
                window ??= new Window();
                MainWindow = window;
                window.Content = new TextBlock
                {
                    Text = $"Launcher failed to start.\n\n{ex}",
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(20),
                };
                window.Activate();
            }
        }

        private static void TryEnableMica(Window window)
        {
            // Avoid hard reference to SystemBackdrops types because the WinUI XAML compiler (net472)
            // may fail to load them during build for unpackaged projects.
            if (TrySetSystemBackdrop(window, "Microsoft.UI.Composition.SystemBackdrops.MicaBackdrop", preferKind: "BaseAlt"))
            {
                return;
            }

            _ = TrySetSystemBackdrop(window, "Microsoft.UI.Composition.SystemBackdrops.DesktopAcrylicBackdrop", preferKind: null);
        }

        private static bool TrySetSystemBackdrop(Window window, string typeName, string? preferKind)
        {
            try
            {
                var backdropType = AppDomain.CurrentDomain
                    .GetAssemblies()
                    .Select(a => a.GetType(typeName, throwOnError: false, ignoreCase: false))
                    .FirstOrDefault(t => t != null);
                if (backdropType == null) return false;

                var backdrop = Activator.CreateInstance(backdropType);
                if (backdrop == null) return false;

                if (!string.IsNullOrWhiteSpace(preferKind))
                {
                    var kindProp = backdropType.GetProperty("Kind");
                    if (kindProp != null && kindProp.CanWrite && kindProp.PropertyType.IsEnum)
                    {
                        var values = Enum.GetValues(kindProp.PropertyType).Cast<object>().ToList();
                        var picked = values.FirstOrDefault(v => string.Equals(v.ToString(), preferKind, StringComparison.OrdinalIgnoreCase))
                                     ?? values.FirstOrDefault();
                        if (picked != null) kindProp.SetValue(backdrop, picked);
                    }
                }

                var sysBackdropProp = typeof(Window).GetProperty("SystemBackdrop");
                sysBackdropProp?.SetValue(window, backdrop);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static void ApplyLanguageOverride(string languageTag)
        {
            if (string.IsNullOrWhiteSpace(languageTag)) return;

            var tag = languageTag.Trim();

            try
            {
                Microsoft.Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = tag;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to set PrimaryLanguageOverride.");
            }

            try
            {
                var culture = CultureInfo.GetCultureInfo(tag);
                CultureInfo.DefaultThreadCurrentCulture = culture;
                CultureInfo.DefaultThreadCurrentUICulture = culture;
            }
            catch
            {
                // ignore
            }

            try
            {
                Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = tag;
            }
            catch
            {
                // ignore
            }
        }

        private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
        {
            Logger.Error(e.Exception, "UnhandledException");
            // keep app alive so we can see the error page/logs
            e.Handled = true;
        }

        /// <summary>
        /// Invoked when Navigation to a certain page fails
        /// </summary>
        /// <param name="sender">The Frame which failed navigation</param>
        /// <param name="e">Details about the navigation failure</param>
        void OnNavigationFailed(object sender, NavigationFailedEventArgs e)
        {
            throw new Exception("Failed to load Page " + e.SourcePageType.FullName);
        }
    }
}
