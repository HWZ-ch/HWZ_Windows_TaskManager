using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using HWZ.TaskManager.Helpers;
using HWZ.TaskManager.Models;
using HWZ.TaskManager.Services;

namespace HWZ.TaskManager.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private const int HistoryLength = 60;

    private readonly AppSettings _settings;
    private readonly SettingsService _settingsService;
    private readonly DialogService _dialogs;
    private readonly SystemMonitor _monitor = new();
    private readonly DispatcherTimer _timer;
    private readonly DispatcherTimer _toastTimer;
    private readonly ObservableCollection<ProcessRow> _processes = new();
    private readonly AsyncRelayCommand _endTaskCommand;
    private readonly AsyncRelayCommand _endTreeCommand;
    private readonly AsyncRelayCommand _refreshCommand;
    private readonly AsyncRelayCommand _refreshServicesCommand;
    private readonly AsyncRelayCommand _refreshStartupCommand;
    private readonly AsyncRelayCommand _startServiceCommand;
    private readonly AsyncRelayCommand _stopServiceCommand;
    private readonly RelayCommand _showDetailsCommand;
    private readonly AsyncRelayCommand _updateActionCommand;
    private readonly HashSet<string> _failedIconPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly UpdateService _updates = new();

    private string _currentPage = "overview";
    private string _searchText = string.Empty;
    private string _selectedProcessFilter = "全部";
    private string _serviceSearchText = string.Empty;
    private string _selectedServiceFilter = "全部";
    private string _startupSearchText = string.Empty;
    private ProcessRow? _selectedProcess;
    private bool _isPaused;
    private bool _isRefreshing;
    private bool _isLoadingServices;
    private bool _isLoadingStartup;
    private bool _servicesLoaded;
    private bool _startupLoaded;
    private bool _initialized;
    private bool _hasDisplaySnapshot;
    private bool _isToastVisible;
    private bool _isToastError;
    private string _toastMessage = string.Empty;
    private DateTime _lastUpdated;
    private double _cpuUsage;
    private double _memoryUsagePercent;
    private ulong _memoryUsedBytes;
    private ulong _memoryTotalBytes;
    private ulong _memoryAvailableBytes;
    private ulong _commitUsedBytes;
    private ulong _commitLimitBytes;
    private double _diskReadMbPerSecond;
    private double _diskWriteMbPerSecond;
    private double _networkDownMbps;
    private double _networkUpMbps;
    private string _activeNetworkName = "未检测到活动网络";
    private string _activeNetworkAddress = "—";
    private string _activeNetworkLinkSpeed = "—";
    private int _processCount;
    private int _applicationCount;
    private int _backgroundCount;
    private long _threadCount;
    private long _handleCount;
    private TimeSpan _uptime;
    private string _healthState = "Good";
    private string _healthTitle = "系统运行良好";
    private string _healthDescription = "正在采集系统状态…";
    private double _overallDiskUsedPercent;
    private long _overallDiskUsedBytes;
    private long _overallDiskTotalBytes;
    private int _totalServices;
    private int _runningServices;
    private int _startupCount;
    private SystemFacts? _systemInfo;
    private int _volumeRefreshTick;
    private UpdateManifest? _availableUpdate;
    private CancellationTokenSource? _updateCancellation;
    private string? _downloadedUpdatePath;
    private string _updateState = "idle";
    private string _updateStatusTitle = "自动更新已开启";
    private string _updateStatusDetail = "启动时会从 GitHub 仓库读取隐藏版本清单。";
    private string _updateReleaseNotes = string.Empty;
    private double _updateProgress;
    private bool _updateAvailable;
    private bool _isUpdateReady;
    private bool _isUpdateProgressVisible;
    private bool _canDownloadUpdate;
    private bool _selfUpdateSupported;
    private string _selfUpdateReason = string.Empty;

    public MainViewModel(SettingsService settingsService, AppSettings settings, DialogService dialogs)
    {
        _settingsService = settingsService;
        _settings = settings;
        _dialogs = dialogs;

        NavItems =
        [
            new("overview", "概览", "M3,3 H9 V9 H3 Z M15,3 H21 V9 H15 Z M3,15 H9 V21 H3 Z M15,15 H21 V21 H15 Z"),
            new("processes", "进程", "M4,6 H20 M4,12 H20 M4,18 H20 M7,4 V8 M16,10 V14 M10,16 V20"),
            new("performance", "性能", "M3,17 L8,11 L12,14 L16,7 L21,10 M3,21 H21"),
            new("startup", "启动应用", "M8,5 L19,12 L8,19 Z"),
            new("services", "服务", "M3,4 H21 V10 H3 Z M3,14 H21 V20 H3 Z M7,7 H7.01 M7,17 H7.01"),
            new("settings", "设置", "M4,7 H20 M4,17 H20 M9,4 V10 M15,14 V20")
        ];

        ThemeOptions =
        [
            new("Dark", "深色", "专注、低光的深色外观"),
            new("Light", "浅色", "明亮、清晰的浅色外观"),
            new("System", "跟随系统", "自动匹配 Windows 主题")
        ];

        RefreshOptions =
        [
            new(1, "1 秒"),
            new(2, "2 秒"),
            new(3, "3 秒"),
            new(5, "5 秒")
        ];

        ProcessFilters = ["全部", "应用", "后台"];
        ServiceFilters = ["全部", "正在运行", "已停止"];

        ProcessView = CollectionViewSource.GetDefaultView(_processes);
        ProcessView.Filter = FilterProcess;
        ProcessView.SortDescriptions.Add(new SortDescription(nameof(ProcessRow.Name), ListSortDirection.Ascending));

        ServicesView = CollectionViewSource.GetDefaultView(Services);
        ServicesView.Filter = FilterService;
        ServicesView.SortDescriptions.Add(new SortDescription(nameof(ServiceRow.DisplayName), ListSortDirection.Ascending));

        StartupView = CollectionViewSource.GetDefaultView(StartupItems);
        StartupView.Filter = FilterStartup;
        StartupView.SortDescriptions.Add(new SortDescription(nameof(StartupRow.Name), ListSortDirection.Ascending));

        for (var index = 0; index < HistoryLength; index++)
        {
            CpuHistory.Add(0);
            MemoryHistory.Add(0);
            DiskReadHistory.Add(0);
            DiskWriteHistory.Add(0);
            NetworkDownHistory.Add(0);
            NetworkUpHistory.Add(0);
        }

        _refreshCommand = new AsyncRelayCommand(() => RefreshSnapshotAsync(), () => !IsRefreshing);
        _endTaskCommand = new AsyncRelayCommand(() => EndProcessAsync(false), () => SelectedProcess is not null);
        _endTreeCommand = new AsyncRelayCommand(() => EndProcessAsync(true), () => SelectedProcess is not null);
        _showDetailsCommand = new RelayCommand(_ => ShowProcessDetails(), _ => SelectedProcess is not null);
        _refreshServicesCommand = new AsyncRelayCommand(() => LoadServicesAsync(force: true), () => !IsLoadingServices);
        _refreshStartupCommand = new AsyncRelayCommand(() => LoadStartupAsync(force: true), () => !IsLoadingStartup);
        _startServiceCommand = new AsyncRelayCommand(async parameter => await ChangeServiceAsync(parameter as ServiceRow, true));
        _stopServiceCommand = new AsyncRelayCommand(async parameter => await ChangeServiceAsync(parameter as ServiceRow, false));
        _updateActionCommand = new AsyncRelayCommand(
            RunUpdateActionAsync,
            () => _updateState is not "checking" and not "downloading");

        RefreshCommand = _refreshCommand;
        EndTaskCommand = _endTaskCommand;
        EndTreeCommand = _endTreeCommand;
        ShowDetailsCommand = _showDetailsCommand;
        RefreshServicesCommand = _refreshServicesCommand;
        RefreshStartupCommand = _refreshStartupCommand;
        StartServiceCommand = _startServiceCommand;
        StopServiceCommand = _stopServiceCommand;

        TogglePauseCommand = new RelayCommand(TogglePause);
        ToggleThemeCommand = new RelayCommand(ToggleTheme);
        RunNewTaskCommand = new AsyncRelayCommand(RunNewTaskAsync);
        OpenProcessLocationCommand = new RelayCommand(_ => OpenSelectedProcessLocation(), _ => SelectedProcess is not null);
        CopyProcessDetailsCommand = new RelayCommand(_ => CopySelectedProcessDetails(), _ => SelectedProcess is not null);
        InspectProcessCommand = new RelayCommand(parameter => InspectProcess(parameter as ProcessRow));
        ShowAllProcessesCommand = new RelayCommand(() => CurrentPage = "processes");
        RefreshServicesManuallyCommand = new AsyncRelayCommand(() => LoadServicesAsync(force: true));
        RefreshStartupManuallyCommand = new AsyncRelayCommand(() => LoadStartupAsync(force: true));
        OpenServicesConsoleCommand = new RelayCommand(OpenServicesConsole);
        OpenStartupSettingsCommand = new RelayCommand(OpenStartupSettings);
        OpenStartupLocationCommand = new RelayCommand(parameter => OpenStartupLocation(parameter as StartupRow));
        OpenRepositoryCommand = new RelayCommand(OpenRepository);
        OpenReleasePageCommand = new RelayCommand(OpenReleasePage);
        ShowUpdatePageCommand = new RelayCommand(() => CurrentPage = "settings");
        UpdateActionCommand = _updateActionCommand;

        _selfUpdateSupported = _updates.CanSelfUpdate(out _selfUpdateReason);
        if (!_settings.AutoUpdate)
        {
            _updateStatusTitle = "自动检查已关闭";
            _updateStatusDetail = "可随时手动检查更新。";
        }

        _settings.PropertyChanged += OnSettingsChanged;
        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(_settings.RefreshSeconds)
        };
        _timer.Tick += OnTimerTick;

        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3.4) };
        _toastTimer.Tick += (_, _) =>
        {
            _toastTimer.Stop();
            IsToastVisible = false;
        };
    }

    public AppSettings Settings => _settings;

    public IReadOnlyList<NavItem> NavItems { get; }
    public IReadOnlyList<ThemeOption> ThemeOptions { get; }
    public IReadOnlyList<RefreshOption> RefreshOptions { get; }
    public IReadOnlyList<string> ProcessFilters { get; }
    public IReadOnlyList<string> ServiceFilters { get; }
    public string AppVersion => AppInfo.VersionText;
    public string RepositoryUrl => AppInfo.RepositoryUrl;
    public string UpdateState => _updateState;
    public string UpdateStatusTitle => _updateStatusTitle;
    public string UpdateStatusDetail => _updateStatusDetail;
    public string UpdateReleaseNotes => _updateReleaseNotes;
    public bool HasUpdateReleaseNotes => !string.IsNullOrWhiteSpace(_updateReleaseNotes);
    public double UpdateProgress => _updateProgress;
    public bool IsUpdateProgressVisible => _isUpdateProgressVisible;
    public bool IsUpdateReady => _isUpdateReady;
    public bool IsUpdateAvailable => _updateAvailable;
    public bool IsUpdateIndicatorVisible => _updateAvailable || _isUpdateReady;
    public bool IsSelfUpdateSupported => _selfUpdateSupported;
    public string UpdateBadgeText => _isUpdateReady
        ? "重启更新"
        : _updateState == "downloading"
            ? "更新中"
            : _availableUpdate is { } update
                ? $"v{update.Version}"
                : "更新";
    public string UpdateActionText => _updateState switch
    {
        "checking" => "正在检查…",
        "available" => _selfUpdateSupported && _canDownloadUpdate ? "下载更新" : "打开发布页",
        "downloading" => $"下载中 {_updateProgress:0}%",
        "ready" => "重启并安装",
        "up-to-date" => "再次检查",
        "failed" => "重试",
        "download-failed" => "重新下载",
        "manual" => "打开发布页",
        _ => "检查更新"
    };

    public ICollectionView ProcessView { get; }
    public ICollectionView ServicesView { get; }
    public ICollectionView StartupView { get; }
    public ObservableCollection<ServiceRow> Services { get; } = new();
    public ObservableCollection<StartupRow> StartupItems { get; } = new();
    public ObservableCollection<ProcessRow> TopProcesses { get; } = new();
    public ObservableCollection<VolumeInfo> Volumes { get; } = new();
    public ObservableCollection<NetworkAdapterInfo> NetworkAdapters { get; } = new();
    public ObservableCollection<double> CpuHistory { get; } = new();
    public ObservableCollection<double> MemoryHistory { get; } = new();
    public ObservableCollection<double> DiskReadHistory { get; } = new();
    public ObservableCollection<double> DiskWriteHistory { get; } = new();
    public ObservableCollection<double> NetworkDownHistory { get; } = new();
    public ObservableCollection<double> NetworkUpHistory { get; } = new();

    public ICommand RefreshCommand { get; }
    public ICommand EndTaskCommand { get; }
    public ICommand EndTreeCommand { get; }
    public ICommand ShowDetailsCommand { get; }
    public ICommand RefreshServicesCommand { get; }
    public ICommand RefreshStartupCommand { get; }
    public ICommand StartServiceCommand { get; }
    public ICommand StopServiceCommand { get; }
    public ICommand TogglePauseCommand { get; }
    public ICommand ToggleThemeCommand { get; }
    public ICommand RunNewTaskCommand { get; }
    public ICommand OpenProcessLocationCommand { get; }
    public ICommand CopyProcessDetailsCommand { get; }
    public ICommand InspectProcessCommand { get; }
    public ICommand ShowAllProcessesCommand { get; }
    public ICommand RefreshServicesManuallyCommand { get; }
    public ICommand RefreshStartupManuallyCommand { get; }
    public ICommand OpenServicesConsoleCommand { get; }
    public ICommand OpenStartupSettingsCommand { get; }
    public ICommand OpenStartupLocationCommand { get; }
    public ICommand OpenRepositoryCommand { get; }
    public ICommand OpenReleasePageCommand { get; }
    public ICommand ShowUpdatePageCommand { get; }
    public ICommand UpdateActionCommand { get; }

    public string CurrentPage
    {
        get => _currentPage;
        set
        {
            if (!SetProperty(ref _currentPage, value))
            {
                return;
            }

            OnPropertyChanged(nameof(PageTitle));
            if (value == "services" && !_servicesLoaded && !IsLoadingServices)
            {
                _ = LoadServicesAsync();
            }
            else if (value == "startup" && !_startupLoaded && !IsLoadingStartup)
            {
                _ = LoadStartupAsync();
            }
        }
    }

    public string PageTitle => _currentPage switch
    {
        "processes" => "进程",
        "performance" => "性能",
        "startup" => "启动应用",
        "services" => "服务",
        "settings" => "设置",
        _ => "概览"
    };

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                ProcessView.Refresh();
            }
        }
    }

    public string SelectedProcessFilter
    {
        get => _selectedProcessFilter;
        set
        {
            if (SetProperty(ref _selectedProcessFilter, value))
            {
                ProcessView.Refresh();
            }
        }
    }

    public string ServiceSearchText
    {
        get => _serviceSearchText;
        set
        {
            if (SetProperty(ref _serviceSearchText, value))
            {
                ServicesView.Refresh();
            }
        }
    }

    public string SelectedServiceFilter
    {
        get => _selectedServiceFilter;
        set
        {
            if (SetProperty(ref _selectedServiceFilter, value))
            {
                ServicesView.Refresh();
            }
        }
    }

    public string StartupSearchText
    {
        get => _startupSearchText;
        set
        {
            if (SetProperty(ref _startupSearchText, value))
            {
                StartupView.Refresh();
            }
        }
    }

    public ProcessRow? SelectedProcess
    {
        get => _selectedProcess;
        set
        {
            if (!SetProperty(ref _selectedProcess, value))
            {
                return;
            }

            NotifyProcessCommands();
            OnPropertyChanged(nameof(SelectedProcessName));
            OnPropertyChanged(nameof(SelectedProcessDescription));
        }
    }

    public string SelectedProcessName => SelectedProcess?.Name ?? "未选择进程";
    public string SelectedProcessDescription => SelectedProcess is null
        ? "在下方列表中选择一个进程以查看操作"
        : $"PID {SelectedProcess.Pid} · {SelectedProcess.KindText} · {SelectedProcess.Subtitle}";

    public bool IsPaused
    {
        get => _isPaused;
        private set
        {
            if (SetProperty(ref _isPaused, value))
            {
                OnPropertyChanged(nameof(LiveStatusText));
                OnPropertyChanged(nameof(LiveStatusDetail));
                OnPropertyChanged(nameof(PauseButtonText));
            }
        }
    }

    public string LiveStatusText => IsPaused ? "监控已暂停" : "实时监控中";
    public string LiveStatusDetail => IsPaused
        ? "点击继续以恢复自动刷新"
        : $"每 {_settings.RefreshSeconds} 秒自动刷新 · 本地采集";
    public string PauseButtonText => IsPaused ? "继续监控" : "暂停监控";

    public bool IsRefreshing
    {
        get => _isRefreshing;
        private set
        {
            if (SetProperty(ref _isRefreshing, value))
            {
                _refreshCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsLoadingServices
    {
        get => _isLoadingServices;
        private set
        {
            if (SetProperty(ref _isLoadingServices, value))
            {
                _refreshServicesCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsLoadingStartup
    {
        get => _isLoadingStartup;
        private set
        {
            if (SetProperty(ref _isLoadingStartup, value))
            {
                _refreshStartupCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsServicesLoaded
    {
        get => _servicesLoaded;
        private set => SetProperty(ref _servicesLoaded, value);
    }

    public bool IsStartupLoaded
    {
        get => _startupLoaded;
        private set => SetProperty(ref _startupLoaded, value);
    }

    public double CpuUsage
    {
        get => _cpuUsage;
        private set => SetProperty(ref _cpuUsage, value);
    }

    public double MemoryUsagePercent
    {
        get => _memoryUsagePercent;
        private set
        {
            if (SetProperty(ref _memoryUsagePercent, value))
            {
                OnPropertyChanged(nameof(MemoryUsagePercentText));
            }
        }
    }

    public string MemoryUsagePercentText => $"{MemoryUsagePercent:0}%";
    public string MemoryUsedText => Formatters.Bytes(_memoryUsedBytes);
    public string MemoryTotalText => Formatters.Bytes(_memoryTotalBytes);
    public string MemoryAvailableText => Formatters.Bytes(_memoryAvailableBytes);
    public string CommitUsedText => Formatters.Bytes(_commitUsedBytes);
    public string CommitText => $"{Formatters.Bytes(_commitUsedBytes)} / {Formatters.Bytes(_commitLimitBytes)}";

    public double DiskReadMbPerSecond
    {
        get => _diskReadMbPerSecond;
        private set
        {
            if (SetProperty(ref _diskReadMbPerSecond, value))
            {
                OnPropertyChanged(nameof(DiskReadText));
                OnPropertyChanged(nameof(DiskThroughputText));
            }
        }
    }

    public double DiskWriteMbPerSecond
    {
        get => _diskWriteMbPerSecond;
        private set
        {
            if (SetProperty(ref _diskWriteMbPerSecond, value))
            {
                OnPropertyChanged(nameof(DiskWriteText));
                OnPropertyChanged(nameof(DiskThroughputText));
            }
        }
    }

    public string DiskReadText => Formatters.MemorySpeed(DiskReadMbPerSecond);
    public string DiskWriteText => Formatters.MemorySpeed(DiskWriteMbPerSecond);
    public string DiskThroughputText => Formatters.MemorySpeed(DiskReadMbPerSecond + DiskWriteMbPerSecond);

    public double NetworkDownMbps
    {
        get => _networkDownMbps;
        private set
        {
            if (SetProperty(ref _networkDownMbps, value))
            {
                OnPropertyChanged(nameof(NetworkDownText));
            }
        }
    }

    public double NetworkUpMbps
    {
        get => _networkUpMbps;
        private set
        {
            if (SetProperty(ref _networkUpMbps, value))
            {
                OnPropertyChanged(nameof(NetworkUpText));
            }
        }
    }

    public string NetworkDownText => Formatters.NetworkSpeed(NetworkDownMbps);
    public string NetworkUpText => Formatters.NetworkSpeed(NetworkUpMbps);
    public string ActiveNetworkName => _activeNetworkName;
    public string ActiveNetworkAddress => _activeNetworkAddress;
    public string ActiveNetworkLinkSpeed => _activeNetworkLinkSpeed;

    public int ProcessCount
    {
        get => _processCount;
        private set
        {
            if (SetProperty(ref _processCount, value))
            {
                OnPropertyChanged(nameof(ProcessCountText));
            }
        }
    }

    public int ApplicationCount
    {
        get => _applicationCount;
        private set
        {
            if (SetProperty(ref _applicationCount, value))
            {
                OnPropertyChanged(nameof(ApplicationCountText));
            }
        }
    }

    public int BackgroundCount
    {
        get => _backgroundCount;
        private set
        {
            if (SetProperty(ref _backgroundCount, value))
            {
                OnPropertyChanged(nameof(BackgroundCountText));
            }
        }
    }

    public string ProcessCountText => $"{ProcessCount} 个进程";
    public string ApplicationCountText => $"{ApplicationCount} 个应用";
    public string BackgroundCountText => $"{BackgroundCount} 个后台";

    public string ThreadsText => Formatters.Count(_threadCount);
    public string HandlesText => Formatters.Count(_handleCount);
    public string UptimeText => Formatters.Uptime(_uptime);
    public string LastUpdatedText => _lastUpdated == default ? "正在采集…" : $"更新于 {_lastUpdated:HH:mm:ss}";
    public string RefreshRateText => $"每 {_settings.RefreshSeconds} 秒刷新";
    public string HistoryWindowText => $"最近 {_settings.RefreshSeconds * HistoryLength} 秒";

    public string HealthState
    {
        get => _healthState;
        private set => SetProperty(ref _healthState, value);
    }

    public string HealthTitle
    {
        get => _healthTitle;
        private set => SetProperty(ref _healthTitle, value);
    }

    public string HealthDescription
    {
        get => _healthDescription;
        private set => SetProperty(ref _healthDescription, value);
    }

    public double OverallDiskUsedPercent
    {
        get => _overallDiskUsedPercent;
        private set
        {
            if (SetProperty(ref _overallDiskUsedPercent, value))
            {
                OnPropertyChanged(nameof(OverallDiskUsedPercentText));
            }
        }
    }

    public string OverallDiskUsedPercentText => $"{OverallDiskUsedPercent:0}%";
    public string OverallDiskUsedText => $"{Formatters.Bytes(_overallDiskUsedBytes)} / {Formatters.Bytes(_overallDiskTotalBytes)}";
    public string CpuNameText => _systemInfo?.CpuName ?? "正在读取处理器信息…";
    public string CpuSubtitleText => _systemInfo is null
        ? "正在读取设备信息…"
        : $"{_systemInfo.LogicalProcessors} 个逻辑处理器 · {_systemInfo.CpuBaseClockText}";
    public string MachineText => _systemInfo?.ComputerName ?? Environment.MachineName;
    public string OsDisplayText => _systemInfo?.OsDisplay ?? "Windows";
    public string UserNameText => _systemInfo?.UserName ?? Environment.UserName;
    public string SystemMemoryText => _systemInfo?.TotalMemoryText ?? "—";
    public string SystemModelText => Formatters.Shorten(_systemInfo?.MachineText, 34);
    public string DotNetVersionText => _systemInfo?.DotNetVersion ?? string.Empty;

    public SystemFacts? SystemInfo
    {
        get => _systemInfo;
        private set
        {
            if (SetProperty(ref _systemInfo, value))
            {
                OnPropertyChanged(nameof(CpuNameText));
                OnPropertyChanged(nameof(CpuSubtitleText));
                OnPropertyChanged(nameof(MachineText));
                OnPropertyChanged(nameof(OsDisplayText));
                OnPropertyChanged(nameof(UserNameText));
                OnPropertyChanged(nameof(SystemMemoryText));
                OnPropertyChanged(nameof(SystemModelText));
                OnPropertyChanged(nameof(DotNetVersionText));
            }
        }
    }

    public int TotalServices
    {
        get => _totalServices;
        private set
        {
            if (SetProperty(ref _totalServices, value))
            {
                OnPropertyChanged(nameof(ServiceSummaryText));
            }
        }
    }

    public int RunningServices
    {
        get => _runningServices;
        private set
        {
            if (SetProperty(ref _runningServices, value))
            {
                OnPropertyChanged(nameof(ServiceSummaryText));
            }
        }
    }

    public string ServiceSummaryText => TotalServices == 0
        ? "点击刷新以加载服务"
        : $"共 {TotalServices} 项服务 · {RunningServices} 项正在运行";

    public int StartupCount
    {
        get => _startupCount;
        private set
        {
            if (SetProperty(ref _startupCount, value))
            {
                OnPropertyChanged(nameof(StartupSummaryText));
            }
        }
    }

    public string StartupSummaryText => StartupCount == 0
        ? "未发现启动项，或尚未加载"
        : $"发现 {StartupCount} 个登录启动项";

    public bool IsToastVisible
    {
        get => _isToastVisible;
        private set => SetProperty(ref _isToastVisible, value);
    }

    public string ToastMessage
    {
        get => _toastMessage;
        private set => SetProperty(ref _toastMessage, value);
    }

    public bool IsToastError
    {
        get => _isToastError;
        private set => SetProperty(ref _isToastError, value);
    }

    public bool HasStartupItems => StartupItems.Count > 0;
    public bool HasNetworkAdapters => NetworkAdapters.Count > 0;
    public bool HasVolumes => Volumes.Count > 0;
    public bool HasServices => Services.Count > 0;

    public async Task InitializeAsync()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;

        try
        {
            // Prime the deltas so the first painted frame already has meaningful percentages.
            await Task.Run(_monitor.Collect);
            await Task.Delay(350);
        }
        catch
        {
            // The regular refresh below reports any persistent collection failure.
        }

        await RefreshSnapshotAsync(showErrors: true);
        _timer.Start();
        OnPropertyChanged(nameof(LiveStatusText));

        _updates.CleanupOldFiles();
        if (_settings.AutoUpdate)
        {
            _ = CheckForUpdatesAsync(manual: false);
        }
    }

    public void Shutdown()
    {
        _timer.Stop();
        _toastTimer.Stop();
        _settings.PropertyChanged -= OnSettingsChanged;
        _updateCancellation?.Cancel();
        _updateCancellation?.Dispose();
        _updateCancellation = null;
        _updates.Dispose();
        _monitor.Dispose();
    }

    private async void OnTimerTick(object? sender, EventArgs e)
    {
        await RefreshSnapshotAsync();
    }

    public async Task RefreshSnapshotAsync(bool showErrors = false)
    {
        if (IsRefreshing)
        {
            return;
        }

        IsRefreshing = true;
        try
        {
            var snapshot = await Task.Run(_monitor.Collect);
            ApplySnapshot(snapshot);
        }
        catch (Exception exception)
        {
            if (showErrors)
            {
                _dialogs.ShowMessage("无法读取系统信息", exception.Message);
            }
            else
            {
                ShowToast("刷新失败：" + exception.Message, true);
            }
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    private void ApplySnapshot(SystemSnapshot snapshot)
    {
        CpuUsage = Math.Round(snapshot.CpuUsage, 1);
        MemoryUsagePercent = Math.Round(snapshot.MemoryUsagePercent, 1);
        _memoryUsedBytes = snapshot.UsedMemoryBytes;
        _memoryTotalBytes = snapshot.TotalMemoryBytes;
        _memoryAvailableBytes = snapshot.AvailableMemoryBytes;
        _commitUsedBytes = snapshot.CommitUsedBytes;
        _commitLimitBytes = snapshot.CommitLimitBytes;
        DiskReadMbPerSecond = Math.Round(snapshot.DiskReadMbPerSecond, 2);
        DiskWriteMbPerSecond = Math.Round(snapshot.DiskWriteMbPerSecond, 2);
        NetworkDownMbps = Math.Round(snapshot.NetworkDownMbps, 2);
        NetworkUpMbps = Math.Round(snapshot.NetworkUpMbps, 2);
        _threadCount = snapshot.TotalThreads;
        _handleCount = snapshot.TotalHandles;
        _uptime = snapshot.Uptime;
        _lastUpdated = snapshot.Timestamp;
        SystemInfo = snapshot.Facts;

        ProcessCount = snapshot.Processes.Count;
        ApplicationCount = snapshot.Processes.Count(process => process.HasWindow);
        BackgroundCount = ProcessCount - ApplicationCount;

        OnPropertyChanged(nameof(MemoryUsedText));
        OnPropertyChanged(nameof(MemoryTotalText));
        OnPropertyChanged(nameof(MemoryAvailableText));
        OnPropertyChanged(nameof(CommitUsedText));
        OnPropertyChanged(nameof(CommitText));
        OnPropertyChanged(nameof(ThreadsText));
        OnPropertyChanged(nameof(HandlesText));
        OnPropertyChanged(nameof(UptimeText));
        OnPropertyChanged(nameof(LastUpdatedText));
        OnPropertyChanged(nameof(HistoryWindowText));
        OnPropertyChanged(nameof(RefreshRateText));

        UpdateProcessCollection(snapshot.Processes);
        UpdateTopProcesses();
        UpdateNetworkAdapters(snapshot.NetworkAdapters);
        UpdateVolumes(snapshot.Volumes);
        UpdateHealth();
        AppendSnapshotValues(snapshot);
        LoadVisibleProcessIcons();
    }

    private void UpdateProcessCollection(IReadOnlyList<ProcessSnapshot> snapshots)
    {
        var active = new HashSet<int>(snapshots.Select(process => process.Pid));
        var membershipChanged = false;

        foreach (var existing in _processes.Where(process => !active.Contains(process.Pid)).ToArray())
        {
            _processes.Remove(existing);
            membershipChanged = true;
            if (ReferenceEquals(existing, SelectedProcess))
            {
                SelectedProcess = null;
            }
        }

        var existingById = _processes.ToDictionary(process => process.Pid);
        foreach (var snapshot in snapshots)
        {
            if (existingById.TryGetValue(snapshot.Pid, out var row))
            {
                row.Update(snapshot);
            }
            else
            {
                var created = new ProcessRow(snapshot);
                _processes.Add(created);
                membershipChanged = true;
            }
        }

        if (membershipChanged)
        {
            ProcessView.Refresh();
        }

        NotifyProcessCommands();
    }

    private void UpdateTopProcesses()
    {
        var top = _processes
            .OrderByDescending(process => process.CpuPercent)
            .ThenByDescending(process => process.MemoryMb)
            .Take(5)
            .ToList();

        TopProcesses.Clear();
        foreach (var process in top)
        {
            TopProcesses.Add(process);
        }
    }

    private void UpdateNetworkAdapters(IReadOnlyList<NetworkAdapterInfo> adapters)
    {
        NetworkAdapters.Clear();
        foreach (var adapter in adapters)
        {
            NetworkAdapters.Add(adapter);
        }

        var active = adapters.FirstOrDefault();
        _activeNetworkName = active?.DisplayName ?? "未检测到活动网络";
        _activeNetworkAddress = active?.Address ?? "—";
        _activeNetworkLinkSpeed = active?.LinkSpeedText ?? "—";

        OnPropertyChanged(nameof(ActiveNetworkName));
        OnPropertyChanged(nameof(ActiveNetworkAddress));
        OnPropertyChanged(nameof(ActiveNetworkLinkSpeed));
        OnPropertyChanged(nameof(HasNetworkAdapters));
    }

    private void UpdateVolumes(IReadOnlyList<VolumeInfo> volumes)
    {
        _volumeRefreshTick++;
        if (_volumeRefreshTick % 5 != 1 && Volumes.Count == volumes.Count)
        {
            return;
        }

        Volumes.Clear();
        foreach (var volume in volumes)
        {
            Volumes.Add(volume);
        }

        var total = volumes.Sum(volume => volume.TotalBytes);
        var used = volumes.Sum(volume => volume.UsedBytes);
        _overallDiskUsedBytes = used;
        _overallDiskTotalBytes = total;
        OverallDiskUsedPercent = total > 0 ? used * 100d / total : 0;

        OnPropertyChanged(nameof(OverallDiskUsedText));
        OnPropertyChanged(nameof(HasVolumes));
    }

    private void UpdateHealth()
    {
        var cpu = CpuUsage;
        var memory = MemoryUsagePercent;

        if (cpu >= 90 || memory >= 90)
        {
            HealthState = "Critical";
            HealthTitle = "系统繁忙";
            HealthDescription = "资源使用接近上限，建议关闭占用较高的应用。";
        }
        else if (cpu >= 70 || memory >= 78)
        {
            HealthState = "Busy";
            HealthTitle = "负载偏高";
            HealthDescription = "部分资源占用较高，可以关注下方的高占用进程。";
        }
        else
        {
            HealthState = "Good";
            HealthTitle = "系统运行良好";
            HealthDescription = "处理器与内存处于舒适区间，系统响应正常。";
        }
    }

    private void AppendSnapshotValues(SystemSnapshot snapshot)
    {
        if (!_hasDisplaySnapshot)
        {
            _hasDisplaySnapshot = true;
            FillHistory(CpuHistory, snapshot.CpuUsage);
            FillHistory(MemoryHistory, snapshot.MemoryUsagePercent);
        }

        AppendHistory(CpuHistory, snapshot.CpuUsage);
        AppendHistory(MemoryHistory, snapshot.MemoryUsagePercent);
        AppendHistory(DiskReadHistory, snapshot.DiskReadMbPerSecond);
        AppendHistory(DiskWriteHistory, snapshot.DiskWriteMbPerSecond);
        AppendHistory(NetworkDownHistory, snapshot.NetworkDownMbps);
        AppendHistory(NetworkUpHistory, snapshot.NetworkUpMbps);
    }

    private static void FillHistory(ObservableCollection<double> history, double value)
    {
        for (var index = 0; index < history.Count; index++)
        {
            history[index] = Math.Round(value, 2);
        }
    }

    private static void AppendHistory(ObservableCollection<double> history, double value)
    {
        if (history.Count >= HistoryLength)
        {
            history.RemoveAt(0);
        }

        history.Add(Math.Round(value, 2));
    }

    private void LoadVisibleProcessIcons()
    {
        if (!_settings.ShowProcessIcons)
        {
            return;
        }

        var iconLimit = CurrentPage == "processes" ? 20 : 10;
        var attemptLimit = CurrentPage == "processes" ? 65 : 36;
        var loaded = 0;
        var attempts = 0;
        var visited = new HashSet<int>();

        IEnumerable<ProcessRow> Candidates()
        {
            foreach (var process in TopProcesses)
            {
                yield return process;
            }

            foreach (var process in _processes.Where(process => process.HasWindow).OrderByDescending(process => process.CpuPercent))
            {
                yield return process;
            }

            foreach (var process in _processes)
            {
                yield return process;
            }
        }

        foreach (var process in Candidates())
        {
            if (!visited.Add(process.Pid) || process.HasIcon || string.IsNullOrWhiteSpace(process.Path))
            {
                continue;
            }

            var path = process.Path;
            if (_failedIconPaths.Contains(path))
            {
                continue;
            }

            var icon = IconLoader.Load(path);
            attempts++;
            if (icon is null)
            {
                _failedIconPaths.Add(path);
                if (attempts >= attemptLimit)
                {
                    break;
                }

                continue;
            }

            foreach (var target in _processes.Where(row =>
                         !row.HasIcon &&
                         string.Equals(row.Path, path, StringComparison.OrdinalIgnoreCase)))
            {
                target.IconSource = icon;
            }

            loaded++;
            if (loaded >= iconLimit || attempts >= attemptLimit)
            {
                break;
            }
        }
    }

    private bool FilterProcess(object item)
    {
        if (item is not ProcessRow process)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var query = SearchText.Trim();
            var matches = process.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                          process.Pid.ToString().Contains(query, StringComparison.Ordinal) ||
                          process.Subtitle.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                          (process.Path?.Contains(query, StringComparison.CurrentCultureIgnoreCase) ?? false);
            if (!matches)
            {
                return false;
            }
        }

        return SelectedProcessFilter switch
        {
            "应用" => process.HasWindow,
            "后台" => !process.HasWindow,
            _ => true
        };
    }

    private bool FilterService(object item)
    {
        if (item is not ServiceRow service)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(ServiceSearchText))
        {
            var query = ServiceSearchText.Trim();
            if (!service.DisplayName.Contains(query, StringComparison.CurrentCultureIgnoreCase) &&
                !service.Name.Contains(query, StringComparison.OrdinalIgnoreCase) &&
                !service.Description.Contains(query, StringComparison.CurrentCultureIgnoreCase))
            {
                return false;
            }
        }

        return SelectedServiceFilter switch
        {
            "正在运行" => service.IsRunning,
            "已停止" => service.StatusCode == "Stopped",
            _ => true
        };
    }

    private bool FilterStartup(object item)
    {
        if (item is not StartupRow startup || string.IsNullOrWhiteSpace(StartupSearchText))
        {
            return true;
        }

        var query = StartupSearchText.Trim();
        return startup.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
               startup.Command.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
               startup.Publisher.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
               startup.Source.Contains(query, StringComparison.CurrentCultureIgnoreCase);
    }

    private void NotifyProcessCommands()
    {
        _endTaskCommand.RaiseCanExecuteChanged();
        _endTreeCommand.RaiseCanExecuteChanged();
        _showDetailsCommand.RaiseCanExecuteChanged();
        (OpenProcessLocationCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (CopyProcessDetailsCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    private void TogglePause()
    {
        if (IsPaused)
        {
            _timer.Start();
            IsPaused = false;
            ShowToast("已继续实时监控");
        }
        else
        {
            _timer.Stop();
            IsPaused = true;
        }
    }

    private void ToggleTheme()
    {
        var target = ThemeManager.IsDark ? "Light" : "Dark";
        _settings.ThemeMode = target;
    }

    private void InspectProcess(ProcessRow? process)
    {
        if (process is null)
        {
            return;
        }

        SearchText = string.Empty;
        SelectedProcessFilter = "全部";
        SelectedProcess = process;
        CurrentPage = "processes";
    }

    private async Task EndProcessAsync(bool entireTree)
    {
        var process = SelectedProcess;
        if (process is null)
        {
            return;
        }

        if (process.Pid == Environment.ProcessId)
        {
            _dialogs.ShowMessage("无法结束当前应用", "不能从任务管理器内部结束 HWZ 任务管理器自身。请使用窗口右上角的关闭按钮退出。");
            return;
        }

        if (process.IsProtected)
        {
            _dialogs.ShowMessage(
                "受保护的系统进程",
                $"“{process.Name}”是 Windows 核心系统进程。结束它可能导致系统不稳定或立即重启，因此此操作已被阻止。");
            return;
        }

        if (_settings.ConfirmOnEnd)
        {
            var message = $"确定要结束“{process.Name}”（PID {process.Pid}）吗？\n\n未保存的数据可能会丢失。";
            if (entireTree)
            {
                message += "\n\n将同时结束该进程创建的所有子进程。";
            }

            var title = entireTree ? "结束进程树" : "结束任务";
            if (!_dialogs.Confirm(title, message, "结束进程", destructive: true))
            {
                return;
            }
        }

        try
        {
            await Task.Run(() =>
            {
                using var target = Process.GetProcessById(process.Pid);

                if (!entireTree && process.HasWindow)
                {
                    try
                    {
                        if (target.CloseMainWindow() && target.WaitForExit(1400))
                        {
                            return;
                        }
                    }
                    catch
                    {
                        // Fall back to terminating the process.
                    }
                }

                target.Kill(entireTree);
                target.WaitForExit(2500);
            });

            ShowToast($"已结束 {process.Name}");
            await RefreshSnapshotAsync();
        }
        catch (ArgumentException)
        {
            ShowToast("该进程已经退出");
            await RefreshSnapshotAsync();
        }
        catch (System.ComponentModel.Win32Exception exception) when (exception.NativeErrorCode == 5)
        {
            _dialogs.ShowMessage(
                "权限不足",
                "Windows 拒绝了这个操作。该进程可能以更高权限运行，请尝试以管理员身份启动 HWZ 任务管理器。");
        }
        catch (Exception exception)
        {
            _dialogs.ShowMessage("无法结束进程", exception.Message);
        }
    }

    private void ShowProcessDetails()
    {
        var process = SelectedProcess;
        if (process is null)
        {
            return;
        }

        var details = BuildProcessDetails(process);
        _dialogs.ShowDetails(
            "进程详情",
            process.Name,
            $"PID {process.Pid} · {process.KindText}",
            details);
    }

    private static IReadOnlyList<Dialogs.DialogDetail> BuildProcessDetails(ProcessRow process)
    {
        return
        [
            new("进程名称", process.Name),
            new("进程 ID", process.Pid.ToString()),
            new("类型", process.KindText),
            new("状态", process.StatusText),
            new("发布者", string.IsNullOrWhiteSpace(process.CompanyName) ? "—" : process.CompanyName),
            new("文件说明", string.IsNullOrWhiteSpace(process.Description) ? "—" : process.Description),
            new("可执行文件", string.IsNullOrWhiteSpace(process.Path) ? "受保护或不可用" : process.Path),
            new("启动时间", process.StartTimeText),
            new("会话", process.SessionText),
            new("CPU", process.CpuText),
            new("内存（工作集）", process.MemoryText),
            new("私有内存", process.PrivateMemoryText),
            new("磁盘 I/O", process.DiskText),
            new("线程", process.ThreadCount.ToString()),
            new("句柄", process.HandleCount.ToString()),
            new("基础优先级", process.BasePriority.ToString()),
            new("窗口标题", string.IsNullOrWhiteSpace(process.WindowTitle) ? "—" : process.WindowTitle)
        ];
    }

    private void CopySelectedProcessDetails()
    {
        var process = SelectedProcess;
        if (process is null)
        {
            return;
        }

        try
        {
            var lines = new List<string> { $"进程：{process.Name} (PID {process.Pid})" };
            lines.AddRange(BuildProcessDetails(process).Skip(2).Select(detail => $"{detail.Title}：{detail.Value}"));
            Clipboard.SetText(string.Join(Environment.NewLine, lines));
            ShowToast("进程详情已复制");
        }
        catch (Exception exception)
        {
            ShowToast("复制失败：" + exception.Message, true);
        }
    }

    private void OpenSelectedProcessLocation()
    {
        var process = SelectedProcess;
        if (process is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(process.Path))
        {
            _dialogs.ShowMessage("无法定位文件", "该进程的路径不可用，可能受到系统保护或权限限制。");
            return;
        }

        OpenFileInExplorer(process.Path);
    }

    private async Task RunNewTaskAsync()
    {
        var result = _dialogs.Prompt(
            "运行新任务",
            "输入要运行的程序、命令或文件路径，支持参数。含空格的路径请使用英文引号包裹，例如：\"C:\\Program Files\\App\\app.exe\" --safe。",
            string.Empty,
            allowRunAsAdmin: true);

        if (result is null || string.IsNullOrWhiteSpace(result.Text))
        {
            return;
        }

        var (fileName, arguments) = ParseCommandLine(result.Text);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return;
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = true
            };

            if (result.RunAsAdministrator)
            {
                startInfo.Verb = "runas";
            }

            Process.Start(startInfo);
            ShowToast("任务已启动");
            await Task.Delay(700);
            await RefreshSnapshotAsync();
        }
        catch (System.ComponentModel.Win32Exception exception) when (exception.NativeErrorCode == 1223)
        {
            ShowToast("已取消管理员权限请求");
        }
        catch (Exception exception)
        {
            _dialogs.ShowMessage("无法启动任务", exception.Message);
        }
    }

    private static (string FileName, string Arguments) ParseCommandLine(string commandLine)
    {
        var command = commandLine.Trim();
        if (command.Length == 0)
        {
            return (string.Empty, string.Empty);
        }

        if (command[0] == '"')
        {
            var closingQuote = command.IndexOf('"', 1);
            if (closingQuote > 1)
            {
                var quotedPath = command[1..closingQuote];
                var rest = closingQuote + 1 < command.Length
                    ? command[(closingQuote + 1)..].Trim()
                    : string.Empty;
                return (quotedPath, rest);
            }
        }

        if (File.Exists(command))
        {
            return (command, string.Empty);
        }

        var match = Regex.Match(command, @"^(?<file>\S+)(?:\s+(?<args>.*))?$");
        if (!match.Success)
        {
            return (command, string.Empty);
        }

        return (
            match.Groups["file"].Value,
            match.Groups["args"].Success ? match.Groups["args"].Value : string.Empty);
    }

    public async Task LoadServicesAsync(bool force = false)
    {
        if (IsLoadingServices || (_servicesLoaded && !force))
        {
            return;
        }

        IsLoadingServices = true;
        try
        {
            var loaded = await ServiceCatalog.LoadAsync();
            Services.Clear();
            foreach (var service in loaded)
            {
                Services.Add(service);
            }

            ServicesView.Refresh();
            IsServicesLoaded = true;
            TotalServices = Services.Count;
            RunningServices = Services.Count(service => service.IsRunning);
            OnPropertyChanged(nameof(HasServices));
            OnPropertyChanged(nameof(ServiceSummaryText));
        }
        catch (Exception exception)
        {
            ShowToast("加载服务失败：" + exception.Message, true);
        }
        finally
        {
            IsLoadingServices = false;
        }
    }

    private async Task ChangeServiceAsync(ServiceRow? service, bool start)
    {
        if (service is null)
        {
            return;
        }

        if (!start &&
            !_dialogs.Confirm(
                "停止服务",
                $"确定要停止“{service.DisplayName}”吗？\n\n停止系统服务可能会影响依赖它的程序或 Windows 功能。",
                "停止服务",
                destructive: true))
        {
            return;
        }

        var action = start ? "启动" : "停止";
        var result = start
            ? await ServiceCatalog.StartAsync(service.Name)
            : await ServiceCatalog.StopAsync(service.Name);

        if (result.Success)
        {
            ShowToast($"已{action}服务 {service.DisplayName}");
        }
        else
        {
            _dialogs.ShowMessage(
                $"无法{action}服务",
                $"{result.Message}\n\n提示：管理系统服务通常需要以管理员身份运行 HWZ 任务管理器。");
        }

        await LoadServicesAsync(force: true);
    }

    public async Task LoadStartupAsync(bool force = false)
    {
        if (IsLoadingStartup || (_startupLoaded && !force))
        {
            return;
        }

        IsLoadingStartup = true;
        try
        {
            var loaded = await StartupCatalog.LoadAsync();
            StartupItems.Clear();
            foreach (var item in loaded)
            {
                StartupItems.Add(item);
                if (_settings.ShowProcessIcons)
                {
                    item.IconSource = IconLoader.Load(item.ExecutablePath);
                }
            }

            StartupView.Refresh();
            IsStartupLoaded = true;
            StartupCount = StartupItems.Count;
            OnPropertyChanged(nameof(HasStartupItems));
            OnPropertyChanged(nameof(StartupSummaryText));
        }
        catch (Exception exception)
        {
            ShowToast("加载启动项失败：" + exception.Message, true);
        }
        finally
        {
            IsLoadingStartup = false;
        }
    }

    private void OpenStartupLocation(StartupRow? startup)
    {
        if (startup is null)
        {
            return;
        }

        var path = startup.ExecutablePath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            _dialogs.ShowMessage("无法定位文件", $"找不到启动项对应的文件：\n{startup.Command}");
            return;
        }

        OpenFileInExplorer(path);
    }

    private void OpenFileInExplorer(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{path}\"",
                UseShellExecute = true
            });
        }
        catch (Exception exception)
        {
            _dialogs.ShowMessage("无法打开文件位置", exception.Message);
        }
    }

    private void OpenStartupSettings()
    {
        try
        {
            Process.Start(new ProcessStartInfo("ms-settings:startupapps") { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            ShowToast("无法打开系统设置：" + exception.Message, true);
        }
    }

    private void OpenServicesConsole()
    {
        try
        {
            Process.Start(new ProcessStartInfo("services.msc") { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            ShowToast("无法打开服务管理器：" + exception.Message, true);
        }
    }

    private async Task RunUpdateActionAsync()
    {
        switch (_updateState)
        {
            case "ready":
                await InstallUpdateAsync();
                break;
            case "available" when _selfUpdateSupported && _canDownloadUpdate:
                await DownloadUpdateAsync(automatic: false);
                break;
            case "download-failed":
                await DownloadUpdateAsync(automatic: false);
                break;
            case "available":
                OpenReleasePage();
                break;
            default:
                await CheckForUpdatesAsync(manual: true);
                break;
        }
    }

    private async Task CheckForUpdatesAsync(bool manual)
    {
        if (_updateState is "checking" or "downloading")
        {
            return;
        }

        _updateCancellation?.Cancel();
        _updateCancellation?.Dispose();
        _updateCancellation = new CancellationTokenSource();

        SetUpdateState("checking", "正在检查更新…", "正在读取仓库中的隐藏版本清单。");

        try
        {
            var result = await _updates.CheckForUpdatesAsync(_updateCancellation.Token);
            if (result.Status == UpdateCheckStatus.UpdateAvailable && result.Manifest is not null)
            {
                var manifest = result.Manifest;
                _availableUpdate = manifest;
                _downloadedUpdatePath = null;
                _isUpdateReady = false;
                _updateAvailable = true;
                _canDownloadUpdate = UpdateService.CanDownload(manifest);
                _updateReleaseNotes = FormatReleaseNotes(manifest.ReleaseNotes);
                _updateProgress = 0;
                _isUpdateProgressVisible = false;

                var detail = _canDownloadUpdate && _selfUpdateSupported
                    ? $"当前 v{AppVersion}，新版本 v{manifest.Version} 已准备好。"
                    : $"{result.Message} {_selfUpdateReason}".Trim();

                SetUpdateState("available", $"发现新版本 v{manifest.Version}", detail);
                NotifyUpdateAvailability();
                ShowToast($"发现新版本 v{manifest.Version}");
                UpdateService.Log($"Update available: {manifest.Version}.");

                if (!manual && _settings.AutoUpdate && _canDownloadUpdate && _selfUpdateSupported)
                {
                    await DownloadUpdateAsync(automatic: true);
                }
            }
            else if (result.Status == UpdateCheckStatus.UpToDate)
            {
                _availableUpdate = null;
                _downloadedUpdatePath = null;
                _isUpdateReady = false;
                _updateAvailable = false;
                _canDownloadUpdate = false;
                _updateReleaseNotes = string.Empty;
                _updateProgress = 0;
                _isUpdateProgressVisible = false;

                SetUpdateState(
                    "up-to-date",
                    "已是最新版本",
                    $"当前 v{AppVersion}，无需更新。");
                NotifyUpdateAvailability();

                if (manual)
                {
                    ShowToast("当前已是最新版本");
                }
            }
            else
            {
                SetUpdateState("failed", "检查更新失败", result.Message);
                UpdateService.Log("Update check failed: " + result.Message);
                if (manual)
                {
                    ShowToast(result.Message, true);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // The window is closing.
        }
        catch (Exception exception)
        {
            SetUpdateState("failed", "检查更新失败", "更新服务暂时不可用，请稍后重试。");
            UpdateService.Log("Unexpected update error: " + exception);
            if (manual)
            {
                ShowToast("检查更新失败：" + exception.Message, true);
            }
        }
    }

    private async Task DownloadUpdateAsync(bool automatic)
    {
        var manifest = _availableUpdate;
        if (manifest is null || _updateState == "downloading")
        {
            return;
        }

        if (!_selfUpdateSupported || !UpdateService.CanDownload(manifest))
        {
            OpenReleasePage();
            return;
        }

        if (_updateCancellation is null)
        {
            _updateCancellation = new CancellationTokenSource();
        }

        var token = _updateCancellation.Token;
        _updateProgress = 0;
        _isUpdateProgressVisible = true;
        OnPropertyChanged(nameof(UpdateProgress));
        OnPropertyChanged(nameof(IsUpdateProgressVisible));
        SetUpdateState(
            "downloading",
            $"正在下载 v{manifest.Version}",
            automatic ? "新版本会自动下载，完成后可选择重启安装。" : "正在从 GitHub 下载更新包…");

        var progress = new Progress<double>(value =>
        {
            var rounded = Math.Clamp(value, 0, 100);
            if (Math.Abs(rounded - _updateProgress) < 0.2)
            {
                return;
            }

            _updateProgress = rounded;
            OnPropertyChanged(nameof(UpdateProgress));
            OnPropertyChanged(nameof(UpdateActionText));
        });

        try
        {
            var path = await _updates.DownloadAsync(manifest, progress, token);
            _downloadedUpdatePath = path;
            _isUpdateReady = true;
            _updateAvailable = true;
            _updateProgress = 100;
            _isUpdateProgressVisible = false;

            SetUpdateState(
                "ready",
                $"v{manifest.Version} 已准备好",
                "更新包已通过 SHA-256 校验。点击“重启并安装”即可完成更新。");
            NotifyUpdateAvailability();
            ShowToast($"v{manifest.Version} 已下载，可重启安装");
        }
        catch (OperationCanceledException)
        {
            _isUpdateProgressVisible = false;
            OnPropertyChanged(nameof(IsUpdateProgressVisible));
        }
        catch (Exception exception)
        {
            _isUpdateProgressVisible = false;
            _updateProgress = 0;
            OnPropertyChanged(nameof(UpdateProgress));
            OnPropertyChanged(nameof(IsUpdateProgressVisible));
            SetUpdateState("download-failed", "更新下载失败", exception.Message);
            UpdateService.Log("Update download failed: " + exception);
            ShowToast("更新下载失败：" + exception.Message, true);
        }
    }

    private async Task InstallUpdateAsync()
    {
        var manifest = _availableUpdate;
        var path = _downloadedUpdatePath;
        if (manifest is null || string.IsNullOrWhiteSpace(path) || !_isUpdateReady)
        {
            return;
        }

        if (!_dialogs.Confirm(
                "安装更新",
                $"将关闭当前窗口并重启到 v{manifest.Version}。\n\n更新包已经过 SHA-256 校验，是否继续？",
                "重启并安装"))
        {
            return;
        }

        try
        {
            await Task.Run(() => _updates.LaunchInstaller(path, manifest.Sha256));
            ShowToast("正在重启并安装更新");
            Application.Current.Shutdown();
        }
        catch (Exception exception)
        {
            SetUpdateState("failed", "无法安装更新", exception.Message);
            UpdateService.Log("Update install failed: " + exception);
            _dialogs.ShowMessage("无法安装更新", exception.Message);
        }
    }

    private void OpenReleasePage()
    {
        var url = _availableUpdate?.ReleasePageUrl;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase) ||
            !uri.AbsolutePath.StartsWith(
                "/HWZ-ch/HWZ_Windows_TaskManager/releases",
                StringComparison.OrdinalIgnoreCase))
        {
            url = AppInfo.RepositoryUrl + "/releases/latest";
        }

        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            ShowToast("无法打开浏览器：" + exception.Message, true);
        }
    }

    private void NotifyUpdateAvailability()
    {
        OnPropertyChanged(nameof(IsUpdateAvailable));
        OnPropertyChanged(nameof(IsUpdateIndicatorVisible));
        OnPropertyChanged(nameof(UpdateBadgeText));
        OnPropertyChanged(nameof(UpdateReleaseNotes));
        OnPropertyChanged(nameof(HasUpdateReleaseNotes));
        OnPropertyChanged(nameof(UpdateProgress));
        OnPropertyChanged(nameof(IsUpdateProgressVisible));
        OnPropertyChanged(nameof(UpdateActionText));
        _updateActionCommand.RaiseCanExecuteChanged();
    }

    private void SetUpdateState(string state, string title, string detail)
    {
        _updateState = state;
        _updateStatusTitle = title;
        _updateStatusDetail = detail;
        OnPropertyChanged(nameof(UpdateState));
        OnPropertyChanged(nameof(UpdateStatusTitle));
        OnPropertyChanged(nameof(UpdateStatusDetail));
        OnPropertyChanged(nameof(UpdateActionText));
        OnPropertyChanged(nameof(UpdateBadgeText));
        OnPropertyChanged(nameof(IsUpdateIndicatorVisible));
        _updateActionCommand.RaiseCanExecuteChanged();
    }

    private static string FormatReleaseNotes(string notes)
    {
        if (string.IsNullOrWhiteSpace(notes))
        {
            return string.Empty;
        }

        var normalized = notes.Replace("\r\n", "\n").Trim();
        return normalized.Length > 640
            ? normalized[..640].TrimEnd() + "…"
            : normalized;
    }

    private void OpenRepository()
    {
        try
        {
            Process.Start(new ProcessStartInfo(RepositoryUrl) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            ShowToast("无法打开浏览器：" + exception.Message, true);
        }
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        _settingsService.Save(_settings);

        switch (e.PropertyName)
        {
            case nameof(AppSettings.ThemeMode):
                ThemeManager.Apply(_settings.ThemeMode);
                break;
            case nameof(AppSettings.RefreshSeconds):
                _timer.Interval = TimeSpan.FromSeconds(_settings.RefreshSeconds);
                OnPropertyChanged(nameof(LiveStatusDetail));
                OnPropertyChanged(nameof(RefreshRateText));
                OnPropertyChanged(nameof(HistoryWindowText));
                break;
            case nameof(AppSettings.ShowProcessIcons):
                if (!_settings.ShowProcessIcons)
                {
                    foreach (var process in _processes)
                    {
                        process.IconSource = null;
                    }

                    foreach (var item in StartupItems)
                    {
                        item.IconSource = null;
                    }
                }
                else
                {
                    LoadVisibleProcessIcons();
                    foreach (var item in StartupItems)
                    {
                        item.IconSource = IconLoader.Load(item.ExecutablePath);
                    }
                }

                break;
            case nameof(AppSettings.AutoUpdate):
                if (_settings.AutoUpdate &&
                    _initialized &&
                    _updateState is not "ready" and not "checking" and not "downloading")
                {
                    _ = CheckForUpdatesAsync(manual: false);
                }

                break;
        }
    }

    private void ShowToast(string message, bool isError = false)
    {
        ToastMessage = message;
        IsToastError = isError;
        IsToastVisible = true;
        _toastTimer.Stop();
        _toastTimer.Start();
    }
}
