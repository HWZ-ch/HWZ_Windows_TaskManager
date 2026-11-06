using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using HWZ.TaskManager.Models;

namespace HWZ.TaskManager.Services;

public static partial class ServiceCatalog
{
    [GeneratedRegex(@"^SERVICE_NAME:\s*(?<name>.+)$", RegexOptions.Compiled)]
    private static partial Regex ServiceNameRegex();

    [GeneratedRegex(@"^\s*STATE\s*:\s*\d+\s+(?<state>[A-Z_]+)\s*$", RegexOptions.Compiled)]
    private static partial Regex StateRegex();

    public static async Task<IReadOnlyList<ServiceRow>> LoadAsync(CancellationToken cancellationToken = default)
    {
        var serviceInfo = await Task.Run(ReadRegistryServices, cancellationToken);
        Dictionary<string, string> states;
        try
        {
            states = await QueryStatesAsync(cancellationToken);
        }
        catch
        {
            states = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        foreach (var row in serviceInfo)
        {
            if (states.TryGetValue(row.Name, out var state))
            {
                row.SetStatus(state);
            }
        }

        var unresolved = serviceInfo
            .Where(service => service.StatusCode == "Unknown")
            .ToList();

        if (unresolved.Count > 0)
        {
            using var gate = new SemaphoreSlim(6);
            var tasks = unresolved.Select(async service =>
            {
                await gate.WaitAsync(cancellationToken);
                try
                {
                    var output = await RunScAsync($"query \"{service.Name}\"", cancellationToken);
                    var state = ParseSingleState(output);
                    if (state is not null)
                    {
                        service.SetStatus(state);
                    }
                }
                catch
                {
                    // A per-user service may not be queryable while inactive.
                }
                finally
                {
                    gate.Release();
                }
            });

            await Task.WhenAll(tasks);
        }

        return serviceInfo
            .OrderByDescending(service => service.IsRunning)
            .ThenBy(service => service.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static string? ParseSingleState(string output)
    {
        foreach (var rawLine in output.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            var stateMatch = StateRegex().Match(line);
            if (stateMatch.Success)
            {
                return NormalizeState(stateMatch.Groups["state"].Value);
            }
        }

        return null;
    }

    public static async Task<(bool Success, string Message)> StartAsync(string serviceName)
        => await ChangeServiceStateAsync(serviceName, "start");

    public static async Task<(bool Success, string Message)> StopAsync(string serviceName)
        => await ChangeServiceStateAsync(serviceName, "stop");

    private static List<ServiceRow> ReadRegistryServices()
    {
        var services = new List<ServiceRow>();

        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services");
            if (key is null)
            {
                return services;
            }

            foreach (var subKeyName in key.GetSubKeyNames())
            {
                try
                {
                    using var serviceKey = key.OpenSubKey(subKeyName);
                    if (serviceKey is null)
                    {
                        continue;
                    }

                    var typeValue = serviceKey.GetValue("Type");
                    var type = typeValue is int intType ? intType : 0;
                    if ((type & 0x30) == 0)
                    {
                        continue;
                    }

                    var startValue = serviceKey.GetValue("Start");
                    var start = startValue is int intStart ? intStart : 4;
                    var delayed = serviceKey.GetValue("DelayedAutostart") is int delayedValue && delayedValue == 1;
                    var rawDisplayName = serviceKey.GetValue("DisplayName")?.ToString();
                    var rawDescription = serviceKey.GetValue("Description")?.ToString();
                    var displayName = NativeMethods.ResolveIndirectString(rawDisplayName);
                    var description = NativeMethods.ResolveIndirectString(rawDescription);
                    var imagePath = serviceKey.GetValue("ImagePath")?.ToString() ?? string.Empty;

                    if (string.IsNullOrWhiteSpace(displayName) ||
                        displayName.StartsWith('@') ||
                        displayName.Length > 160)
                    {
                        displayName = subKeyName;
                    }

                    if (description.StartsWith('@') || description.Length > 600)
                    {
                        description = string.Empty;
                    }

                    services.Add(new ServiceRow(
                        subKeyName,
                        displayName,
                        description,
                        GetStartupType(start, delayed),
                        "Unknown",
                        imagePath));
                }
                catch
                {
                    // Protected or removed service entries can be skipped.
                }
            }
        }
        catch
        {
            // Service enumeration is best-effort.
        }

        return services;
    }

    private static async Task<Dictionary<string, string>> QueryStatesAsync(CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var output = await RunScAsync("query type= service state= all", cancellationToken);
        if (string.IsNullOrWhiteSpace(output))
        {
            return result;
        }

        string? current = null;
        foreach (var rawLine in output.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            var nameMatch = ServiceNameRegex().Match(line);
            if (nameMatch.Success)
            {
                current = nameMatch.Groups["name"].Value.Trim();
                continue;
            }

            if (current is null)
            {
                continue;
            }

            var stateMatch = StateRegex().Match(line);
            if (stateMatch.Success)
            {
                result[current] = NormalizeState(stateMatch.Groups["state"].Value);
                current = null;
            }
        }

        return result;
    }

    private static async Task<(bool Success, string Message)> ChangeServiceStateAsync(string serviceName, string action)
    {
        try
        {
            var output = await RunScAsync($"{action} \"{serviceName}\"", CancellationToken.None);
            var success = output.Contains("RUNNING", StringComparison.OrdinalIgnoreCase) ||
                          output.Contains("STOPPED", StringComparison.OrdinalIgnoreCase) ||
                          output.Contains("START_PENDING", StringComparison.OrdinalIgnoreCase) ||
                          output.Contains("STOP_PENDING", StringComparison.OrdinalIgnoreCase) ||
                          output.Contains("成功", StringComparison.OrdinalIgnoreCase);
            var message = CleanScMessage(output);
            return (success, string.IsNullOrWhiteSpace(message) ? (success ? "操作已提交。" : "操作未完成。") : message);
        }
        catch (Exception exception)
        {
            return (false, exception.Message);
        }
    }

    private static async Task<string> RunScAsync(string arguments, CancellationToken cancellationToken)
    {
        var systemDirectory = Environment.SystemDirectory;
        var scPath = Path.Combine(systemDirectory, "sc.exe");
        if (!File.Exists(scPath))
        {
            scPath = "sc.exe";
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = scPath,
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        using var process = Process.Start(startInfo);
        if (process is null)
        {
            return string.Empty;
        }

        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            await process.WaitForExitAsync(linked.Token);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // The process may already have exited.
            }

            throw new TimeoutException("服务管理器响应超时。");
        }

        var standardOutput = await outputTask;
        var standardError = await errorTask;
        return string.Join(Environment.NewLine, standardOutput, standardError);
    }

    private static string CleanScMessage(string output)
    {
        var lines = output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToList();

        if (lines.Count == 0)
        {
            return string.Empty;
        }

        var summary = lines.FirstOrDefault(line =>
            line.Contains("失败", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("拒绝", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("FAILED", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("DENIED", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("错误", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("ERROR", StringComparison.OrdinalIgnoreCase));

        return summary ?? lines[^1];
    }

    private static string NormalizeState(string state) => state.ToUpperInvariant() switch
    {
        "RUNNING" => "Running",
        "STOPPED" => "Stopped",
        "START_PENDING" => "StartPending",
        "STOP_PENDING" => "StopPending",
        "PAUSED" => "Paused",
        "PAUSE_PENDING" => "PausePending",
        "CONTINUE_PENDING" => "ContinuePending",
        _ => "Unknown"
    };

    private static string GetStartupType(int start, bool delayed) => start switch
    {
        0 => "引导启动",
        1 => "系统启动",
        2 => delayed ? "自动（延迟）" : "自动",
        3 => "手动",
        4 => "已禁用",
        _ => "未知"
    };
}
