using System;
using System.Collections.Generic;

namespace Candied.Studio.Services;

public interface IUnityWindowLocator
{
    IntPtr FindChildWindow(IntPtr containerHwnd);
    IntPtr FindUnityWindowForProcessTree(int rootProcessId);
    IReadOnlySet<int> GetProcessTreeIds(int rootProcessId);
}
