namespace OverlayHost;

using System.Runtime.InteropServices;

/// <summary>显示器枚举所需的最小 Win32 声明。</summary>
internal static class Win32
{
    internal const int MONITORINFOF_PRIMARY = 0x00000001;

    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT
    {
        public int left;
        public int top;
        public int right;
        public int bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public int dwFlags;
    }

    internal delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, ref RECT rect, IntPtr data);

    [DllImport("user32.dll")]
    internal static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc proc, IntPtr data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO info);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetMonitorInfoW")]
    private static extern bool GetMonitorInfoRaw(IntPtr hMonitor, IntPtr info);

    /// <summary>取显示器设备名（MONITORINFOEX 的 szDevice）。</summary>
    internal static string GetMonitorDeviceName(IntPtr hMonitor)
    {
        // MONITORINFOEX: MONITORINFO + 32 个 wchar 的设备名
        var size = Marshal.SizeOf<MONITORINFO>() + 32 * 2;
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.WriteInt32(buffer, size);
            if (!GetMonitorInfoRaw(hMonitor, buffer)) return string.Empty;
            var offset = Marshal.SizeOf<MONITORINFO>();
            return Marshal.PtrToStringUni(buffer + offset) ?? string.Empty;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}
