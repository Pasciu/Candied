using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Candied.Vision.Protocol;
using UnityEngine;

namespace Candied.VisionSensors.UnityMono;

public static class InputDispatcher
{
    private const uint WM_ACTIVATE = 0x0006;
    private const uint WM_SETFOCUS = 0x0007;
    private const uint WM_KEYDOWN = 0x0100;
    private const uint WM_KEYUP = 0x0101;
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

    public static IEnumerator DispatchCoroutine(
        VisionInputRequest request,
        string? methodOverride,
        Action<VisionInputResponse> onComplete)
    {
        string channel = request.Channel ?? VisionConstants.ChannelPointer;

        if (string.Equals(channel, VisionConstants.ChannelKeyboard, StringComparison.OrdinalIgnoreCase))
        {
            yield return DispatchKeyboardCoroutine(request, onComplete);
            yield break;
        }

        if (string.Equals(channel, VisionConstants.ChannelGamepad, StringComparison.OrdinalIgnoreCase))
        {
            yield return DispatchGamepadCoroutine(request, onComplete);
            yield break;
        }

        yield return DispatchPointerCoroutine(request, methodOverride, onComplete);
    }

    private static IEnumerator DispatchKeyboardCoroutine(
        VisionInputRequest request,
        Action<VisionInputResponse> onComplete)
    {
        string key = request.Key ?? string.Empty;
        if (string.IsNullOrEmpty(key))
        {
            onComplete(new VisionInputResponse
            {
                Success = false,
                Channel = VisionConstants.ChannelKeyboard,
                Action = request.Action,
                Message = "Missing 'key' for keyboard input."
            });
            yield break;
        }

        IntPtr hwnd = FindUnityHwnd();
        int vk = MapKeyToVk(key);
        int hold = Math.Max(1, request.HoldFrames);

        if (hwnd != IntPtr.Zero)
        {
            _ = SendMessage(hwnd, WM_ACTIVATE, WA_ACTIVE, IntPtr.Zero);
            _ = SendMessage(hwnd, WM_SETFOCUS, IntPtr.Zero, IntPtr.Zero);
            if (vk > 0)
            {
                _ = PostMessage(hwnd, WM_KEYDOWN, new IntPtr(vk), IntPtr.Zero);
            }
        }

        VirtualInputDeviceManager.SetKeyState(key.ToLowerInvariant(), true);
        for (int i = 0; i < hold; i++)
        {
            yield return null;
        }

        VirtualInputDeviceManager.SetKeyState(key.ToLowerInvariant(), false);
        if (hwnd != IntPtr.Zero && vk > 0)
        {
            _ = PostMessage(hwnd, WM_KEYUP, new IntPtr(vk), IntPtr.Zero);
        }

        yield return null;
        onComplete(new VisionInputResponse
        {
            Success = true,
            Channel = VisionConstants.ChannelKeyboard,
            Action = request.Action,
            Message = "Keyboard key '" + key + "' dispatched."
        });
    }

    private static IEnumerator DispatchGamepadCoroutine(
        VisionInputRequest request,
        Action<VisionInputResponse> onComplete)
    {
        if (string.Equals(request.Action, "stick", StringComparison.OrdinalIgnoreCase))
        {
            VirtualInputDeviceManager.StateTracker.UpdateGamepadStick(
                request.Stick ?? "left",
                request.StickX ?? 0f,
                request.StickY ?? 0f);
            yield return null;
            onComplete(new VisionInputResponse
            {
                Success = true,
                Channel = VisionConstants.ChannelGamepad,
                Action = "stick",
                Message = "Gamepad stick updated."
            });
            yield break;
        }

        string button = request.GamepadButton ?? "South";
        int hold = Math.Max(1, request.HoldFrames);
        VirtualInputDeviceManager.StateTracker.UpdateGamepadButtonState(button, true);

        IntPtr hwnd = FindUnityHwnd();
        int fallbackVk = MapGamepadToFallbackVk(button);
        if (hwnd != IntPtr.Zero && fallbackVk > 0)
        {
            _ = SendMessage(hwnd, WM_ACTIVATE, WA_ACTIVE, IntPtr.Zero);
            _ = PostMessage(hwnd, WM_KEYDOWN, new IntPtr(fallbackVk), IntPtr.Zero);
        }

        for (int i = 0; i < hold; i++)
        {
            yield return null;
        }

        VirtualInputDeviceManager.StateTracker.UpdateGamepadButtonState(button, false);
        if (hwnd != IntPtr.Zero && fallbackVk > 0)
        {
            _ = PostMessage(hwnd, WM_KEYUP, new IntPtr(fallbackVk), IntPtr.Zero);
        }

        yield return null;
        onComplete(new VisionInputResponse
        {
            Success = true,
            Channel = VisionConstants.ChannelGamepad,
            Action = request.Action,
            Message = "Gamepad button '" + button + "' dispatched."
        });
    }

    private static IEnumerator DispatchPointerCoroutine(
        VisionInputRequest request,
        string? methodOverride,
        Action<VisionInputResponse> onComplete)
    {
        if (!string.IsNullOrEmpty(request.TargetElementId))
        {
            var uiRes = UguiClickDispatcher.Click(request);
            yield return null;
            onComplete(uiRes);
            yield break;
        }

        float imageX = request.X ?? (Screen.width * 0.5f);
        float imageY = request.Y ?? (Screen.height * 0.5f);

        if (string.Equals(request.Action, "release_isolation", StringComparison.OrdinalIgnoreCase))
        {
            bool released = VirtualInputDeviceManager.ReleaseIsolation();
            yield return null;
            onComplete(new VisionInputResponse
            {
                Success = released,
                Channel = VisionConstants.ChannelPointer,
                Action = "release_isolation",
                HitPointX = imageX,
                HitPointY = imageY,
                Message = released ? "Virtual input isolation released." : "Failed to release isolation."
            });
            yield break;
        }

        if (string.Equals(request.Action, "isolate", StringComparison.OrdinalIgnoreCase))
        {
            float targetY = Screen.height - imageY;
            bool isolated = VirtualInputDeviceManager.SetMouseState(imageX, targetY, 0, 0);
            yield return null;
            onComplete(new VisionInputResponse
            {
                Success = isolated,
                Channel = VisionConstants.ChannelPointer,
                Action = "isolate",
                HitPointX = imageX,
                HitPointY = imageY,
                Message = isolated ? "Virtual mouse isolated." : "Legacy virtual mouse state isolated."
            });
            yield break;
        }

        string chosenMethod = methodOverride ?? request.Method ?? DetermineDefaultPointerMethod();
        if (string.Equals(chosenMethod, "legacy", StringComparison.OrdinalIgnoreCase))
        {
            int screenHeight = Screen.height;
            float screenY = screenHeight - imageY;
            VirtualInputDeviceManager.SetMouseState(imageX, screenY, 0, 0);
            yield return null;
            if (!string.Equals(request.Action, "move", StringComparison.OrdinalIgnoreCase))
            {
                VirtualInputDeviceManager.SetMouseState(imageX, screenY, (ushort)(1 << request.Button), 1);
                yield return null;
                VirtualInputDeviceManager.SetMouseState(imageX, screenY, 0, 0);
            }

            onComplete(UguiClickDispatcher.Click(request));
            yield break;
        }

        if (string.Equals(chosenMethod, "postmessage", StringComparison.OrdinalIgnoreCase))
        {
            IntPtr hwnd = FindUnityHwnd();
            if (hwnd == IntPtr.Zero)
            {
                onComplete(new VisionInputResponse
                {
                    Success = false,
                    Channel = VisionConstants.ChannelPointer,
                    Action = request.Action,
                    HitPointX = imageX,
                    HitPointY = imageY,
                    Message = "UnityWndClass HWND not found."
                });
                yield break;
            }

            int x = (int)Math.Round(imageX);
            int y = (int)Math.Round(imageY);
            IntPtr lParam = (IntPtr)((y << 16) | (x & 0xFFFF));
            _ = SendMessage(hwnd, WM_SETFOCUS, IntPtr.Zero, IntPtr.Zero);
            _ = PostMessage(hwnd, WM_MOUSEMOVE, IntPtr.Zero, lParam);

            if (!string.Equals(request.Action, "move", StringComparison.OrdinalIgnoreCase))
            {
                uint downMsg = request.Button == 1 ? WM_RBUTTONDOWN : (request.Button == 2 ? WM_MBUTTONDOWN : WM_LBUTTONDOWN);
                uint upMsg = request.Button == 1 ? WM_RBUTTONUP : (request.Button == 2 ? WM_MBUTTONUP : WM_LBUTTONUP);
                IntPtr wParam = request.Button == 1 ? MK_RBUTTON : (request.Button == 2 ? MK_MBUTTON : MK_LBUTTON);
                yield return null;
                _ = PostMessage(hwnd, downMsg, wParam, lParam);
                for (int i = 0; i < Math.Max(1, request.HoldFrames); i++) yield return null;
                _ = PostMessage(hwnd, upMsg, IntPtr.Zero, lParam);
            }

            yield return null;
            onComplete(new VisionInputResponse
            {
                Success = true,
                Channel = VisionConstants.ChannelPointer,
                Action = request.Action,
                HitPointX = imageX,
                HitPointY = imageY,
                Message = "Win32 PostMessage pointer action executed."
            });
            yield break;
        }

        // Default: InputSystem virtual mouse
        int sh = Screen.height;
        float sx = imageX;
        float sy = sh - imageY;

        IntPtr uHwnd = FindUnityHwnd();
        if (uHwnd != IntPtr.Zero)
        {
            _ = SendMessage(uHwnd, WM_ACTIVATE, WA_ACTIVE, IntPtr.Zero);
            _ = SendMessage(uHwnd, WM_SETFOCUS, IntPtr.Zero, IntPtr.Zero);
        }

        if (string.Equals(request.Action, "move", StringComparison.OrdinalIgnoreCase))
        {
            bool moved = false;
            for (int f = 0; f < 4; f++)
            {
                moved = VirtualInputDeviceManager.SetMouseState(sx, sy, 0, 0);
                yield return null;
            }

            onComplete(new VisionInputResponse
            {
                Success = moved,
                Channel = VisionConstants.ChannelPointer,
                Action = "move",
                HitPointX = imageX,
                HitPointY = imageY,
                Message = moved ? "Virtual mouse moved." : "Failed to move virtual mouse."
            });
            yield break;
        }

        bool provisioned = false;
        for (int f = 0; f < 4; f++)
        {
            provisioned = VirtualInputDeviceManager.SetMouseState(sx, sy, 0, 0);
            if (!provisioned) break;
            yield return null;
        }

        if (!provisioned)
        {
            onComplete(new VisionInputResponse
            {
                Success = false,
                Channel = VisionConstants.ChannelPointer,
                Action = request.Action,
                HitPointX = imageX,
                HitPointY = imageY,
                Message = "CandiedVirtualMouse unavailable in UnityEngine.InputSystem."
            });
            yield break;
        }

        ushort btnMask = (ushort)(1 << request.Button);
        for (int f = 0; f < Math.Max(1, request.HoldFrames); f++)
        {
            VirtualInputDeviceManager.SetMouseState(sx, sy, btnMask, 1);
            yield return null;
        }

        for (int f = 0; f < 3; f++)
        {
            VirtualInputDeviceManager.SetMouseState(sx, sy, 0, 0);
            yield return null;
        }

        onComplete(new VisionInputResponse
        {
            Success = true,
            Channel = VisionConstants.ChannelPointer,
            Action = request.Action,
            HitPointX = imageX,
            HitPointY = imageY,
            Message = "Click dispatched via isolated CandiedVirtualMouse."
        });
    }

    private static string DetermineDefaultPointerMethod()
    {
        if (!string.IsNullOrEmpty(CapabilitiesDetector.ActivePointerMethod))
        {
            return CapabilitiesDetector.ActivePointerMethod;
        }

        if (CapabilitiesDetector.HasInputSystem) return "input_system";
        if (CapabilitiesDetector.HasLegacyInput) return "legacy";
        return "postmessage";
    }

    private static int MapKeyToVk(string key)
    {
        string trimmed = key.Trim();
        if (trimmed.Length == 1)
        {
            char c = char.ToUpperInvariant(trimmed[0]);
            if ((c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9')) return c;
        }

        switch (trimmed.ToLowerInvariant())
        {
            case "enter":
            case "return": return 0x0D;
            case "escape":
            case "esc": return 0x1B;
            case "space": return 0x20;
            case "tab": return 0x09;
            case "backspace": return 0x08;
            case "uparrow":
            case "up": return 0x26;
            case "downarrow":
            case "down": return 0x28;
            case "leftarrow":
            case "left": return 0x25;
            case "rightarrow":
            case "right": return 0x27;
            default: return 0;
        }
    }

    private static int MapGamepadToFallbackVk(string button)
    {
        switch (button.Trim().ToLowerInvariant())
        {
            case "south":
            case "a":
            case "start": return 0x0D;
            case "east":
            case "b": return 0x1B;
            case "select": return 0x08;
            case "dpadup": return 0x26;
            case "dpaddown": return 0x28;
            case "dpadleft": return 0x25;
            case "dpadright": return 0x27;
            default: return 0;
        }
    }
}
