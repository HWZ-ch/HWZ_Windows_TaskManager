namespace HWZ.TaskManager.Models;

public sealed record ProcessSnapshot(
    int Pid,
    string Name,
    string? Path,
    string? Description,
    string? CompanyName,
    double CpuPercent,
    double MemoryMb,
    double PrivateMemoryMb,
    double DiskMbPerSecond,
    bool HasWindow,
    bool IsHung,
    string WindowTitle,
    int SessionId,
    DateTime? StartTime,
    int ThreadCount,
    int HandleCount,
    int BasePriority,
    bool IsProtected);

public sealed class SystemSnapshot
{
    public DateTime Timestamp { get; init; }
    public double CpuUsage { get; init; }
    public double MemoryUsagePercent { get; init; }
    public ulong TotalMemoryBytes { get; init; }
    public ulong AvailableMemoryBytes { get; init; }
    public ulong UsedMemoryBytes { get; init; }
    public ulong CommitUsedBytes { get; init; }
    public ulong CommitLimitBytes { get; init; }
    public double DiskReadMbPerSecond { get; init; }
    public double DiskWriteMbPerSecond { get; init; }
    public double NetworkDownMbps { get; init; }
    public double NetworkUpMbps { get; init; }
    public TimeSpan Uptime { get; init; }
    public required IReadOnlyList<ProcessSnapshot> Processes { get; init; }
    public required IReadOnlyList<VolumeInfo> Volumes { get; init; }
    public required IReadOnlyList<NetworkAdapterInfo> NetworkAdapters { get; init; }
    public required SystemFacts Facts { get; init; }

    public double DiskThroughputMbPerSecond => DiskReadMbPerSecond + DiskWriteMbPerSecond;
    public long TotalThreads => Processes.Sum(process => (long)process.ThreadCount);
    public long TotalHandles => Processes.Sum(process => (long)process.HandleCount);
}
