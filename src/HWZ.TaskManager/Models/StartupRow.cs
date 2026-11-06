using System.Windows.Media;
using HWZ.TaskManager.Helpers;

namespace HWZ.TaskManager.Models;

public sealed class StartupRow : ObservableObject
{
    private ImageSource? _iconSource;

    public StartupRow(string name, string command, string publisher, string source, string executablePath, bool isEnabled = true)
    {
        Name = name;
        Command = command;
        Publisher = publisher;
        Source = source;
        ExecutablePath = executablePath;
        IsEnabled = isEnabled;
        AvatarBrush = AccentPalette.Create(name);
        Initial = string.IsNullOrWhiteSpace(name) ? "?" : char.ToUpperInvariant(name.Trim()[0]).ToString();
    }

    public string Name { get; }
    public string Command { get; }
    public string Publisher { get; }
    public string Source { get; }
    public string ExecutablePath { get; }
    public Brush AvatarBrush { get; }
    public string Initial { get; }

    public ImageSource? IconSource
    {
        get => _iconSource;
        set
        {
            if (SetProperty(ref _iconSource, value))
            {
                OnPropertyChanged(nameof(HasIcon));
            }
        }
    }

    public bool HasIcon => IconSource is not null;

    public bool IsEnabled { get; }
    public string StatusText => IsEnabled ? "已启用" : "已禁用";
    public string FriendlyPublisher => string.IsNullOrWhiteSpace(Publisher) ? "未知发布者" : Publisher;
    public string CommandDisplay => Formatters.Shorten(Command, 130);
    public string SourceShort => Source.Replace(" · 注册表", string.Empty);
}
