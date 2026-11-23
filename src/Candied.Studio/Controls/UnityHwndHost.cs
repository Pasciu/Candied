using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Candied.Studio.Native;

namespace Candied.Studio.Controls;

public class UnityHwndHost : HwndHost
{
    private IntPtr _hwndHost = IntPtr.Zero;

    public IntPtr ContainerHwnd => _hwndHost;

    public event Action<int, int>? HostResized;

    protected override HandleRef BuildWindowCore(HandleRef hwndParent)
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        int initialWidth = Math.Max(1, (int)(ActualWidth * dpi.DpiScaleX));
        int initialHeight = Math.Max(1, (int)(ActualHeight * dpi.DpiScaleY));

        _hwndHost = Win32Native.CreateWindowEx(
            0,
            "static",
            string.Empty,
            Win32Native.WS_CHILD | Win32Native.WS_VISIBLE | Win32Native.WS_CLIPCHILDREN | Win32Native.WS_CLIPSIBLINGS,
            0,
            0,
            initialWidth,
            initialHeight,
            hwndParent.Handle,
            IntPtr.Zero,
            IntPtr.Zero,
            IntPtr.Zero);

        return new HandleRef(this, _hwndHost);
    }

    protected override IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Win32Native.WM_ERASEBKGND)
        {
            if (wParam != IntPtr.Zero && Win32Native.GetClientRect(hwnd, out var rect))
            {
                IntPtr blackBrush = Win32Native.GetStockObject(Win32Native.BLACK_BRUSH);
                _ = Win32Native.FillRect(wParam, ref rect, blackBrush);
            }
            handled = true;
            return new IntPtr(1);
        }

        return base.WndProc(hwnd, msg, wParam, lParam, ref handled);
    }

    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        if (hwnd.Handle != IntPtr.Zero)
        {
            Win32Native.DestroyWindow(hwnd.Handle);
            _hwndHost = IntPtr.Zero;
        }
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);

        if (_hwndHost == IntPtr.Zero) return;

        var dpi = VisualTreeHelper.GetDpi(this);
        int pxWidth = (int)Math.Max(1, sizeInfo.NewSize.Width * dpi.DpiScaleX);
        int pxHeight = (int)Math.Max(1, sizeInfo.NewSize.Height * dpi.DpiScaleY);

        Win32Native.MoveWindow(_hwndHost, 0, 0, pxWidth, pxHeight, true);
        HostResized?.Invoke(pxWidth, pxHeight);
    }

    public (int pixelWidth, int pixelHeight) GetCurrentPixelSize()
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        int pxWidth = (int)Math.Max(1, ActualWidth * dpi.DpiScaleX);
        int pxHeight = (int)Math.Max(1, ActualHeight * dpi.DpiScaleY);
        return (pxWidth, pxHeight);
    }
}
