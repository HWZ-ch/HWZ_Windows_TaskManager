using System.Globalization;

namespace HWZ.TaskManager.Helpers;

public static class Formatters
{
    private static readonly string[] ByteUnits = ["B", "KB", "MB", "GB", "TB", "PB"];

    public static string Bytes(double bytes, int decimals = 1)
    {
        if (double.IsNaN(bytes) || double.IsInfinity(bytes) || bytes <= 0)
        {
            return "0 B";
        }

        var order = 0;
        while (bytes >= 1024 && order < ByteUnits.Length - 1)
        {
            bytes /= 1024;
            order++;
        }

        var format = order == 0 ? "0" : "0." + new string('#', Math.Max(1, decimals));
        return $"{bytes.ToString(format, CultureInfo.CurrentCulture)} {ByteUnits[order]}";
    }

    public static string MemorySpeed(double megabytesPerSecond)
        => megabytesPerSecond < 0.05 ? "0.0 MB/s" : $"{megabytesPerSecond:0.0} MB/s";

    public static string NetworkSpeed(double megabitsPerSecond)
        => megabitsPerSecond < 0.005 ? "0.0 Mbps" : $"{megabitsPerSecond:0.0} Mbps";

    public static string Uptime(TimeSpan uptime)
    {
        if (uptime.TotalDays >= 1)
        {
            return $"{(int)uptime.TotalDays} 天 {uptime.Hours} 小时";
        }

        if (uptime.TotalHours >= 1)
        {
            return $"{uptime.Hours} 小时 {uptime.Minutes} 分";
        }

        return $"{Math.Max(0, uptime.Minutes)} 分钟";
    }

    public static string Count(long value)
        => value >= 1000 ? value.ToString("N0", CultureInfo.CurrentCulture) : value.ToString(CultureInfo.CurrentCulture);

    public static string Frequency(double megahertz)
        => megahertz <= 0 ? "未知" : megahertz >= 1000 ? $"{megahertz / 1000:0.00} GHz" : $"{megahertz:0} MHz";

    public static string Shorten(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..Math.Max(0, maxLength - 1)] + "…";
    }
}
