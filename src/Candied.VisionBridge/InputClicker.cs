using System;
using System.Collections;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Candied.VisionBridge.Protocol;
using UnityEngine;

namespace Candied.VisionBridge;

public static class InputClicker
{
    private const uint WM_ACTIVATE = 0x0006;
    private const uint WM_SETFOCUS = 0x0007;
    private const uint WM_MOUSEMOVE = 0x0200;
    private const uint WM_LBUTTONDOWN = 0x0201;
    private const uint WM_LBUTTONUP = 0x0202;
    private const uint WM_RBUTTONDOWN = 0x0204;
    private const uint WM_RBUTTONUP = 0x0205;
    private const uint WM_MBUTTONDOWN = 0x0207;
    private const uint WM_MBUTTONUP = 0x0208;
    private static readonly IntPtr WA_ACTIVE = new IntPtr(1);
    private static readonly IntPtr MK_LBUTTON = new IntPtr(0x0001);
    private static readonly IntPtr MK_RBUTTON = new IntPtr(0x0002);
    private static readonly IntPtr MK_MBUTTON = new IntPtr(0x0010);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr hWnd);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "GetClassNameW")]
    private static extern int GetClassName(IntPtr hWnd, [Out] char[] lpClassName, int nMaxCount);

    private static IntPtr _cachedHwnd = IntPtr.Zero;

    public static IntPtr FindUnityHwnd()
    {
        if (_cachedHwnd != IntPtr.Zero && IsWindow(_cachedHwnd))
        {
            return _cachedHwnd;
        }

        uint currentPid = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
        IntPtr found = IntPtr.Zero;

        _ = EnumWindows((hwnd, _) =>
        {
            uint threadId = GetWindowThreadProcessId(hwnd, out uint pid);
            if (threadId != 0 && pid == currentPid)
            {
                var buffer = new char[256];
                int len = GetClassName(hwnd, buffer, buffer.Length);
                if (len > 0 && new string(buffer, 0, len) == "UnityWndClass")
                {
                    found = hwnd;
                    return false;
                }
            }
            return true;
        }, IntPtr.Zero);

        if (found != IntPtr.Zero)
        {
            _cachedHwnd = found;
        }

        return found;
    }

    public static IEnumerator ClickCoroutine(
        float imageX,
        float imageY,
        int button,
        string? method,
        string? action,
        int holdFrames,
        Action<VisionClickResponse> onComplete)
    {
        if (string.Equals(action, "release_isolation", StringComparison.OrdinalIgnoreCase))
        {
            bool released = VirtualInputDeviceManager.ReleaseIsolation();
            yield return null;
            onComplete(new VisionClickResponse
            {
                Success = released,
                HitPointX = imageX,
                HitPointY = imageY,
                Message = released
                    ? "Virtual input isolation released; physical mouse restored."
                    : "Failed to release virtual input isolation."
            });
            yield break;
        }

        if (string.Equals(action, "isolate", StringComparison.OrdinalIgnoreCase))
        {
            float targetX = imageX > 0 ? imageX : Screen.width * 0.5f;
            float targetY = imageY > 0 ? (Screen.height - imageY) : Screen.height * 0.5f;
            bool isolated = VirtualInputDeviceManager.SetMouseState(targetX, targetY, 0, 0);
            yield return null;
            onComplete(new VisionClickResponse
            {
                Success = isolated,
                HitPointX = imageX,
                HitPointY = imageY,
                Message = isolated
                    ? "Virtual input isolation enabled on CandiedVirtualMouse."
                    : "Virtual input state isolated (Legacy Input)."
            });
            yield break;
        }

        string chosenMethod = method ?? DetermineDefaultMethod();

        if (string.Equals(chosenMethod, "input_system", StringComparison.OrdinalIgnoreCase))
        {
            yield return ClickViaInputSystem(imageX, imageY, button, action, holdFrames, onComplete);
        }
        else if (string.Equals(chosenMethod, "legacy", StringComparison.OrdinalIgnoreCase))
        {
            yield return ClickViaLegacy(imageX, imageY, button, action, onComplete);
        }
        else if (string.Equals(chosenMethod, "postmessage", StringComparison.OrdinalIgnoreCase))
        {
            yield return ClickViaPostMessage(imageX, imageY, button, action, holdFrames, onComplete);
        }
        else
        {
            // Default fallback
            yield return ClickViaInputSystem(imageX, imageY, button, action, holdFrames, onComplete);
        }
    }

    public static string DetermineDefaultMethod()
    {
        if (!string.IsNullOrEmpty(CapabilitiesDetector.ActiveInputClickMethod))
        {
            return CapabilitiesDetector.ActiveInputClickMethod;
        }

        var caps = CapabilitiesDetector.Detect();
        if (caps.InputSystem) return "input_system";
        if (caps.LegacyInput) return "legacy";
        return "postmessage";
    }

    private static IEnumerator ClickViaInputSystem(
        float imageX,
        float imageY,
        int button,
        string? action,
        int holdFrames,
        Action<VisionClickResponse> onComplete)
    {
        int screenHeight = Screen.height;
        float screenX = imageX;
        float screenY = screenHeight - imageY;

        IntPtr hwnd = FindUnityHwnd();
        if (hwnd != IntPtr.Zero)
        {
            _ = SendMessage(hwnd, WM_ACTIVATE, WA_ACTIVE, IntPtr.Zero);
            _ = SendMessage(hwnd, WM_SETFOCUS, IntPtr.Zero, IntPtr.Zero);
        }

        // Check if action is hover/move only
        if (string.Equals(action, "move", StringComparison.OrdinalIgnoreCase))
        {
            bool moved = false;
            for (int f = 0; f < 4; f++)
            {
                moved = VirtualInputDeviceManager.SetMouseState(screenX, screenY, 0, 0);
                yield return null;
            }

            if (!moved)
            {
                onComplete(new VisionClickResponse
                {
                    Success = false,
                    HitPointX = imageX,
                    HitPointY = imageY,
                    Message = "Unable to resolve or provision isolated CandiedVirtualMouse in UnityEngine.InputSystem."
                });
                yield break;
            }

            onComplete(new VisionClickResponse
            {
                Success = true,
                HitPointX = imageX,
                HitPointY = imageY,
                Message = "Hover/Move via isolated CandiedVirtualMouse executed successfully at Unity screen (" + screenX + ", " + screenY + ")."
            });
            yield break;
        }

        // Full click sequence on isolated virtual mouse:
        // Step 1: Pre-hover at target position (buttons up) for 4 frames so game hover/collider detection settles
        bool provisioned = false;
        for (int f = 0; f < 4; f++)
        {
            provisioned = VirtualInputDeviceManager.SetMouseState(screenX, screenY, 0, 0);
            if (!provisioned) break;
            yield return null;
        }

        if (!provisioned)
        {
            onComplete(new VisionClickResponse
            {
                Success = false,
                HitPointX = imageX,
                HitPointY = imageY,
                Message = "Unable to resolve or provision isolated CandiedVirtualMouse in UnityEngine.InputSystem."
            });
            yield break;
        }

        // Step 2: Mouse button down and hold across specified frames
        ushort buttonMask = (ushort)(1 << button);
        int framesToHold = Math.Max(1, holdFrames);
        for (int f = 0; f < framesToHold; f++)
        {
            VirtualInputDeviceManager.SetMouseState(screenX, screenY, buttonMask, 1);
            yield return null;
        }

        // Step 3: Mouse button up (release) and wait 3 frames for UI transition to execute
        for (int f = 0; f < 3; f++)
        {
            VirtualInputDeviceManager.SetMouseState(screenX, screenY, 0, 0);
            yield return null;
        }

        onComplete(new VisionClickResponse
        {
            Success = true,
            HitPointX = imageX,
            HitPointY = imageY,
            Message = "Click via isolated CandiedVirtualMouse (QueueStateEvent) executed successfully at Unity screen (" + screenX + ", " + screenY + ")."
        });
    }

    private static IEnumerator ClickViaLegacy(
        float imageX,
        float imageY,
        int button,
        string? action,
        Action<VisionClickResponse> onComplete)
    {
        int screenHeight = Screen.height;
        float screenX = imageX;
        float screenY = screenHeight - imageY;

        // Also update virtual state for legacy Input.mousePosition / GetMouseButton hooks
        VirtualInputDeviceManager.SetMouseState(screenX, screenY, 0, 0);
        yield return null;
        yield return null;

        if (!string.Equals(action, "move", StringComparison.OrdinalIgnoreCase))
        {
            ushort buttonMask = (ushort)(1 << button);
            VirtualInputDeviceManager.SetMouseState(screenX, screenY, buttonMask, 1);
            yield return null;
            yield return null;
            VirtualInputDeviceManager.SetMouseState(screenX, screenY, 0, 0);
            yield return null;
        }

        var req = new VisionClickRequest
        {
            X = imageX,
            Y = imageY,
            Button = button,
            Action = action
        };

        var resp = UguiClickDispatcher.Click(req);
        yield return null;
        onComplete(resp);
    }

    private static IEnumerator ClickViaPostMessage(
        float imageX,
        float imageY,
        int button,
        string? action,
        int holdFrames,
        Action<VisionClickResponse> onComplete)
    {
        IntPtr hwnd = FindUnityHwnd();
        if (hwnd == IntPtr.Zero)
        {
            onComplete(new VisionClickResponse
            {
                Success = false,
                HitPointX = imageX,
                HitPointY = imageY,
                Message = "Unity child HWND (UnityWndClass) not found in current process."
            });
            yield break;
        }

        int x = (int)Math.Round(imageX);
        int y = (int)Math.Round(imageY);
        IntPtr lParam = (IntPtr)((y << 16) | (x & 0xFFFF));

        uint downMsg = WM_LBUTTONDOWN;
        uint upMsg = WM_LBUTTONUP;
        IntPtr wParamDown = MK_LBUTTON;

        if (button == 1)
        {
            downMsg = WM_RBUTTONDOWN;
            upMsg = WM_RBUTTONUP;
            wParamDown = MK_RBUTTON;
        }
        else if (button == 2)
        {
            downMsg = WM_MBUTTONDOWN;
            upMsg = WM_MBUTTONUP;
            wParamDown = MK_MBUTTON;
        }

        _ = SendMessage(hwnd, WM_SETFOCUS, IntPtr.Zero, IntPtr.Zero);
        _ = PostMessage(hwnd, WM_MOUSEMOVE, IntPtr.Zero, lParam);

        if (string.Equals(action, "move", StringComparison.OrdinalIgnoreCase))
        {
            yield return null;
            yield return null;

            onComplete(new VisionClickResponse
            {
                Success = true,
                HitPointX = imageX,
                HitPointY = imageY,
                Message = "Move/Hover via Win32 PostMessage executed successfully on HWND " + hwnd + " at (" + x + ", " + y + ")."
            });
            yield break;
        }

        yield return null; // wait 1 frame for position to register
        _ = PostMessage(hwnd, downMsg, wParamDown, lParam);

        int frames = Math.Max(1, holdFrames);
        for (int i = 0; i < frames; i++)
        {
            yield return null;
        }

        _ = PostMessage(hwnd, upMsg, IntPtr.Zero, lParam);

        yield return null;
        yield return null;

        onComplete(new VisionClickResponse
        {
            Success = true,
            HitPointX = imageX,
            HitPointY = imageY,
            Message = "Click via Win32 PostMessage executed successfully on HWND " + hwnd + " at (" + x + ", " + y + ")."
        });
    }
}
