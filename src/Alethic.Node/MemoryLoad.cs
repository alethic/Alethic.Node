using System;
#if NETFRAMEWORK
using System.Runtime.InteropServices;
#endif

namespace Alethic.Node;

/// <summary>
/// How much of the memory the process may use is in use, by everything: the fraction the garbage collector itself goes
/// by to know it is under pressure.
/// </summary>
/// <remarks>
/// On .NET, the collector's own figure: the physical memory in use against what is available, which is the machine's,
/// or the container's where one is limited. On .NET Framework, the machine's memory load, from Windows. Either counts
/// an engine's heap, which is the process's memory whether or not the collector manages it, and anything else on the
/// machine or in the container besides.
/// </remarks>
static class MemoryLoad
{

    /// <summary>
    /// The fraction of the memory the process may use that is in use, from 0 to 1; nothing, where it cannot be read.
    /// </summary>
    public static double? Read()
    {
#if NETFRAMEWORK
        var status = new MEMORYSTATUSEX() { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        return GlobalMemoryStatusEx(ref status) ? status.dwMemoryLoad / 100.0 : null;
#else
        var info = GC.GetGCMemoryInfo();
        return info.TotalAvailableMemoryBytes > 0 ? Math.Min(1, info.MemoryLoadBytes / (double)info.TotalAvailableMemoryBytes) : null;
#endif
    }

#if NETFRAMEWORK

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
