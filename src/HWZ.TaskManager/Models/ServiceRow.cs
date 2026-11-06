using HWZ.TaskManager.Helpers;

namespace HWZ.TaskManager.Models;

public sealed class ServiceRow : ObservableObject
{
    private string _statusCode = "Unknown";

    public ServiceRow(
        string name,
        string displayName,
        string description,
        string startupType,
        string statusCode,
        string imagePath)
    {
        Name = name;
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? name : displayName;
        Description = description;
        StartupType = startupType;
        ImagePath = imagePath;
        _statusCode = statusCode;
        AvatarBrush = AccentPalette.Create(name);
        Initial = string.IsNullOrWhiteSpace(DisplayName) ? "S" : DisplayName.Trim()[0].ToString().ToUpperInvariant();
    }

    public string Name { get; }
    public string DisplayName { get; }
    public string Description { get; }
    public string StartupType { get; }
    public string ImagePath { get; }
    public object AvatarBrush { get; }
    public string Initial { get; }

    public string StatusCode
    {
        get => _statusCode;
        private set
        {
            if (SetProperty(ref _statusCode, value))
            {
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(CanStart));
                OnPropertyChanged(nameof(CanStop));
                OnPropertyChanged(nameof(IsRunning));
            }
        }
    }

    public string StatusText => StatusCode switch
    {
        "Running" => "正在运行",
        "Stopped" => "已停止",
        "StartPending" => "正在启动",
        "StopPending" => "正在停止",
        "Paused" => "已暂停",
        "PausePending" => "正在暂停",
        "ContinuePending" => "正在继续",
        "Disabled" => "已禁用",
        _ => "未知"
    };

    public bool IsRunning => StatusCode == "Running";
    public bool CanStart => StatusCode == "Stopped";
    public bool CanStop => StatusCode == "Running";

    public void SetStatus(string code) => StatusCode = code;

    public string ShortDescription => string.IsNullOrWhiteSpace(Description)
        ? $"服务名称：{Name}"
        : Formatters.Shorten(Description, 110);
}
