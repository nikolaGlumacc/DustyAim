using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace AimmyWPF
{
    internal static class OverlayClickThrough
    {
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_LAYERED = 0x00080000;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_NOACTIVATE = 0x08000000;

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr", SetLastError = true)]
        private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr", SetLastError = true)]
        private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll", EntryPoint = "GetWindowLong", SetLastError = true)]
        private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLong", SetLastError = true)]
        private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

        private static IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex)
        {
            if (IntPtr.Size == 8)
                return GetWindowLongPtr64(hWnd, nIndex);

            return new IntPtr(GetWindowLong32(hWnd, nIndex));
        }

        private static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong)
        {
            if (IntPtr.Size == 8)
                return SetWindowLongPtr64(hWnd, nIndex, dwNewLong);

            return new IntPtr(SetWindowLong32(hWnd, nIndex, unchecked((int)dwNewLong.ToInt64())));
        }

        public static void Apply(Window window)
        {
            if (window == null) return;

            IntPtr hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;

            int exStyle = unchecked((int)GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64());
            int desired = exStyle | WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
            if (desired != exStyle)
                _ = SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(desired));
        }

        public static void Remove(Window window)
        {
            if (window == null) return;

            IntPtr hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;

            int exStyle = unchecked((int)GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64());
            int desired = exStyle & ~WS_EX_TRANSPARENT; // Remove transparent flag so it catches clicks
            if (desired != exStyle)
                _ = SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(desired));
        }
    }
}
