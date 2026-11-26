using System;
using System.Collections.Generic;

namespace Candied.Vision.Controls;

public enum InputChannel
{
    Mouse,
    Keyboard,
    Gamepad
}

public sealed record InputBinding(
    InputChannel Channel,
    string? Key = null,
    string? GamepadButton = null,
    int MouseButton = 0,
    float? NormalizedX = null,
    float? NormalizedY = null,
    int HoldFrames = 6,
    string? Description = null
);

public sealed record ControlActionEntry(
    string ActionName,
    string DisplayName,
    string Source,
    IReadOnlyList<InputBinding> Bindings
);

public sealed record GameControlProfile(
    string TargetId,
    string TargetName,
    DateTimeOffset UpdatedAtUtc,
    IReadOnlyList<ControlActionEntry> Actions
);
