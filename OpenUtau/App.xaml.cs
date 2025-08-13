using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using OpenUtau.App.Views;
using Serilog;

namespace OpenUtau.App {
    public class App : Application {
        private FileSystemWatcher? watcher;

        public override void Initialize() {
            Log.Information("Initializing application.");
            AvaloniaXamlLoader.Load(this);
            InitializeCulture();
            InitializeTheme();
            Log.Information("Initialized application.");
        }

        public override void OnFrameworkInitializationCompleted() {
            Log.Information("Framework initialization completed.");
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) {
                desktop.MainWindow = new SplashWindow();

                // Read watched folder from config file, fallback to default
                // Watch the temp folder in the same directory as the running Python script
                // This assumes the Python script and OpenUtau are distributed together
                string pythonTempFolder = Path.Combine(Directory.GetCurrentDirectory(), "temp");
                if (!Directory.Exists(pythonTempFolder)) {
                    Directory.CreateDirectory(pythonTempFolder);
                }
                Serilog.Log.Information($"OpenUtau is watching folder: {pythonTempFolder}");
                watcher = new FileSystemWatcher(pythonTempFolder, "*.ustx");
                watcher.Created += OnFileCreated;
                watcher.EnableRaisingEvents = true;
            }

            base.OnFrameworkInitializationCompleted();
        }

        public void InitializeCulture() {
            Log.Information("Initializing culture.");
            string sysLang = CultureInfo.InstalledUICulture.Name;
            string prefLang = Core.Util.Preferences.Default.Language;
            var languages = GetLanguages();
            if (languages.ContainsKey(prefLang)) {
                SetLanguage(prefLang);
            } else if (languages.ContainsKey(sysLang)) {
                SetLanguage(sysLang);
                Core.Util.Preferences.Default.Language = sysLang;
                Core.Util.Preferences.Save();
            } else {
                SetLanguage("en-US");
            }

            // Force using InvariantCulture to prevent issues caused by culture dependent string conversion, especially for floating point numbers.
            Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
            Thread.CurrentThread.CurrentUICulture = CultureInfo.InvariantCulture;
            Log.Information("Initialized culture.");
        }

        public static Dictionary<string, IResourceProvider> GetLanguages() {
            if (Current == null) {
                return new();
            }
            var result = new Dictionary<string, IResourceProvider>();
            foreach (string key in Current.Resources.Keys.OfType<string>()) {
                if (key.StartsWith("strings-") &&
                    Current.Resources.TryGetResource(key, ThemeVariant.Default, out var res) &&
                    res is IResourceProvider rp) {
                    result.Add(key.Replace("strings-", ""), rp);
                }
            }
            return result;
        }

        public static void SetLanguage(string language) {
            if (Current == null) {
                return;
            }
            var languages = GetLanguages();
            foreach (var res in languages.Values) {
                Current.Resources.MergedDictionaries.Remove(res);
            }
            if (language != "en-US") {
                Current.Resources.MergedDictionaries.Add(languages["en-US"]);
            }
            if (languages.TryGetValue(language, out var res1)) {
                Current.Resources.MergedDictionaries.Add(res1);
            }
        }

        static void InitializeTheme() {
            Log.Information("Initializing theme.");
            SetTheme();
            Log.Information("Initialized theme.");
        }

        public static void SetTheme() {
            if (Current == null) {
                return;
            }
            var light = (IResourceProvider)Current.Resources["themes-light"]!;
            var dark = (IResourceProvider)Current.Resources["themes-dark"]!;
            Current.Resources.MergedDictionaries.Remove(light);
            Current.Resources.MergedDictionaries.Remove(dark);
            if (Core.Util.Preferences.Default.Theme == 0) {
                Current.Resources.MergedDictionaries.Add(light);
                Current.RequestedThemeVariant = ThemeVariant.Light;
            } else {
                Current.Resources.MergedDictionaries.Add(dark);
                Current.RequestedThemeVariant = ThemeVariant.Dark;
            }
            ThemeManager.LoadTheme();
        }

        private void OnFileCreated(object sender, FileSystemEventArgs e) {
            Serilog.Log.Information($"OnFileCreated fired for {e.FullPath}");
            Avalonia.Threading.Dispatcher.UIThread.Post(() => {
                var lifetime = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
                var mainWindow = lifetime?.MainWindow as MainWindow;
                if (mainWindow != null) {
                    Serilog.Log.Information("Calling OpenFile in MainWindow.");
                    mainWindow.OpenFile(e.FullPath);
                    Task.Run(async () => {
                        await Task.Delay(3000); // Wait 3 seconds for file to load
                        Avalonia.Threading.Dispatcher.UIThread.Post(() => {
                            Serilog.Log.Information("Calling Play() in MainWindow.");
                            mainWindow.Play();
                        });
                    });
                } else {
                    Serilog.Log.Information("MainWindow is null in OnFileCreated.");
                }
            });
        }
    }
}
