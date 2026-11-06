using System.Runtime.InteropServices;
using Microsoft.Win32;
using HWZ.TaskManager.Helpers;
using HWZ.TaskManager.Services;

namespace HWZ.TaskManager.Models;

public sealed class SystemFacts
{
    public required string ComputerName { get; init; }
    public required string UserName { get; init; }
    public required string OsName { get; init; }
    public required string OsVersion { get; init; }
    public required string OsDisplay { get; init; }
    public required string CpuName { get; init; }
    public required double CpuBaseMhz { get; init; }
    public required int LogicalProcessors { get; init; }
    public required ulong TotalMemoryBytes { get; init; }
    public required string Manufacturer { get; init; }
    public required string Model { get; init; }
    public required string DotNetVersion { get; init; }

    public string CpuBaseClockText => Formatters.Frequency(CpuBaseMhz);
    public string TotalMemoryText => Formatters.Bytes(TotalMemoryBytes);
    public string LogicalProcessorText => $"{LogicalProcessors} 个逻辑处理器";
    public string MachineText => string.IsNullOrWhiteSpace(Model) ? ComputerName : $"{Manufacturer} {Model}".Trim();
    public string ShortCpuName => Formatters.Shorten(CpuName, 46);

    public static SystemFacts Create()
    {
        var cpuName = ReadRegistryString(
            @"HARDWARE\DESCRIPTION\System\CentralProcessor\0",
            "ProcessorNameString") ?? Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? "未知处理器";

        var mhzValue = ReadRegistryValue(
            @"HARDWARE\DESCRIPTION\System\CentralProcessor\0",
            "~MHz");
        var mhz = mhzValue is int value ? value : 0d;
        var manufacturer = ReadRegistryString(@"HARDWARE\DESCRIPTION\System\BIOS", "SystemManufacturer") ?? string.Empty;
        var model = ReadRegistryString(@"HARDWARE\DESCRIPTION\System\BIOS", "SystemProductName") ?? string.Empty;

        var version = Environment.OSVersion.Version;
        var osName = version.Build >= 22000 ? "Windows 11" : "Windows 10";
        var osDisplay = $"{osName} · {version.Major}.{version.Minor}.{version.Build}";

        var memory = GetTotalPhysicalMemory();

        return new SystemFacts
        {
            ComputerName = Environment.MachineName,
            UserName = Environment.UserName,
            OsName = osName,
            OsVersion = version.ToString(),
            OsDisplay = osDisplay,
            CpuName = cpuName.Trim(),
            CpuBaseMhz = mhz,
            LogicalProcessors = Environment.ProcessorCount,
            TotalMemoryBytes = memory,
            Manufacturer = manufacturer.Trim(),
            Model = model.Trim(),
            DotNetVersion = RuntimeInformation.FrameworkDescription
        };
    }

    private static string? ReadRegistryString(string path, string valueName)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(path);
            return key?.GetValue(valueName)?.ToString();
        }
        catch
        {
            return null;
        }
    }

    private static object? ReadRegistryValue(string path, string valueName)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(path);
            return key?.GetValue(valueName);
        }
        catch
        {
            return null;
        }
    }

    private static ulong GetTotalPhysicalMemory()
    {
        try
        {
            return NativeMethods.GetMemoryStatus().TotalPhysical;
        }
        catch
        {
            return 0;
        }
    }
}
