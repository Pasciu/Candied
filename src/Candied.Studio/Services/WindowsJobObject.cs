using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Candied.Studio.Native;

namespace Candied.Studio.Services;

public sealed class WindowsJobObject : IWindowsJobObject
{
    private IntPtr _jobHandle = IntPtr.Zero;
    private bool _isDisposed;

    public bool IsInitialized => _jobHandle != IntPtr.Zero;

    public bool Initialize()
    {
        if (_jobHandle != IntPtr.Zero)
        {
            return true;
        }

        _jobHandle = Win32Native.CreateJobObject(IntPtr.Zero, null);
        if (_jobHandle == IntPtr.Zero)
        {
            return false;
        }

        var info = new Win32Native.JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
        info.BasicLimitInformation.LimitFlags = Win32Native.JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;

        int length = Marshal.SizeOf<Win32Native.JOBOBJECT_EXTENDED_LIMIT_INFORMATION>();
        IntPtr infoPtr = Marshal.AllocHGlobal(length);
        try
        {
            Marshal.StructureToPtr(info, infoPtr, false);
            bool success = Win32Native.SetInformationJobObject(
                _jobHandle,
                Win32Native.JobObjectExtendedLimitInformation,
                infoPtr,
                (uint)length);

            if (!success)
            {
                Win32Native.CloseHandle(_jobHandle);
                _jobHandle = IntPtr.Zero;
                return false;
            }

            return true;
        }
        finally
        {
            Marshal.FreeHGlobal(infoPtr);
        }
    }

    public bool AssignProcess(Process process)
    {
        if (process == null || process.HasExited) return false;
        return AssignProcess(process.Handle);
    }

    public bool AssignProcess(IntPtr processHandle)
    {
        if (_jobHandle == IntPtr.Zero)
        {
            if (!Initialize())
            {
                return false;
            }
        }

        return Win32Native.AssignProcessToJobObject(_jobHandle, processHandle);
    }

    public void Dispose()
    {
        if (!_isDisposed)
        {
            if (_jobHandle != IntPtr.Zero)
            {
                Win32Native.CloseHandle(_jobHandle);
                _jobHandle = IntPtr.Zero;
            }
            _isDisposed = true;
        }
        GC.SuppressFinalize(this);
    }
}
