using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;

namespace Alethic.Node;

/// <summary>
/// How much of the memory the process may use is in use, by everything: the fraction the garbage collector itself goes
/// by to know it is under pressure.
/// </summary>
/// <remarks>
/// On .NET, the collector's own figure: the physical memory in use against what is available, which is the machine's,
/// or the container's where one is limited. On .NET Framework, which has no such figure, what the system says: on
/// Windows its memory load, and on Linux the container's use against its limit where there is one, else the memory
/// not available against the whole; on any other system, nothing. Either counts an engine's heap, which is the
/// process's memory whether or not the collector manages it, and anything else on the machine or in the container
/// besides, and on some systems the file pages cached in memory too.
///
/// Nothing here can throw: a reading that fails is no reading, which the pool takes as no brake. The Windows call is
/// bound the first time it is made, so a runtime on another system never looks for it.
/// </remarks>
static class MemoryLoad
{

    /// <summary>
    /// The fraction of the memory the process may use that is in use, from 0 to 1; nothing, where it cannot be read.
    /// </summary>
    public static double? Read()
    {
        try
        {
#if NETFRAMEWORK
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return ReadWindows();

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                return ReadLinux();

            return null;
#else
            var info = GC.GetGCMemoryInfo();
            return info.TotalAvailableMemoryBytes > 0 ? Math.Min(1, info.MemoryLoadBytes / (double)info.TotalAvailableMemoryBytes) : null;
#endif
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Linux's figure, from the files it keeps: the container's use against its limit where it has one under cgroup v2
    /// or v1, and otherwise the memory not available against the whole.
    /// </summary>
    static double? ReadLinux()
    {
        return FromLinux(
            ReadFile("/proc/meminfo"),
            ReadFile("/sys/fs/cgroup/memory.max") ?? ReadFile("/sys/fs/cgroup/memory/memory.limit_in_bytes"),
            ReadFile("/sys/fs/cgroup/memory.current") ?? ReadFile("/sys/fs/cgroup/memory/memory.usage_in_bytes"));
    }

    /// <summary>
    /// Linux's figure, from what its files say.
    /// </summary>
    /// <remarks>
    /// A cgroup limit counts only where it is a number below the machine's memory: cgroup v2 says <c>max</c> for none,
    /// and v1 a number too large to mean anything.
    /// </remarks>
    /// <param name="meminfo">The text of <c>/proc/meminfo</c>, or nothing.</param>
    /// <param name="cgroupLimit">The text of the cgroup's memory limit, or nothing.</param>
    /// <param name="cgroupUsage">The text of the cgroup's memory usage, or nothing.</param>
    internal static double? FromLinux(string? meminfo, string? cgroupLimit, string? cgroupUsage)
    {
        var total = Field(meminfo, "MemTotal:");
        var available = Field(meminfo, "MemAvailable:");

        if (Number(cgroupLimit) is { } limit && Number(cgroupUsage) is { } usage && limit > 0 && (total is null || limit < total))
            return Math.Min(1, usage / limit);

        if (total is { } whole && available is { } free && whole > 0)
            return Math.Min(1, Math.Max(0, (whole - free) / whole));

        return null;
    }

    /// <summary>
    /// A field of <c>/proc/meminfo</c>, in bytes: <c>MemTotal:       16303840 kB</c>.
    /// </summary>
    /// <param name="meminfo">The text.</param>
    /// <param name="name">The field's name, with its colon.</param>
    static double? Field(string? meminfo, string name)
    {
        if (meminfo is null)
            return null;

        foreach (var line in meminfo.Split('\n'))
        {
            if (line.StartsWith(name, StringComparison.Ordinal) == false)
                continue;

            var parts = line.Substring(name.Length).Trim().Split(' ');
            if (parts.Length > 0 && double.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
                return parts.Length > 1 && string.Equals(parts[1], "kB", StringComparison.OrdinalIgnoreCase) ? value * 1024 : value;
        }

        return null;
    }

    /// <summary>
    /// A number on its own in a file, in bytes; nothing for anything else, <c>max</c> included.
    /// </summary>
    /// <param name="text">The text.</param>
    static double? Number(string? text)
    {
        return text is not null && double.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;
    }

    /// <summary>
    /// A file's text, or nothing where there is no such file or it cannot be read.
    /// </summary>
    /// <param name="path">The file.</param>
    static string? ReadFile(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

#if NETFRAMEWORK

    /// <summary>
    /// Windows's figure: its memory load, which it keeps as a percentage.
    /// </summary>
    static double? ReadWindows()
    {
        var status = new MEMORYSTATUSEX() { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        return GlobalMemoryStatusEx(ref status) ? status.dwMemoryLoad / 100.0 : null;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

#endif

}
