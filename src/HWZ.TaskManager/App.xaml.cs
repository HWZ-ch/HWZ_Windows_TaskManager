using System.IO;
using System.Windows;
using System.Windows.Threading;
using HWZ.TaskManager.Helpers;
using HWZ.TaskManager.Models;
using HWZ.TaskManager.Services;

namespace HWZ.TaskManager;

public partial class App : Application
{
    private SettingsService? _settingsService;
    private AppSettings? _settings;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnDispatcherUnhandledException;

        _settingsService = new SettingsService();
        _settings = _settingsService.Load();

        ThemeManager.Apply(_settings.ThemeMode);

        var window = new MainWindow(_settingsService, _settings);
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_settingsService is not null && _settings is not null)
        {
            _settingsService.Save(_settings);
        }

        base.OnExit(e);
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        TryLog(e.Exception);
        MessageBox.Show(
            "HWZ 任务管理器遇到了一点问题。\n\n" + e.Exception.Message +
            "\n\n详细信息已记录到本地日志，应用会尽量继续运行。",
            "HWZ 任务管理器",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
        e.Handled = true;
    }

    private static void TryLog(Exception exception)
    {
        try
        {
            var folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "HWZ", "TaskManager");
            Directory.CreateDirectory(folder);
            File.AppendAllText(
                Path.Combine(folder, "error.log"),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {exception}\n\n");
        }
        catch
        {
            // Logging must never bring the application down.
        }
    }
}
