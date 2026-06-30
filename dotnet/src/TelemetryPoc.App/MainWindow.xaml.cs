using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using TelemetryPoc.App.ViewModels;

namespace TelemetryPoc.App;

public partial class MainWindow : Window
{
    private readonly RideSession _session;

    public MainWindow(RideSession session, TopBarViewModel topBar, OverviewViewModel overview,
        TransportViewModel transport, HudViewModel hud)
    {
        InitializeComponent();
        _session = session;
        TopBar.DataContext = topBar;
        Overview.DataContext = overview;
        Transport.DataContext = transport;
        Hud.DataContext = hud;
        Loaded += (_, _) => _session.StartAsync();
        Closed += (_, _) => _session.Dispose();
        SourceInitialized += OnSourceInitialized; // keep the WM_GETMINMAXINFO hook
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        HwndSource.FromHwnd(handle)?.AddHook(WindowProc);
    }

    private static IntPtr WindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_GETMINMAXINFO = 0x0024;
        if (msg == WM_GETMINMAXINFO)
        {
            ClampToWorkArea(hwnd, lParam);
            handled = true;
        }
        return IntPtr.Zero;
    }

    private static void ClampToWorkArea(IntPtr hwnd, IntPtr lParam)
    {
        var mmi = Marshal.PtrToStructure<MINMAXINFO>(lParam);
        const int MONITOR_DEFAULTTONEAREST = 0x00000002;
        var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        if (monitor != IntPtr.Zero)
        {
            var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            GetMonitorInfo(monitor, ref info);
            var work = info.rcWork;
            var mon = info.rcMonitor;
            // Position/size are relative to the monitor's top-left.
            mmi.ptMaxPosition.X = work.Left - mon.Left;
            mmi.ptMaxPosition.Y = work.Top - mon.Top;
            mmi.ptMaxSize.X = work.Right - work.Left;
            mmi.ptMaxSize.Y = work.Bottom - work.Top;
            // Keep a sane minimum so the chrome can't collapse.
            mmi.ptMinTrackSize.X = 800;
            mmi.ptMinTrackSize.Y = 600;
            Marshal.StructureToPtr(mmi, lParam, true);
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr handle, int flags);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MINMAXINFO
    {
        public POINT ptReserved;
        public POINT ptMaxSize;
        public POINT ptMaxPosition;
        public POINT ptMinTrackSize;
        public POINT ptMaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public int dwFlags;
    }
}
