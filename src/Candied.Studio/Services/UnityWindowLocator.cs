using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Candied.Studio.Native;

namespace Candied.Studio.Services;

public sealed class UnityWindowLocator : IUnityWindowLocator
{
    public const string UnityWndClass = "UnityWndClass";

    public IntPtr FindChildWindow(IntPtr containerHwnd)
    {
        if (containerHwnd == IntPtr.Zero) return IntPtr.Zero;

        IntPtr found = IntPtr.Zero;
        Win32Native.EnumChildWindows(containerHwnd, (childHwnd, _) =>
        {
            var className = Win32Native.GetClassNameString(childHwnd);

            if (string.Equals(className, UnityWndClass, StringComparison.OrdinalIgnoreCase))
            {
                found = childHwnd;
                return false; // Stop enumeration
            }

            // Fallback: any child window
            if (found == IntPtr.Zero && Win32Native.IsWindowVisible(childHwnd))
            {
                found = childHwnd;
            }

            return true;
        }, IntPtr.Zero);

        return found;
    }

    public IntPtr FindUnityWindowForProcessTree(int rootProcessId)
    {
        if (rootProcessId <= 0) return IntPtr.Zero;

        var processIds = GetProcessTreeIds(rootProcessId);
        IntPtr unityHwnd = IntPtr.Zero;

        Win32Native.EnumWindows((hwnd, _) =>
        {
            if (!Win32Native.IsWindowVisible(hwnd)) return true;

            uint threadId = Win32Native.GetWindowThreadProcessId(hwnd, out uint pid);
            if (threadId != 0 && processIds.Contains((int)pid))
            {
                var className = Win32Native.GetClassNameString(hwnd);

                if (string.Equals(className, UnityWndClass, StringComparison.OrdinalIgnoreCase))
                {
                    unityHwnd = hwnd;
                    return false; // Found Unity main window
                }
            }

            return true;
        }, IntPtr.Zero);

        // Fallback: if not found by process tree, scan for any newly visible UnityWndClass
        if (unityHwnd == IntPtr.Zero)
        {
            Win32Native.EnumWindows((hwnd, _) =>
            {
                if (!Win32Native.IsWindowVisible(hwnd)) return true;

                var className = Win32Native.GetClassNameString(hwnd);
                if (string.Equals(className, UnityWndClass, StringComparison.OrdinalIgnoreCase))
                {
                    unityHwnd = hwnd;
                    return false;
                }

                return true;
            }, IntPtr.Zero);
        }

        return unityHwnd;
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
            var entry = new Win32Native.PROCESSENTRY32();
            entry.dwSize = (uint)Marshal.SizeOf<Win32Native.PROCESSENTRY32>();

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

        // BFS to collect all descendants
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
