using System.Runtime.InteropServices;

namespace VpnRdp.Services;

/// <param name="MstscId">The ID mstsc uses in "selectedmonitors" (as listed by "mstsc /l").</param>
/// <param name="DisplayNumber">Windows display number (\\.\DISPLAYn), as shown in Settings &gt; Display.</param>
public sealed record MonitorInfo(int MstscId, int DisplayNumber, string DeviceName, int Left, int Top, int Width, int Height, bool IsPrimary);

public static class MonitorEnumerator
{
    /// <summary>
    /// mstsc's monitor IDs are the display device index, i.e. \\.\DISPLAYn → n-1 (so they can have gaps,
    /// e.g. 0,1,3 — verified against "mstsc /l" on Windows 11). They are NOT the enumeration order.
    /// </summary>
    public static List<MonitorInfo> GetMonitors()
    {
        var list = new List<MonitorInfo>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (hMon, _, _, _) =>
        {
            var mi = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
            if (GetMonitorInfo(hMon, ref mi))
            {
                var r = mi.rcMonitor;
                var display = ParseDisplayNumber(mi.szDevice) ?? list.Count + 1;
                list.Add(new MonitorInfo(display - 1, display, mi.szDevice, r.Left, r.Top,
                    r.Right - r.Left, r.Bottom - r.Top, (mi.dwFlags & 1) != 0));
            }
            return true;
        }, IntPtr.Zero);
        return list.OrderBy(m => m.Left).ThenBy(m => m.Top).ToList();
    }

    internal static int? ParseDisplayNumber(string deviceName)
    {
        var digits = new string(deviceName.Reverse().TakeWhile(char.IsDigit).Reverse().ToArray());
        return int.TryParse(digits, out var n) && n > 0 ? n : null;
    }

    private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, IntPtr lprcMonitor, IntPtr data);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc proc, IntPtr data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX info);
}
