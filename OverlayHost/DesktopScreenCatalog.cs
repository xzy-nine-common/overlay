namespace OverlayHost;

using NotifyRelay.Services.Overlay;

/// <summary>显示器枚举：经 Win32 EnumDisplayMonitors 取设备名与主屏标记。</summary>
internal sealed class DesktopScreenCatalog : IScreenCatalog
{
    public IReadOnlyList<ScreenDescriptor> GetScreens()
    {
        var list = new List<ScreenDescriptor>();
        Win32.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMonitor, IntPtr hdc, ref Win32.RECT rect, IntPtr data) =>
        {
            var info = new Win32.MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Win32.MONITORINFO>() };
            if (Win32.GetMonitorInfo(hMonitor, ref info))
            {
                var name = Win32.GetMonitorDeviceName(hMonitor);
                list.Add(new ScreenDescriptor(name, (info.dwFlags & Win32.MONITORINFOF_PRIMARY) != 0));
            }
            return true;
        }, IntPtr.Zero);
        return list;
    }
}
