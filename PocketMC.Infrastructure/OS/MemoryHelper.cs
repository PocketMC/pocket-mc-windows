using System;
using System.Runtime.InteropServices;

namespace PocketMC.Infrastructure.OS
{
    public static class MemoryHelper
    {
        [StructLayout(LayoutKind.Sequential)]
        private class MEMORYSTATUSEX
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

            public MEMORYSTATUSEX()
            {
                dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);

        public static ulong GetTotalPhysicalMemoryMb() => GetMemoryMetric(m => m.ullTotalPhys);

        public static ulong GetAvailablePhysicalMemoryMb() => GetMemoryMetric(m => m.ullAvailPhys);

        private static ulong GetMemoryMetric(Func<MEMORYSTATUSEX, ulong> selector)
        {
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    var memStatus = new MEMORYSTATUSEX();
                    if (GlobalMemoryStatusEx(memStatus))
                    {
                        return selector(memStatus) / (1024 * 1024);
                    }
                }
            }
            catch { }

            var gcMemoryInfo = GC.GetGCMemoryInfo();
            // Fallback: This is not quite "System available", but it's the best we have cross-platform
            return (ulong)gcMemoryInfo.TotalAvailableMemoryBytes / (1024 * 1024);
        }
    }
}
