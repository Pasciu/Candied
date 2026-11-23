using System;
using System.Diagnostics;

namespace Candied.Studio.Services;

public interface IWindowsJobObject : IDisposable
{
    bool IsInitialized { get; }
    bool Initialize();
    bool AssignProcess(Process process);
    bool AssignProcess(IntPtr processHandle);
}
