using System.Windows.Media;
using HWZ.TaskManager.Helpers;

namespace HWZ.TaskManager.Models;

public sealed class ProcessRow : ObservableObject
{
    private double _cpuPercent;
    private double _memoryMb;
    private double _diskMbPerSecond;
    private string _statusText = "正在运行";
    private string _subtitle = string.Empty;
    private string? _path;
    private ImageSource? _iconSource;

    public ProcessRow(ProcessSnapshot snapshot)
        : this(snapshot, AccentPalette.Create(snapshot.Name))
    {
    }

    private ProcessRow(ProcessSnapshot snapshot, LinearGradientBrush avatarBrush)
    {
        Pid = snapshot.Pid;
        Name = snapshot.Name;
        AvatarBrush = avatarBrush;
        Initial = BuildInitial(snapshot.Name);
        StartTime = snapshot.StartTime;
        SessionId = snapshot.SessionId;
        ThreadCount = snapshot.ThreadCount;
        HandleCount = snapshot.HandleCount;
        BasePriority = snapshot.BasePriority;
        IsProtected = snapshot.IsProtected;
        Update(snapshot);
    }

    public int Pid { get; }
    public string Name { get; }
    public string Initial { get; }
    public Brush AvatarBrush { get; }
    public bool IsProtected { get; }
    public int SessionId { get; private set; }
    public int ThreadCount { get; private set; }
    public int HandleCount { get; private set; }
    public int BasePriority { get; private set; }
    public DateTime? StartTime { get; private set; }

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

    public string? Path
    {
        get => _path;
        private set => SetProperty(ref _path, value);
    }

    public string Description { get; private set; } = string.Empty;
    public string CompanyName { get; private set; } = string.Empty;
    public bool HasWindow { get; private set; }
    public bool IsHung { get; private set; }
    public string WindowTitle { get; private set; } = string.Empty;

    public string Subtitle
    {
        get => _subtitle;
        private set => SetProperty(ref _subtitle, value);
    }

    public double CpuPercent
    {
        get => _cpuPercent;
        private set
        {
            if (SetProperty(ref _cpuPercent, value))
            {
                OnPropertyChanged(nameof(CpuText));
            }
        }
    }

    public double MemoryMb
    {
        get => _memoryMb;
        private set
        {
            if (SetProperty(ref _memoryMb, value))
            {
                OnPropertyChanged(nameof(MemoryText));
            }
        }
    }

    public double DiskMbPerSecond
    {
        get => _diskMbPerSecond;
        private set
        {
            if (SetProperty(ref _diskMbPerSecond, value))
            {
                OnPropertyChanged(nameof(DiskText));
            }
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string CpuText => $"{CpuPercent:0.0}%";
    public string MemoryText => $"{MemoryMb:0.0} MB";
    public string DiskText => DiskMbPerSecond < 0.05 ? "—" : $"{DiskMbPerSecond:0.0} MB/s";
    public string Category => HasWindow ? "应用" : "后台";
    public string KindText => IsProtected ? "系统" : Category;
    public string StartTimeText => StartTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "未知";
    public string SessionText => SessionId == 0 ? "系统会话" : $"会话 {SessionId}";
    public string PrivateMemoryText => PrivateMemoryMb <= 0 ? "—" : Formatters.Bytes(PrivateMemoryMb * 1024 * 1024);
    public double PrivateMemoryMb { get; private set; }

    public void Update(ProcessSnapshot snapshot)
    {
        Path = snapshot.Path;
        Description = snapshot.Description ?? string.Empty;
        CompanyName = snapshot.CompanyName ?? string.Empty;
        HasWindow = snapshot.HasWindow;
        IsHung = snapshot.IsHung;
        WindowTitle = snapshot.WindowTitle ?? string.Empty;
        CpuPercent = Math.Round(snapshot.CpuPercent, 1);
        MemoryMb = Math.Round(snapshot.MemoryMb, 1);
        PrivateMemoryMb = snapshot.PrivateMemoryMb;
        DiskMbPerSecond = Math.Round(snapshot.DiskMbPerSecond, 1);
        ThreadCount = snapshot.ThreadCount;
        HandleCount = snapshot.HandleCount;
        BasePriority = snapshot.BasePriority;
        SessionId = snapshot.SessionId;
        StartTime = snapshot.StartTime ?? StartTime;
        StatusText = IsHung ? "未响应" : "正在运行";

        var subtitle = !string.IsNullOrWhiteSpace(CompanyName)
            ? CompanyName
            : !string.IsNullOrWhiteSpace(Description)
                ? Description
                : IsProtected
                    ? "Windows 系统进程"
                    : "后台进程";

        Subtitle = Formatters.Shorten(subtitle, 60)!;

        OnPropertyChanged(nameof(Category));
        OnPropertyChanged(nameof(KindText));
        OnPropertyChanged(nameof(PrivateMemoryText));
        OnPropertyChanged(nameof(StartTimeText));
        OnPropertyChanged(nameof(SessionText));
    }

    private static string BuildInitial(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "?";
        }

        var character = name.Trim()[0];
        return char.ToUpperInvariant(character).ToString();
    }
}
