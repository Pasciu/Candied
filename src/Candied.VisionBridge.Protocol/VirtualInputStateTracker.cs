using System;

namespace Candied.VisionBridge.Protocol;

/// <summary>
/// Pure, zero-dependency state tracker for the isolated in-process virtual mouse and keyboard.
/// Tracks pinned screen coordinates, active button bitmasks, and frame-accurate button down/up edges
/// without requiring the Unity runtime.
/// </summary>
public sealed class VirtualInputStateTracker
{
    public const string DefaultVirtualMouseName = "CandiedVirtualMouse";
    public const string DefaultVirtualKeyboardName = "CandiedVirtualKeyboard";

    private readonly int[] _buttonDownTargetFrame = new int[3] { -1, -1, -1 };
    private readonly int[] _buttonUpTargetFrame = new int[3] { -1, -1, -1 };

    public bool IsIsolated { get; private set; }
    public float ScreenX { get; private set; }
    public float ScreenY { get; private set; }
    public ushort ButtonsMask { get; private set; }
    public ushort ClickCount { get; private set; }

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

    public bool IsButtonHeld(int button)
    {
        if (button < 0 || button > 2) return false;
        return IsIsolated && (ButtonsMask & (1 << button)) != 0;
    }

    public bool IsButtonDownOnFrame(int button, int frame)
    {
        if (!IsIsolated || button < 0 || button > 2) return false;
        int target = _buttonDownTargetFrame[button];
        return target >= 0 && (frame == target || frame == target - 1);
    }

    public bool IsButtonUpOnFrame(int button, int frame)
    {
        if (!IsIsolated || button < 0 || button > 2) return false;
        int target = _buttonUpTargetFrame[button];
        return target >= 0 && (frame == target || frame == target - 1);
    }

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
        for (int i = 0; i < 3; i++)
        {
            _buttonDownTargetFrame[i] = -1;
            _buttonUpTargetFrame[i] = -1;
        }
    }
}
