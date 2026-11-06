using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace HWZ.TaskManager.Services;

internal static class NativeMethods
{
    internal const int ProcessQueryLimitedInformation = 0x1000;
    internal const uint ShgfiIcon = 0x000000100;

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeFileTime
    {
        public uint Low;
        public uint High;

        public readonly ulong ToUInt64() => ((ulong)High << 32) | Low;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    internal readonly record struct VisibleWindow(string Title, bool IsHung);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct ShFileInfo
    {
        public IntPtr Icon;
        public int IconIndex;
        public uint Attributes;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string DisplayName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string TypeName;
    }

    [StructLayout(LayoutKind.Explicit)]
    internal struct PdhFormattedCounterValue
    {
        [FieldOffset(0)]
        public uint Status;

        [FieldOffset(8)]
        public double DoubleValue;
    }

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out NativeFileTime idleTime, out NativeFileTime kernelTime, out NativeFileTime userTime);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(IntPtr processHandle, uint flags, StringBuilder exeName, ref uint size);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessIoCounters(IntPtr processHandle, out IoCounters ioCounters);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc enumProc, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsHungAppWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DestroyIcon(IntPtr icon);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    internal static extern IntPtr SHGetFileInfo(
        string path,
        uint fileAttributes,
        ref ShFileInfo fileInfo,
        uint fileInfoSize,
        uint flags);

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    private static extern int SHLoadIndirectString(
        string source,
        StringBuilder output,
        uint outputLength,
        IntPtr reserved);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    internal static extern uint PdhOpenQuery(string? dataSource, IntPtr userData, out IntPtr query);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    internal static extern uint PdhAddEnglishCounter(
        IntPtr query,
        string fullCounterPath,
        IntPtr userData,
        out IntPtr counter);

    [DllImport("pdh.dll")]
    internal static extern uint PdhCollectQueryData(IntPtr query);

    [DllImport("pdh.dll")]
    internal static extern uint PdhGetFormattedCounterValue(
        IntPtr counter,
        uint format,
        out uint counterType,
        out PdhFormattedCounterValue value);

    [DllImport("pdh.dll")]
    internal static extern uint PdhCloseQuery(IntPtr query);

    internal static MemoryStatusEx GetMemoryStatus()
    {
        var status = new MemoryStatusEx
        {
            Length = (uint)Marshal.SizeOf<MemoryStatusEx>()
        };

        if (!GlobalMemoryStatusEx(ref status))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        return status;
    }

    internal static bool TryGetSystemTimes(out ulong idle, out ulong kernel, out ulong user)
    {
        idle = kernel = user = 0;
        if (!GetSystemTimes(out var idleTime, out var kernelTime, out var userTime))
        {
            return false;
        }

        idle = idleTime.ToUInt64();
        kernel = kernelTime.ToUInt64();
        user = userTime.ToUInt64();
        return true;
    }

    internal static IntPtr OpenProcessForQuery(int processId)
        => OpenProcess(ProcessQueryLimitedInformation, false, processId);

    internal static void CloseProcessHandle(IntPtr handle)
    {
        if (handle != IntPtr.Zero)
        {
            _ = CloseHandle(handle);
        }
    }

    internal static string? TryGetProcessImagePath(IntPtr processHandle)
    {
        var builder = new StringBuilder(2048);
        uint size = (uint)builder.Capacity;
        if (!QueryFullProcessImageName(processHandle, 0, builder, ref size))
        {
            return null;
        }

        return builder.ToString(0, (int)size);
    }

    internal static bool TryGetIoCounters(IntPtr processHandle, out IoCounters counters)
        => GetProcessIoCounters(processHandle, out counters);

    internal static string ResolveIndirectString(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var raw = value.Trim();
        try
        {
            raw = Environment.ExpandEnvironmentVariables(raw);
        }
        catch
        {
            // The raw string remains usable even if expansion fails.
        }

        if (!raw.StartsWith('@'))
        {
            return raw;
        }

        try
        {
            var builder = new StringBuilder(2048);
            var result = SHLoadIndirectString(raw, builder, (uint)builder.Capacity, IntPtr.Zero);
            if (result == 0 && builder.Length > 0)
            {
                var resolved = builder.ToString().Trim();
                if (!string.IsNullOrWhiteSpace(resolved) && !resolved.StartsWith('@'))
                {
                    return resolved;
                }
            }
        }
        catch
        {
            // Fall back to the original value below.
        }

        return raw;
    }

    internal static Dictionary<uint, VisibleWindow> GetVisibleWindows()
    {
        var windows = new Dictionary<uint, VisibleWindow>();

        bool Callback(IntPtr hWnd, IntPtr lParam)
        {
            try
            {
                if (!IsWindowVisible(hWnd))
                {
                    return true;
                }

                var length = GetWindowTextLength(hWnd);
                if (length <= 0)
                {
                    return true;
                }

                var buffer = new StringBuilder(length + 1);
                _ = GetWindowText(hWnd, buffer, buffer.Capacity);
                var title = buffer.ToString().Trim();
                if (title.Length == 0)
                {
                    return true;
                }

                _ = GetWindowThreadProcessId(hWnd, out var processId);
                if (processId == 0)
                {
                    return true;
                }

                var isHung = IsHungAppWindow(hWnd);
                if (!windows.TryGetValue(processId, out var existing) || (!existing.IsHung && isHung))
                {
                    windows[processId] = new VisibleWindow(title, isHung);
                }
            }
            catch
            {
                // A single disappearing top-level window must not stop enumeration.
            }

            return true;
        }

        try
        {
            _ = EnumWindows(Callback, IntPtr.Zero);
            GC.KeepAlive((EnumWindowsProc)Callback);
        }
        catch
        {
            // Window enumeration is optional metadata.
        }

        return windows;
    }
}

internal sealed class PdhDiskReader : IDisposable
{
    private const uint PdhFormatDouble = 0x00000200;
    private const uint PdhFormatNoScale = 0x00001000;

    private readonly object _sync = new();
    private IntPtr _query;
    private IntPtr _readCounter;
    private IntPtr _writeCounter;
    private bool _initialized;
    private bool _hasSample;

    public bool IsAvailable { get; private set; }

    public (double ReadMbPerSecond, double WriteMbPerSecond) Next()
    {
        lock (_sync)
        {
            if (!_initialized)
            {
                Initialize();
            }

            if (!IsAvailable)
            {
                return (0, 0);
            }

            try
            {
                if (NativeMethods.PdhCollectQueryData(_query) != 0)
                {
                    IsAvailable = false;
                    return (0, 0);
                }

                if (!_hasSample)
                {
                    _hasSample = true;
                    return (0, 0);
                }

                var read = ReadCounter(_readCounter);
                var write = ReadCounter(_writeCounter);
                return (read, write);
            }
            catch
            {
                IsAvailable = false;
                return (0, 0);
            }
        }
    }

    private void Initialize()
    {
        _initialized = true;

        try
        {
            if (NativeMethods.PdhOpenQuery(null, IntPtr.Zero, out _query) != 0)
            {
                return;
            }

            var readStatus = NativeMethods.PdhAddEnglishCounter(
                _query,
                @"\PhysicalDisk(_Total)\Disk Read Bytes/sec",
                IntPtr.Zero,
                out _readCounter);
            var writeStatus = NativeMethods.PdhAddEnglishCounter(
                _query,
                @"\PhysicalDisk(_Total)\Disk Write Bytes/sec",
                IntPtr.Zero,
                out _writeCounter);

            if (readStatus != 0 || writeStatus != 0)
            {
                return;
            }

            IsAvailable = true;
        }
        catch
        {
            IsAvailable = false;
        }
    }

    private static double ReadCounter(IntPtr counter)
    {
        var status = NativeMethods.PdhGetFormattedCounterValue(
            counter,
            PdhFormatDouble | PdhFormatNoScale,
            out _,
            out var value);

        if (status != 0 || value.Status > 1 || double.IsNaN(value.DoubleValue) || double.IsInfinity(value.DoubleValue))
        {
            return 0;
        }

        return Math.Max(0, value.DoubleValue) / 1024d / 1024d;
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_query != IntPtr.Zero)
            {
                _ = NativeMethods.PdhCloseQuery(_query);
                _query = IntPtr.Zero;
            }

            IsAvailable = false;
        }
    }
}
