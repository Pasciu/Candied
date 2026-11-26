using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Candied.Studio.Native;

namespace Candied.Studio.Services;

internal sealed class Win32WindowLocator : IWindowLocator
{
    public IntPtr FindChildWindow(IntPtr containerHwnd, string? windowClassHint = null)
    {
        if (containerHwnd == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        IntPtr found = IntPtr.Zero;
        Win32Native.EnumChildWindows(containerHwnd, (childHwnd, _) =>
        {
            var className = Win32Native.GetClassNameString(childHwnd);
            if (!string.IsNullOrWhiteSpace(windowClassHint) &&
                string.Equals(className, windowClassHint, StringComparison.OrdinalIgnoreCase))
            {
                found = childHwnd;
                return false;
            }

            if (found == IntPtr.Zero && Win32Native.IsWindowVisible(childHwnd))
            {
                found = childHwnd;
            }

            return true;
        }, IntPtr.Zero);

        return found;
    }

    public IntPtr FindWindowForProcessTree(int rootProcessId, string? windowClassHint = null)
    {
        if (rootProcessId <= 0)
        {
            return IntPtr.Zero;
        }

        var processIds = GetProcessTreeIds(rootProcessId);
        IntPtr matchedByHint = IntPtr.Zero;
        IntPtr largestVisibleWindow = IntPtr.Zero;
        int largestArea = 0;

        Win32Native.EnumWindows((hwnd, _) =>
        {
            if (!Win32Native.IsWindowVisible(hwnd))
            {
                return true;
            }

            uint threadId = Win32Native.GetWindowThreadProcessId(hwnd, out uint pid);
            if (threadId == 0 || !processIds.Contains((int)pid))
            {
                return true;
            }

            var className = Win32Native.GetClassNameString(hwnd);
            if (string.Equals(className, "IME", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(className, "MSCTFIME UI", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(windowClassHint) &&
                string.Equals(className, windowClassHint, StringComparison.OrdinalIgnoreCase))
            {
                matchedByHint = hwnd;
                return false;
            }

            if (Win32Native.GetWindowRect(hwnd, out var rect))
            {
                int w = rect.Right - rect.Left;
                int h = rect.Bottom - rect.Top;
                int area = w * h;
                if (w >= 64 && h >= 64 && area > largestArea)
                {
                    largestArea = area;
                    largestVisibleWindow = hwnd;
                }
            }

            return true;
        }, IntPtr.Zero);

        if (matchedByHint != IntPtr.Zero)
        {
            return matchedByHint;
        }

        if (largestVisibleWindow != IntPtr.Zero)
        {
            return largestVisibleWindow;
        }

        if (!string.IsNullOrWhiteSpace(windowClassHint))
        {
            Win32Native.EnumWindows((hwnd, _) =>
            {
                if (!Win32Native.IsWindowVisible(hwnd))
                {
                    return true;
                }

                var className = Win32Native.GetClassNameString(hwnd);
                if (string.Equals(className, windowClassHint, StringComparison.OrdinalIgnoreCase))
                {
                    matchedByHint = hwnd;
                    return false;
                }

                return true;
            }, IntPtr.Zero);
        }

        return matchedByHint;
    }

    public IReadOnlySet<int> GetProcessTreeIds(int rootProcessId)
    {
        var result = new HashSet<int> { rootProcessId };
        var parentToChildren = new Dictionary<int, List<int>>();

        IntPtr snapshot = Win32Native.CreateToolhelp32Snapshot(Win32Native.TH32CS_SNAPPROCESS, 0);
        if (snapshot == IntPtr.Zero || snapshot == new IntPtr(-1))
        {
            return result;
        }

        try
        {
            var entry = new Win32Native.PROCESSENTRY32
            {
                dwSize = (uint)Marshal.SizeOf<Win32Native.PROCESSENTRY32>()
            };

            if (Win32Native.Process32First(snapshot, ref entry))
            {
                do
                {
                    int parentId = (int)entry.th32ParentProcessID;
                    int childId = (int)entry.th32ProcessID;

                    if (!parentToChildren.TryGetValue(parentId, out var children))
                    {
                        children = new List<int>();
                        parentToChildren[parentId] = children;
                    }

                    children.Add(childId);
                }
                while (Win32Native.Process32Next(snapshot, ref entry));
            }
        }
        finally
        {
            Win32Native.CloseHandle(snapshot);
        }

        var queue = new Queue<int>();
        queue.Enqueue(rootProcessId);

        while (queue.Count > 0)
        {
            int current = queue.Dequeue();
            if (parentToChildren.TryGetValue(current, out var children))
            {
                foreach (var child in children)
                {
                    if (result.Add(child))
                    {
                        queue.Enqueue(child);
                    }
                }
            }
        }

        return result;
    }
}
