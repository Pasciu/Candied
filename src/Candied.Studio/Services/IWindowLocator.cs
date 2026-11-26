using System;
using System.Collections.Generic;

namespace Candied.Studio.Services;

public interface IWindowLocator
{
    IntPtr FindChildWindow(IntPtr containerHwnd, string? windowClassHint = null);
    IntPtr FindWindowForProcessTree(int rootProcessId, string? windowClassHint = null);
    IReadOnlySet<int> GetProcessTreeIds(int rootProcessId);
}
