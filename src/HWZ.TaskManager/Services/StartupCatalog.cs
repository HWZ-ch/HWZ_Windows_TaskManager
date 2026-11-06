using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using HWZ.TaskManager.Models;

namespace HWZ.TaskManager.Services;

public static partial class StartupCatalog
{
    [GeneratedRegex("^\\s*\"(?<path>[^\"]+)\"", RegexOptions.Compiled)]
    private static partial Regex QuotedPathRegex();

    [GeneratedRegex("^\\s*(?<path>.+?\\.exe)", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex UnquotedExeRegex();

    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static async Task<IReadOnlyList<StartupRow>> LoadAsync(CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            var items = new List<StartupRow>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            ReadRunKey(RegistryHive.CurrentUser, RegistryView.Default, "当前用户 · 注册表", items, seen);
            ReadRunKey(RegistryHive.LocalMachine, RegistryView.Registry64, "所有用户 · 注册表", items, seen);

            if (Environment.Is64BitOperatingSystem)
            {
                ReadRunKey(RegistryHive.LocalMachine, RegistryView.Registry32, "所有用户 · 注册表", items, seen);
            }

            ReadStartupFolder(
                Environment.GetFolderPath(Environment.SpecialFolder.Startup),
                "当前用户 · 启动文件夹",
                RegistryHive.CurrentUser,
                items,
                seen);
            ReadStartupFolder(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup),
                "所有用户 · 启动文件夹",
                RegistryHive.LocalMachine,
                items,
                seen);

            return (IReadOnlyList<StartupRow>)items
                .OrderBy(item => item.Source, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }, cancellationToken);
    }

    private static void ReadRunKey(
        RegistryHive hive,
        RegistryView view,
        string source,
        ICollection<StartupRow> items,
        ISet<string> seen)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var runKey = baseKey.OpenSubKey(RunKeyPath);
            if (runKey is null)
            {
                return;
            }

            var approvalPath = hive == RegistryHive.LocalMachine && view == RegistryView.Registry32
                ? @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run32"
                : @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
            var approvals = ReadApprovals(hive, view, approvalPath);

            foreach (var valueName in runKey.GetValueNames())
            {
                try
                {
                    var rawCommand = runKey.GetValue(valueName)?.ToString();
                    if (string.IsNullOrWhiteSpace(rawCommand))
                    {
                        continue;
                    }

                    var command = Environment.ExpandEnvironmentVariables(rawCommand);
                    var executable = ExtractExecutablePath(command);
                    var uniqueKey = $"{valueName}|{command}";
                    if (!seen.Add(uniqueKey))
                    {
                        continue;
                    }

                    var enabled = !approvals.TryGetValue(valueName, out var isEnabled) || isEnabled;
                    items.Add(new StartupRow(
                        string.IsNullOrWhiteSpace(valueName) ? executable : valueName,
                        command,
                        ReadPublisher(executable),
                        source,
                        executable,
                        enabled));
                }
                catch
                {
                    // An unreadable Run value should not hide other entries.
                }
            }
        }
        catch
        {
            // The Run key may not exist or may not be readable.
        }
    }

    private static void ReadStartupFolder(
        string folder,
        string source,
        RegistryHive hive,
        ICollection<StartupRow> items,
        ISet<string> seen)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            return;
        }

        try
        {
            var approvals = ReadApprovals(
                hive,
                hive == RegistryHive.LocalMachine ? RegistryView.Registry64 : RegistryView.Default,
                @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder");

            foreach (var file in Directory.EnumerateFiles(folder))
            {
                try
                {
                    var attributes = File.GetAttributes(file);
                    if (attributes.HasFlag(FileAttributes.Hidden) ||
                        attributes.HasFlag(FileAttributes.System) ||
                        string.Equals(Path.GetFileName(file), "desktop.ini", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var name = Path.GetFileNameWithoutExtension(file);
                    if (!seen.Add(file))
                    {
                        continue;
                    }

                    var enabled = !approvals.TryGetValue(Path.GetFileName(file), out var isEnabled) || isEnabled;
                    items.Add(new StartupRow(
                        name,
                        file,
                        GetPublisherForShortcut(file),
                        source,
                        file,
                        enabled));
                }
                catch
                {
                    // Skip files that disappear while enumerating.
                }
            }
        }
        catch
        {
            // Startup folders can be unreadable for standard users.
        }
    }

    private static Dictionary<string, bool> ReadApprovals(RegistryHive hive, RegistryView view, string path)
    {
        var approvals = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var approvalKey = baseKey.OpenSubKey(path);
            if (approvalKey is null)
            {
                return approvals;
            }

            foreach (var valueName in approvalKey.GetValueNames())
            {
                try
                {
                    if (approvalKey.GetValue(valueName) is byte[] bytes && bytes.Length > 0)
                    {
                        approvals[valueName] = (bytes[0] & 1) == 0;
                    }
                }
                catch
                {
                    // A single malformed approval value should not stop the scan.
                }
            }
        }
        catch
        {
            // Approval state is optional; missing state means enabled.
        }

        return approvals;
    }

    private static string ExtractExecutablePath(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return string.Empty;
        }

        try
        {
            var quotedMatch = QuotedPathRegex().Match(command);
            var candidate = quotedMatch.Success
                ? quotedMatch.Groups["path"].Value
                : UnquotedExeRegex().Match(command) is { Success: true } unquoted
                    ? unquoted.Groups["path"].Value.Trim()
                    : command.Trim();

            if (string.IsNullOrWhiteSpace(candidate))
            {
                return string.Empty;
            }

            if (File.Exists(candidate))
            {
                return candidate;
            }

            var expanded = Environment.ExpandEnvironmentVariables(candidate);
            return File.Exists(expanded) ? expanded : candidate;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string ReadPublisher(string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
        {
            return string.Empty;
        }

        try
        {
            var info = FileVersionInfo.GetVersionInfo(executablePath);
            return info.CompanyName?.Trim() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string GetPublisherForShortcut(string path)
    {
        try
        {
            var info = FileVersionInfo.GetVersionInfo(path);
            return info.CompanyName?.Trim() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }
}
