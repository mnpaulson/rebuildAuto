using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using RebuildOrchestrator.Models;

namespace RebuildOrchestrator.Services
{
    public class WindowManager
    {
        #region Win32 P/Invoke Definitions

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
        private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc enumProc, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;

            public int Width => Right - Left;
            public int Height => Bottom - Top;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MONITORINFOEX
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string szDevice;
        }

        private const int SW_HIDE = 0;
        private const int SW_NORMAL = 1;
        private const int SW_SHOWMINIMIZED = 2;
        private const int SW_MAXIMIZE = 3;
        private const int SW_RESTORE = 9;

        private static readonly IntPtr HWND_TOP = IntPtr.Zero;
        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_SHOWWINDOW = 0x0040;
        private const uint SWP_NOACTIVATE = 0x0010;

        #endregion

        public List<MonitorInfoData> GetMonitors()
        {
            var list = new List<MonitorInfoData>();
            int idx = 0;

            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData) =>
            {
                var mi = new MONITORINFOEX();
                mi.cbSize = Marshal.SizeOf(typeof(MONITORINFOEX));
                if (GetMonitorInfo(hMonitor, ref mi))
                {
                    list.Add(new MonitorInfoData
                    {
                        Index = idx++,
                        DeviceName = mi.szDevice ?? $"Monitor {idx}",
                        IsPrimary = (mi.dwFlags & 1) != 0,
                        Left = mi.rcMonitor.Left,
                        Top = mi.rcMonitor.Top,
                        Width = mi.rcMonitor.Width,
                        Height = mi.rcMonitor.Height,
                        WorkAreaLeft = mi.rcWork.Left,
                        WorkAreaTop = mi.rcWork.Top,
                        WorkAreaWidth = mi.rcWork.Width,
                        WorkAreaHeight = mi.rcWork.Height
                    });
                }
                return true;
            }, IntPtr.Zero);

            if (list.Count == 0)
            {
                // Fallback default monitor
                list.Add(new MonitorInfoData
                {
                    Index = 0,
                    DeviceName = "Primary Display",
                    IsPrimary = true,
                    Left = 0,
                    Top = 0,
                    Width = 1920,
                    Height = 1080,
                    WorkAreaLeft = 0,
                    WorkAreaTop = 0,
                    WorkAreaWidth = 1920,
                    WorkAreaHeight = 1040
                });
            }

            return list;
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        public IntPtr FindWindowForProcess(int pid)
        {
            IntPtr unityHwnd = IntPtr.Zero;
            IntPtr fallbackHwnd = IntPtr.Zero;

            EnumWindows((hWnd, lParam) =>
            {
                GetWindowThreadProcessId(hWnd, out uint windowPid);
                if (windowPid == (uint)pid)
                {
                    var classSb = new StringBuilder(256);
                    GetClassName(hWnd, classSb, classSb.Capacity);
                    string className = classSb.ToString();

                    GetWindowRect(hWnd, out RECT r);

                    if (className.Equals("UnityWndClass", StringComparison.OrdinalIgnoreCase))
                    {
                        unityHwnd = hWnd;
                        return false; // Found exact Unity game window!
                    }

                    if (r.Width > 200 && r.Height > 200)
                    {
                        fallbackHwnd = hWnd;
                    }
                }
                return true;
            }, IntPtr.Zero);

            return unityHwnd != IntPtr.Zero ? unityHwnd : fallbackHwnd;
        }

        public List<IntPtr> FindAllWindowsForProcess(int pid)
        {
            var list = new List<IntPtr>();
            EnumWindows((hWnd, lParam) =>
            {
                GetWindowThreadProcessId(hWnd, out uint windowPid);
                if (windowPid == (uint)pid)
                {
                    list.Add(hWnd);
                }
                return true;
            }, IntPtr.Zero);
            return list;
        }

        public bool FocusWindow(int pid)
        {
            var hwnd = FindWindowForProcess(pid);
            if (hwnd == IntPtr.Zero) return false;

            ShowWindow(hwnd, SW_RESTORE);
            return SetForegroundWindow(hwnd);
        }

        public bool HideWindow(int pid)
        {
            var allHwnds = FindAllWindowsForProcess(pid);
            if (allHwnds.Count == 0) return false;

            foreach (var h in allHwnds)
            {
                ShowWindow(h, SW_HIDE);
            }
            return true;
        }

        public bool ShowWindowForPid(int pid)
        {
            var hwnd = FindWindowForProcess(pid);
            if (hwnd == IntPtr.Zero) return false;

            ShowWindow(hwnd, SW_RESTORE);
            return SetForegroundWindow(hwnd);
        }

        public bool IsWindowVisibleForPid(int pid)
        {
            var hwnd = FindWindowForProcess(pid);
            if (hwnd == IntPtr.Zero) return false;
            return IsWindowVisible(hwnd);
        }

        public void HideAll(IEnumerable<int> pids)
        {
            foreach (var pid in pids)
            {
                HideWindow(pid);
            }
        }

        public void ShowAll(IEnumerable<int> pids)
        {
            foreach (var pid in pids)
            {
                ShowWindowForPid(pid);
            }
        }

        public void MinimizeAll(IEnumerable<int> pids)
        {
            foreach (var pid in pids)
            {
                var hwnd = FindWindowForProcess(pid);
                if (hwnd != IntPtr.Zero)
                {
                    ShowWindow(hwnd, SW_SHOWMINIMIZED);
                }
            }
        }

        public void RestoreAll(IEnumerable<int> pids)
        {
            foreach (var pid in pids)
            {
                var hwnd = FindWindowForProcess(pid);
                if (hwnd != IntPtr.Zero)
                {
                    ShowWindow(hwnd, SW_RESTORE);
                }
            }
        }

        public bool TileWindows(List<int> processIds, string layoutType, int monitorIndex = 0)
        {
            if (processIds == null || processIds.Count == 0) return false;

            var monitors = GetMonitors();
            var monitor = (monitorIndex >= 0 && monitorIndex < monitors.Count) ? monitors[monitorIndex] : monitors[0];

            int total = processIds.Count;
            int startX = monitor.WorkAreaLeft;
            int startY = monitor.WorkAreaTop;
            int totalW = monitor.WorkAreaWidth;
            int totalH = monitor.WorkAreaHeight;

            int cols = 1;
            int rows = 1;

            switch (layoutType.ToLowerInvariant())
            {
                case "2x2":
                    cols = 2;
                    rows = total <= 2 ? 1 : 2;
                    break;

                case "3x2":
                    cols = 3;
                    rows = total <= 3 ? 1 : 2;
                    break;

                case "1x2":
                case "side-by-side":
                    cols = Math.Min(total, 2);
                    rows = 1;
                    break;

                case "left-main":
                    return LayoutLeftMain(processIds, startX, startY, totalW, totalH);

                case "stack":
                case "cascade":
                    return LayoutStack(processIds, startX, startY, totalW, totalH);

                default:
                    // Auto-calculate best grid based on aspect ratio
                    cols = total switch
                    {
                        1 => 1,
                        2 => 2,
                        3 or 4 => 2,
                        5 or 6 => 3,
                        _ => 4
                    };
                    rows = (int)Math.Ceiling((double)total / cols);
                    break;
            }

            int cellW = totalW / cols;
            int cellH = totalH / rows;

            for (int i = 0; i < total; i++)
            {
                int pid = processIds[i];
                var hwnd = FindWindowForProcess(pid);
                if (hwnd == IntPtr.Zero) continue;

                int col = i % cols;
                int row = (i / cols) % rows;

                int x = startX + (col * cellW);
                int y = startY + (row * cellH);

                ShowWindow(hwnd, SW_RESTORE);
                SetWindowPos(hwnd, HWND_TOP, x, y, cellW, cellH, SWP_SHOWWINDOW);
            }

            return true;
        }

        private bool LayoutLeftMain(List<int> processIds, int startX, int startY, int totalW, int totalH)
        {
            if (processIds.Count == 0) return false;

            // Main window gets 60% of width on the left
            int mainW = (int)(totalW * 0.60);
            int sideW = totalW - mainW;

            int mainPid = processIds[0];
            var mainHwnd = FindWindowForProcess(mainPid);
            if (mainHwnd != IntPtr.Zero)
            {
                ShowWindow(mainHwnd, SW_RESTORE);
                SetWindowPos(mainHwnd, HWND_TOP, startX, startY, mainW, totalH, SWP_SHOWWINDOW);
            }

            int supportCount = processIds.Count - 1;
            if (supportCount > 0)
            {
                int subH = totalH / supportCount;
                for (int i = 1; i < processIds.Count; i++)
                {
                    int subPid = processIds[i];
                    var subHwnd = FindWindowForProcess(subPid);
                    if (subHwnd == IntPtr.Zero) continue;

                    int subY = startY + ((i - 1) * subH);
                    ShowWindow(subHwnd, SW_RESTORE);
                    SetWindowPos(subHwnd, HWND_TOP, startX + mainW, subY, sideW, subH, SWP_SHOWWINDOW);
                }
            }

            return true;
        }

        private bool LayoutStack(List<int> processIds, int startX, int startY, int totalW, int totalH)
        {
            int defaultW = Math.Min(totalW - 200, 1280);
            int defaultH = Math.Min(totalH - 200, 720);
            int offset = 32;

            for (int i = 0; i < processIds.Count; i++)
            {
                int pid = processIds[i];
                var hwnd = FindWindowForProcess(pid);
                if (hwnd == IntPtr.Zero) continue;

                int x = startX + 40 + (i * offset);
                int y = startY + 40 + (i * offset);

                ShowWindow(hwnd, SW_RESTORE);
                SetWindowPos(hwnd, HWND_TOP, x, y, defaultW, defaultH, SWP_SHOWWINDOW);
            }

            return true;
        }
    }
}
