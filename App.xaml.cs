using System;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using ZapretGuiWpf.Helpers;
using ZapretGuiWpf.Models;

namespace ZapretGuiWpf;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        string rootDir = AppDomain.CurrentDomain.BaseDirectory;
        string configDir = Path.Combine(rootDir, "config");
        Logger.Init(configDir);

        // Load the saved theme preference before the main window is
        // constructed, so it never flashes in the wrong theme on startup.
        var cfg = new AppConfig(Path.Combine(configDir, "config.ini"));
        bool dark = cfg.Get("DarkMode", "0") == "1";
        ThemeManager.Apply(dark ? AppTheme.Dark : AppTheme.Light);

        // Equivalent of Python's threading.excepthook / the top-level try/except around mainloop():
        // unhandled exceptions get logged instead of silently crashing with no trace.
        DispatcherUnhandledException += (_, args) =>
        {
            Logger.Exception("Uygulama beklenmedik şekilde sonlandı (UI thread).", args.Exception);
            MessageBox.Show(
                "Beklenmeyen bir hata oluştu:\n\n" + args.Exception.Message +
                "\n\nDetaylar config\\zapret_gui.log dosyasına yazıldı.",
                "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
                Logger.Exception("Yakalanmamış thread hatası.", ex);
        };

        base.OnStartup(e);
    }
}
