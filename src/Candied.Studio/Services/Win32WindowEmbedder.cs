using System;
using Candied.Studio.Native;

namespace Candied.Studio.Services;

public sealed class Win32WindowEmbedder : IWindowEmbedder
{
    public int CalculateChildWindowStyle(int currentStyle)
    {
        // Strip top-level and window frame styles
        currentStyle &= ~Win32Native.WS_POPUP;
        currentStyle &= ~Win32Native.WS_CAPTION;
        currentStyle &= ~Win32Native.WS_THICKFRAME;
        currentStyle &= ~Win32Native.WS_MINIMIZEBOX;
        currentStyle &= ~Win32Native.WS_MAXIMIZEBOX;

        // Apply child and visibility styles
        currentStyle |= Win32Native.WS_CHILD;
        currentStyle |= Win32Native.WS_VISIBLE;
        currentStyle |= Win32Native.WS_CLIPCHILDREN;

        return currentStyle;
    }

    public bool EmbedWindow(IntPtr childHwnd, IntPtr parentHwnd, int width, int height)
    {
        if (childHwnd == IntPtr.Zero || parentHwnd == IntPtr.Zero) return false;

        // 1. Calculate and apply child styles
        IntPtr currentLong = Win32Native.GetWindowLongPtr(childHwnd, Win32Native.GWL_STYLE);
        int currentStyle = currentLong.ToInt32();
        int newStyle = CalculateChildWindowStyle(currentStyle);
        Win32Native.SetWindowLongPtr(childHwnd, Win32Native.GWL_STYLE, new IntPtr(newStyle));

        // 2. Set new parent
        IntPtr previousParent = Win32Native.SetParent(childHwnd, parentHwnd);

        // 3. Size and frame change
        bool positioned = Win32Native.SetWindowPos(
            childHwnd,
            IntPtr.Zero,
            0,
            0,
            Math.Max(1, width),
            Math.Max(1, height),
            Win32Native.SWP_NOZORDER | Win32Native.SWP_FRAMECHANGED | Win32Native.SWP_SHOWWINDOW);

        return positioned;
    }

    public bool ResizeEmbeddedWindow(IntPtr childHwnd, int width, int height)
    {
        return ResizeEmbeddedWindow(childHwnd, 0, 0, width, height);
    }

    public bool ResizeEmbeddedWindow(IntPtr childHwnd, int x, int y, int width, int height)
    {
        if (childHwnd == IntPtr.Zero) return false;

        return Win32Native.SetWindowPos(
            childHwnd,
            IntPtr.Zero,
            x,
            y,
            Math.Max(1, width),
            Math.Max(1, height),
            Win32Native.SWP_NOZORDER | Win32Native.SWP_NOACTIVATE | Win32Native.SWP_SHOWWINDOW);
    }
}
