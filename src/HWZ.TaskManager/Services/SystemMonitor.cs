using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using HWZ.TaskManager.Models;

namespace HWZ.TaskManager.Services;

public sealed class SystemMonitor : IDisposable
{
    private static readonly HashSet<string> ProtectedProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "system",
        "registry",
        "memory compression",
        "secure system",
        "smss",
        "csrss",
        "wininit",
        "winlogon",
        "services",
        "lsass"
    };

    private readonly Dictionary<int, ProcessHistory> _processHistory = new();
    private readonly Dictionary<string, NetworkHistory> _networkHistory = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, FileMetadata> _metadataCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly PdhDiskReader _diskReader = new();

    private ulong _previousIdle;
    private ulong _previousKernel;
    private ulong _previousUser;
    private long _previousTimestamp;
    private long _previousNetworkTimestamp;
    private SystemFacts? _facts;

    public SystemSnapshot Collect()
    {
        var timestamp = Stopwatch.GetTimestamp();
        var now = DateTime.Now;
        var elapsedSeconds = _previousTimestamp == 0
            ? 0
            : (timestamp - _previousTimestamp) / (double)Stopwatch.Frequency;

        var cpuUsage = ReadCpuUsage();
        var memory = NativeMethods.GetMemoryStatus();
        var visibleWindows = NativeMethods.GetVisibleWindows();

        var (pdhRead, pdhWrite) = _diskReader.Next();
        var processes = CollectProcesses(elapsedSeconds, visibleWindows, out var fallbackRead, out var fallbackWrite);
        var (networkDown, networkUp, adapters) = CollectNetwork();
        var volumes = CollectVolumes();
        var facts = _facts ??= SystemFacts.Create();

        var usedMemory = memory.TotalPhysical > memory.AvailablePhysical
            ? memory.TotalPhysical - memory.AvailablePhysical
            : 0;
        var memoryPercent = memory.TotalPhysical > 0
            ? usedMemory * 100d / memory.TotalPhysical
            : memory.MemoryLoad;

        var commitUsed = memory.TotalPageFile > memory.AvailablePageFile
            ? memory.TotalPageFile - memory.AvailablePageFile
            : 0;

        var diskRead = _diskReader.IsAvailable ? pdhRead : fallbackRead;
        var diskWrite = _diskReader.IsAvailable ? pdhWrite : fallbackWrite;

        _previousTimestamp = timestamp;

        return new SystemSnapshot
        {
            Timestamp = now,
            CpuUsage = Math.Clamp(cpuUsage, 0, 100),
            MemoryUsagePercent = Math.Clamp(memoryPercent, 0, 100),
            TotalMemoryBytes = memory.TotalPhysical,
            AvailableMemoryBytes = memory.AvailablePhysical,
            UsedMemoryBytes = usedMemory,
            CommitUsedBytes = commitUsed,
            CommitLimitBytes = memory.TotalPageFile,
            DiskReadMbPerSecond = diskRead,
            DiskWriteMbPerSecond = diskWrite,
            NetworkDownMbps = networkDown,
            NetworkUpMbps = networkUp,
            Uptime = TimeSpan.FromMilliseconds(Environment.TickCount64),
            Processes = processes,
            Volumes = volumes,
            NetworkAdapters = adapters,
            Facts = facts
        };
    }

    private double ReadCpuUsage()
    {
        if (!NativeMethods.TryGetSystemTimes(out var idle, out var kernel, out var user))
        {
            return 0;
        }

        var usage = 0d;
        if (_previousIdle != 0 || _previousKernel != 0 || _previousUser != 0)
        {
            var idleDelta = idle >= _previousIdle ? idle - _previousIdle : 0;
            var kernelDelta = kernel >= _previousKernel ? kernel - _previousKernel : 0;
            var userDelta = user >= _previousUser ? user - _previousUser : 0;
            var total = kernelDelta + userDelta;

            if (total > 0)
            {
                var busy = total > idleDelta ? total - idleDelta : 0;
                usage = busy * 100d / total;
            }
        }

        _previousIdle = idle;
        _previousKernel = kernel;
        _previousUser = user;
        return usage;
    }

    private List<ProcessSnapshot> CollectProcesses(
        double elapsedSeconds,
        Dictionary<uint, NativeMethods.VisibleWindow> visibleWindows,
        out double fallbackReadMb,
        out double fallbackWriteMb)
    {
        var snapshots = new List<ProcessSnapshot>(256);
        var activePids = new HashSet<int>();
        var totalReadMb = 0d;
        var totalWriteMb = 0d;

        Process[] processes;
        try
        {
            processes = Process.GetProcesses();
        }
        catch
        {
            fallbackReadMb = fallbackWriteMb = 0;
            return snapshots;
        }

        foreach (var process in processes)
        {
            var pid = process.Id;
            activePids.Add(pid);

            try
            {
                var name = process.ProcessName;
                if (!_processHistory.TryGetValue(pid, out var history) ||
                    !string.Equals(history.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    history = new ProcessHistory
                    {
                        Name = name,
                        StartTime = TryReadStartTime(process)
                    };
                    _processHistory[pid] = history;
                }

                TimeSpan processorTime;
                try
                {
                    processorTime = process.TotalProcessorTime;
                }
                catch
                {
                    processorTime = TimeSpan.Zero;
                }

                var cpuPercent = 0d;
                if (elapsedSeconds > 0.05 && history.LastProcessorTime > TimeSpan.Zero)
                {
                    var delta = processorTime - history.LastProcessorTime;
                    if (delta > TimeSpan.Zero)
                    {
                        cpuPercent = delta.TotalSeconds / elapsedSeconds / Math.Max(1, Environment.ProcessorCount) * 100d;
                    }
                }

                history.LastProcessorTime = processorTime;

                long workingSet;
                long privateMemory;
                try
                {
                    workingSet = process.WorkingSet64;
                    privateMemory = process.PrivateMemorySize64;
                }
                catch
                {
                    workingSet = 0;
                    privateMemory = 0;
                }

                var handleCount = TryReadInt(() => process.HandleCount);
                var threadCount = TryReadInt(() => process.Threads.Count);
                var sessionId = TryReadInt(() => process.SessionId);
                var basePriority = TryReadInt(() => process.BasePriority);

                var handle = NativeMethods.OpenProcessForQuery(pid);
                ulong ioBytes = 0;
                var processRead = 0UL;
                var processWrite = 0UL;

                if (handle != IntPtr.Zero)
                {
                    try
                    {
                        if (NativeMethods.TryGetIoCounters(handle, out var io))
                        {
                            processRead = io.ReadTransferCount;
                            processWrite = io.WriteTransferCount;
                            ioBytes = processRead + processWrite;
                        }

                        if (string.IsNullOrWhiteSpace(history.Path))
                        {
                            history.Path = NativeMethods.TryGetProcessImagePath(handle);
                        }
                    }
                    finally
                    {
                        NativeMethods.CloseProcessHandle(handle);
                    }
                }

                if (string.IsNullOrWhiteSpace(history.Path))
                {
                    try
                    {
                        history.Path = process.MainModule?.FileName;
                    }
                    catch
                    {
                        // Access to another process image path is not always permitted.
                    }
                }

                var diskMb = 0d;
                if (elapsedSeconds > 0.05 && history.LastIoBytes > 0 && ioBytes >= history.LastIoBytes)
                {
                    diskMb = (ioBytes - history.LastIoBytes) / elapsedSeconds / 1024d / 1024d;
                    if (history.LastIoRead > 0 || history.LastIoWrite > 0)
                    {
                        var readDelta = processRead >= history.LastIoRead ? processRead - history.LastIoRead : 0;
                        var writeDelta = processWrite >= history.LastIoWrite ? processWrite - history.LastIoWrite : 0;
                        totalReadMb += readDelta / elapsedSeconds / 1024d / 1024d;
                        totalWriteMb += writeDelta / elapsedSeconds / 1024d / 1024d;
                    }
                }

                history.LastIoBytes = ioBytes;
                history.LastIoRead = processRead;
                history.LastIoWrite = processWrite;

                FileMetadata? metadata = null;
                if (!string.IsNullOrWhiteSpace(history.Path))
                {
                    if (!_metadataCache.TryGetValue(history.Path, out metadata))
                    {
                        metadata = ReadFileMetadata(history.Path);
                        _metadataCache[history.Path] = metadata;
                    }
                }

                visibleWindows.TryGetValue((uint)pid, out var window);
                var hasWindow = !string.IsNullOrWhiteSpace(window.Title);
                var isProtected = pid <= 4 || ProtectedProcessNames.Contains(name);

                snapshots.Add(new ProcessSnapshot(
                    pid,
                    name,
                    history.Path,
                    metadata?.Description,
                    metadata?.CompanyName,
                    Math.Clamp(cpuPercent, 0, 100),
                    workingSet / 1024d / 1024d,
                    privateMemory / 1024d / 1024d,
                    Math.Max(0, diskMb),
                    hasWindow,
                    hasWindow && window.IsHung,
                    hasWindow ? window.Title : string.Empty,
                    sessionId,
                    history.StartTime,
                    threadCount,
                    handleCount,
                    basePriority,
                    isProtected));
            }
            catch
            {
                // A process can disappear or deny access while it is being measured.
            }
            finally
            {
                process.Dispose();
            }
        }

        foreach (var stalePid in _processHistory.Keys.Where(pid => !activePids.Contains(pid)).ToArray())
        {
            _processHistory.Remove(stalePid);
        }

        fallbackReadMb = Math.Max(0, totalReadMb);
        fallbackWriteMb = Math.Max(0, totalWriteMb);
        return snapshots;
    }

    private (double DownMbps, double UpMbps, IReadOnlyList<NetworkAdapterInfo> Adapters) CollectNetwork()
    {
        var timestamp = Stopwatch.GetTimestamp();
        var elapsedSeconds = _previousNetworkTimestamp == 0
            ? 0
            : (timestamp - _previousNetworkTimestamp) / (double)Stopwatch.Frequency;

        double totalDown = 0;
        double totalUp = 0;
        var adapters = new List<NetworkAdapterInfo>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
            {
                try
                {
                    if (networkInterface.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel ||
                        networkInterface.OperationalStatus != OperationalStatus.Up)
                    {
                        continue;
                    }

                    var statistics = networkInterface.GetIPStatistics();
                    var received = statistics.BytesReceived;
                    var sent = statistics.BytesSent;
                    var key = networkInterface.Id;
                    seen.Add(key);

                    _networkHistory.TryGetValue(key, out var history);
                    var down = 0d;
                    var up = 0d;

                    if (history is not null && elapsedSeconds > 0.05)
                    {
                        if (received >= history.BytesReceived)
                        {
                            down = (received - history.BytesReceived) * 8d / 1_000_000d / elapsedSeconds;
                        }

                        if (sent >= history.BytesSent)
                        {
                            up = (sent - history.BytesSent) * 8d / 1_000_000d / elapsedSeconds;
                        }
                    }

                    _networkHistory[key] = new NetworkHistory(received, sent);
                    totalDown += down;
                    totalUp += up;

                    var address = GetPrimaryAddress(networkInterface);
                    adapters.Add(new NetworkAdapterInfo(
                        networkInterface.Name,
                        networkInterface.Description,
                        GetNetworkTypeText(networkInterface.NetworkInterfaceType),
                        address,
                        Math.Max(0, networkInterface.Speed),
                        down,
                        up));
                }
                catch
                {
                    // Ignore adapters that disappear or deny statistics.
                }
            }
        }
        catch
        {
            // Network information is optional.
        }

        foreach (var staleKey in _networkHistory.Keys.Where(key => !seen.Contains(key)).ToArray())
        {
            _networkHistory.Remove(staleKey);
        }

        _previousNetworkTimestamp = timestamp;

        return (totalDown, totalUp, adapters
            .OrderByDescending(adapter => adapter.DownMbps + adapter.UpMbps)
            .ThenBy(adapter => adapter.Name)
            .Take(8)
            .ToList());
    }

    private static List<VolumeInfo> CollectVolumes()
    {
        var volumes = new List<VolumeInfo>();

        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                try
                {
                    if (!drive.IsReady ||
                        drive.DriveType is not (DriveType.Fixed or DriveType.Removable))
                    {
                        continue;
                    }

                    var total = drive.TotalSize;
                    var free = drive.AvailableFreeSpace;
                    if (total <= 0)
                    {
                        continue;
                    }

                    var used = total - free;
                    volumes.Add(new VolumeInfo(
                        drive.Name.TrimEnd('\\'),
                        drive.VolumeLabel,
                        drive.DriveFormat,
                        used * 100d / total,
                        used,
                        free,
                        total));
                }
                catch
                {
                    // Removable drives can be removed during enumeration.
                }
            }
        }
        catch
        {
            // Drive information is optional.
        }

        return volumes.OrderBy(volume => volume.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string GetPrimaryAddress(NetworkInterface networkInterface)
    {
        try
        {
            var addresses = networkInterface.GetIPProperties().UnicastAddresses;
            var ipv4 = addresses.FirstOrDefault(address =>
                address.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork &&
                !IPAddress.IsLoopback(address.Address));
            return ipv4?.Address.ToString()
                ?? addresses.FirstOrDefault()?.Address.ToString()
                ?? "未分配地址";
        }
        catch
        {
            return "未分配地址";
        }
    }

    private static string GetNetworkTypeText(NetworkInterfaceType type) => type switch
    {
        NetworkInterfaceType.Wireless80211 => "无线网络",
        NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet or
            NetworkInterfaceType.FastEthernetFx or NetworkInterfaceType.FastEthernetT => "以太网",
        NetworkInterfaceType.Ppp => "拨号连接",
        _ => "网络连接"
    };

    private static DateTime? TryReadStartTime(Process process)
    {
        try
        {
            return process.StartTime;
        }
        catch
        {
            return null;
        }
    }

    private static int TryReadInt(Func<int> getter)
    {
        try
        {
            return getter();
        }
        catch
        {
            return 0;
        }
    }

    private static FileMetadata ReadFileMetadata(string path)
    {
        try
        {
            var info = FileVersionInfo.GetVersionInfo(path);
            return new FileMetadata(info.FileDescription, info.CompanyName);
        }
        catch
        {
            return new FileMetadata(null, null);
        }
    }

    public void Dispose()
    {
        _diskReader.Dispose();
    }

    private sealed class ProcessHistory
    {
        public string Name { get; set; } = string.Empty;
        public TimeSpan LastProcessorTime { get; set; }
        public ulong LastIoBytes { get; set; }
        public ulong LastIoRead { get; set; }
        public ulong LastIoWrite { get; set; }
        public string? Path { get; set; }
        public DateTime? StartTime { get; set; }
    }

    private sealed record NetworkHistory(long BytesReceived, long BytesSent);

    private sealed record FileMetadata(string? Description, string? CompanyName);
}
