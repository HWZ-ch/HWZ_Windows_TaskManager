using HWZ.TaskManager.Helpers;

namespace HWZ.TaskManager.Models;

public sealed class AppSettings : ObservableObject
{
    private string _themeMode = "Dark";
    private int _refreshSeconds = 1;
    private bool _alwaysOnTop;
    private bool _confirmOnEnd = true;
    private bool _showProcessIcons = true;
    private bool _autoUpdate = true;

    public string ThemeMode
    {
        get => _themeMode;
        set => SetProperty(ref _themeMode, value);
    }

    public int RefreshSeconds
    {
        get => _refreshSeconds;
        set => SetProperty(ref _refreshSeconds, value is < 1 or > 10 ? 1 : value);
    }

    public bool AlwaysOnTop
    {
        get => _alwaysOnTop;
        set => SetProperty(ref _alwaysOnTop, value);
    }

    public bool ConfirmOnEnd
    {
        get => _confirmOnEnd;
        set => SetProperty(ref _confirmOnEnd, value);
    }

    public bool ShowProcessIcons
    {
        get => _showProcessIcons;
        set => SetProperty(ref _showProcessIcons, value);
    }

    public bool AutoUpdate
    {
        get => _autoUpdate;
        set => SetProperty(ref _autoUpdate, value);
    }
}
