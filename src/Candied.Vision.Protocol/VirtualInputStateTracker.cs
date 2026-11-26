using System;
using System.Collections.Generic;

namespace Candied.Vision.Protocol;

/// <summary>
/// Pure, zero-dependency state tracker for isolated virtual pointer, keyboard, and gamepad devices.
/// Tracks pinned coordinates, held keys/buttons, analog sticks, and frame-accurate down/up edges.
/// </summary>
public sealed class VirtualInputStateTracker
{
    public const string DefaultVirtualMouseName = "CandiedVirtualMouse";
    public const string DefaultVirtualKeyboardName = "CandiedVirtualKeyboard";
    public const string DefaultVirtualGamepadName = "CandiedVirtualGamepad";

    private readonly int[] _buttonDownTargetFrame = new int[3] { -1, -1, -1 };
    private readonly int[] _buttonUpTargetFrame = new int[3] { -1, -1, -1 };
    private readonly Dictionary<string, int> _keyDownFrames = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _keyUpFrames = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _heldKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _heldGamepadButtons = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public bool IsIsolated { get; private set; }
    public float ScreenX { get; private set; }
    public float ScreenY { get; private set; }
    public ushort ButtonsMask { get; private set; }
    public ushort ClickCount { get; private set; }
    public float LeftStickX { get; private set; }
    public float LeftStickY { get; private set; }
    public float RightStickX { get; private set; }
    public float RightStickY { get; private set; }

    public void UpdateMouseState(float screenX, float screenY, ushort buttonsMask, ushort clickCount, int currentFrame)
    {
        for (int b = 0; b < 3; b++)
        {
            bool wasDown = (ButtonsMask & (1 << b)) != 0;
            bool isDown = (buttonsMask & (1 << b)) != 0;

            if (!wasDown && isDown)
            {
                _buttonDownTargetFrame[b] = currentFrame + 1;
            }
            else if (wasDown && !isDown)
            {
                _buttonUpTargetFrame[b] = currentFrame + 1;
            }
        }

        ScreenX = screenX;
        ScreenY = screenY;
        ButtonsMask = buttonsMask;
        ClickCount = clickCount;
        IsIsolated = true;
    }

    public void UpdateKeyState(string key, bool isDown, int currentFrame)
    {
        if (string.IsNullOrEmpty(key))
        {
            return;
        }

        string normalized = key.Trim();
        bool wasDown = _heldKeys.Contains(normalized);
        if (isDown)
        {
            _heldKeys.Add(normalized);
            if (!wasDown)
            {
                _keyDownFrames[normalized] = currentFrame + 1;
            }
        }
        else
        {
            _heldKeys.Remove(normalized);
            if (wasDown)
            {
                _keyUpFrames[normalized] = currentFrame + 1;
            }
        }
    }

    public void UpdateGamepadButtonState(string button, bool isDown)
    {
        if (string.IsNullOrEmpty(button))
        {
            return;
        }

        string normalized = button.Trim();
        if (isDown)
        {
            _heldGamepadButtons.Add(normalized);
        }
        else
        {
            _heldGamepadButtons.Remove(normalized);
        }
    }

    public void UpdateGamepadStick(string stick, float x, float y)
    {
        float cx = Math.Max(-1f, Math.Min(1f, x));
        float cy = Math.Max(-1f, Math.Min(1f, y));
        if (string.Equals(stick, "right", StringComparison.OrdinalIgnoreCase))
        {
            RightStickX = cx;
            RightStickY = cy;
        }
        else
        {
            LeftStickX = cx;
            LeftStickY = cy;
        }
    }

    public bool IsButtonHeld(int button)
    {
        if (button < 0 || button > 2)
        {
            return false;
        }

        return IsIsolated && (ButtonsMask & (1 << button)) != 0;
    }

    public bool IsButtonDownOnFrame(int button, int frame)
    {
        if (!IsIsolated || button < 0 || button > 2)
        {
            return false;
        }

        int target = _buttonDownTargetFrame[button];
        return target >= 0 && (frame == target || frame == target - 1);
    }

    public bool IsButtonUpOnFrame(int button, int frame)
    {
        if (!IsIsolated || button < 0 || button > 2)
        {
            return false;
        }

        int target = _buttonUpTargetFrame[button];
        return target >= 0 && (frame == target || frame == target - 1);
    }

    public bool IsKeyHeld(string key) => !string.IsNullOrEmpty(key) && _heldKeys.Contains(key.Trim());

    public bool IsKeyDownOnFrame(string key, int frame)
    {
        if (string.IsNullOrEmpty(key) || !_keyDownFrames.TryGetValue(key.Trim(), out int target))
        {
            return false;
        }

        return target >= 0 && (frame == target || frame == target - 1);
    }

    public bool IsKeyUpOnFrame(string key, int frame)
    {
        if (string.IsNullOrEmpty(key) || !_keyUpFrames.TryGetValue(key.Trim(), out int target))
        {
            return false;
        }

        return target >= 0 && (frame == target || frame == target - 1);
    }

    public bool IsGamepadButtonHeld(string button) => !string.IsNullOrEmpty(button) && _heldGamepadButtons.Contains(button.Trim());

    public void SetIsolated(bool isolated)
    {
        if (!isolated)
        {
            Reset();
        }
        else
        {
            IsIsolated = true;
        }
    }

    public void Reset()
    {
        IsIsolated = false;
        ScreenX = 0f;
        ScreenY = 0f;
        ButtonsMask = 0;
        ClickCount = 0;
        LeftStickX = 0f;
        LeftStickY = 0f;
        RightStickX = 0f;
        RightStickY = 0f;
        _heldKeys.Clear();
        _keyDownFrames.Clear();
        _keyUpFrames.Clear();
        _heldGamepadButtons.Clear();
        for (int i = 0; i < 3; i++)
        {
            _buttonDownTargetFrame[i] = -1;
            _buttonUpTargetFrame[i] = -1;
        }
    }
}
