using System;

namespace Candied.Studio.Services;

public interface IWindowEmbedder
{
    int CalculateChildWindowStyle(int currentStyle);
    bool EmbedWindow(IntPtr childHwnd, IntPtr parentHwnd, int width, int height);
    bool ResizeEmbeddedWindow(IntPtr childHwnd, int width, int height);
    bool ResizeEmbeddedWindow(IntPtr childHwnd, int x, int y, int width, int height);
}
