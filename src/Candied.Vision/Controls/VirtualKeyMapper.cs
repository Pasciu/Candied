using System;
using System.Collections.Generic;

namespace Candied.Vision.Controls;

/// <summary>
/// Pure Win32 Virtual-Key mapper enabling Level 1 BaseWindowVisionAdapter to dispatch
/// standard Keyboard keys and default Gamepad bindings to any PC game or emulator window.
/// </summary>
public static class VirtualKeyMapper
{
    private static readonly Dictionary<string, int> KeyToVk = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Backspace"] = 0x08,
        ["Tab"] = 0x09,
        ["Enter"] = 0x0D,
        ["Return"] = 0x0D,
        ["Shift"] = 0x10,
        ["LeftShift"] = 0xA0,
        ["RightShift"] = 0xA1,
        ["Control"] = 0x11,
        ["Ctrl"] = 0x11,
        ["LeftCtrl"] = 0xA2,
        ["RightCtrl"] = 0xA3,
        ["Alt"] = 0x12,
        ["Escape"] = 0x1B,
        ["Esc"] = 0x1B,
        ["Space"] = 0x20,
        ["PageUp"] = 0x21,
        ["PageDown"] = 0x22,
        ["End"] = 0x23,
        ["Home"] = 0x24,
        ["LeftArrow"] = 0x25,
        ["Left"] = 0x25,
        ["UpArrow"] = 0x26,
        ["Up"] = 0x26,
        ["RightArrow"] = 0x27,
        ["Right"] = 0x27,
        ["DownArrow"] = 0x28,
        ["Down"] = 0x28,
        ["Insert"] = 0x2D,
        ["Delete"] = 0x2E,
        ["F1"] = 0x70,
        ["F2"] = 0x71,
        ["F3"] = 0x72,
        ["F4"] = 0x73,
        ["F5"] = 0x74,
        ["F6"] = 0x75,
        ["F7"] = 0x76,
        ["F8"] = 0x77,
        ["F9"] = 0x78,
        ["F10"] = 0x79,
        ["F11"] = 0x7A,
        ["F12"] = 0x7B
    };

    private static readonly Dictionary<string, int> GamepadToFallbackVk = new(StringComparer.OrdinalIgnoreCase)
    {
        ["South"] = 0x0D,        // Enter / Cross / A
        ["A"] = 0x0D,
        ["Cross"] = 0x0D,
        ["East"] = 0x1B,         // Escape / Circle / B
        ["B"] = 0x1B,
        ["Circle"] = 0x1B,
        ["West"] = 'X',          // X / Square
        ["X"] = 'X',
        ["Square"] = 'X',
        ["North"] = 'C',         // C / Triangle / Y
        ["Y"] = 'C',
        ["Triangle"] = 'C',
        ["Start"] = 0x0D,        // Enter
        ["Select"] = 0x08,       // Backspace
        ["DPadUp"] = 0x26,       // UpArrow
        ["DPadDown"] = 0x28,     // DownArrow
        ["DPadLeft"] = 0x25,     // LeftArrow
        ["DPadRight"] = 0x27,    // RightArrow
        ["LeftShoulder"] = 'Q',
        ["RightShoulder"] = 'E',
        ["LeftTrigger"] = '1',
        ["RightTrigger"] = '3'
    };

    public static bool TryResolveKeyVk(string? keyName, out int virtualKeyCode)
    {
        virtualKeyCode = 0;
        if (string.IsNullOrWhiteSpace(keyName))
        {
            return false;
        }

        string trimmed = keyName.Trim();
        if (KeyToVk.TryGetValue(trimmed, out virtualKeyCode))
        {
            return true;
        }

        if (trimmed.Length == 1)
        {
            char upper = char.ToUpperInvariant(trimmed[0]);
            if ((upper >= 'A' && upper <= 'Z') || (upper >= '0' && upper <= '9'))
            {
                virtualKeyCode = upper;
                return true;
            }
        }

        if (trimmed.StartsWith("Digit", StringComparison.OrdinalIgnoreCase) && trimmed.Length == 6 && char.IsDigit(trimmed[5]))
        {
            virtualKeyCode = trimmed[5];
            return true;
        }

        return false;
    }

    public static bool TryResolveGamepadFallbackVk(string? gamepadButton, out int virtualKeyCode)
    {
        virtualKeyCode = 0;
        if (string.IsNullOrWhiteSpace(gamepadButton))
        {
            return false;
        }

        return GamepadToFallbackVk.TryGetValue(gamepadButton.Trim(), out virtualKeyCode);
    }
}
