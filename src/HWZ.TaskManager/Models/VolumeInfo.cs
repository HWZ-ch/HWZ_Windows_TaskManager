using HWZ.TaskManager.Helpers;

namespace HWZ.TaskManager.Models;

public sealed class VolumeInfo
{
    public VolumeInfo(string name, string label, string fileSystem, double usedPercent, long usedBytes, long freeBytes, long totalBytes)
    {
        Name = name;
        Label = label;
        FileSystem = fileSystem;
        UsedPercent = Math.Clamp(usedPercent, 0, 100);
        UsedBytes = usedBytes;
        FreeBytes = freeBytes;
        TotalBytes = totalBytes;
        AvatarBrush = AccentPalette.Create(name);
    }

    public string Name { get; }
    public string Label { get; }
    public string FileSystem { get; }
    public double UsedPercent { get; }
    public long UsedBytes { get; }
    public long FreeBytes { get; }
    public long TotalBytes { get; }
    public object AvatarBrush { get; }

    public string DisplayName => string.IsNullOrWhiteSpace(Label) ? $"本地磁盘 ({Name})" : $"{Label} ({Name})";
    public string UsedText => $"{Formatters.Bytes(UsedBytes)} / {Formatters.Bytes(TotalBytes)}";
    public string FreeText => $"{Formatters.Bytes(FreeBytes)} 可用";
    public string UsedPercentText => $"{UsedPercent:0}%";
    public string SpaceText => $"{Formatters.Bytes(FreeBytes)} 可用";
}
