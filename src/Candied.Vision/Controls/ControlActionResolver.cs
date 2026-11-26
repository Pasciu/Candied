using System;
using System.Collections.Generic;
using System.Linq;
using Candied.Primitives;
using Candied.Vision.Models;
using Candied.Vision.Protocol;

namespace Candied.Vision.Controls;

/// <summary>
/// Pure resolver translating semantic game actions (e.g. "Confirm", "Cancel", "Pause", "Inventory")
/// into concrete 3-channel VisionInputRequests using the per-target GameControlProfile and active capabilities.
/// </summary>
public static class ControlActionResolver
{
    private static readonly Dictionary<string, InputBinding[]> BuiltInFallbackActions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Confirm"] =
        [
            new InputBinding(InputChannel.Keyboard, Key: "Enter", Description: "Default Confirm (Enter)"),
            new InputBinding(InputChannel.Gamepad, GamepadButton: "South", Description: "Default Confirm (Gamepad South)")
        ],
        ["Cancel"] =
        [
            new InputBinding(InputChannel.Keyboard, Key: "Escape", Description: "Default Cancel (Escape)"),
            new InputBinding(InputChannel.Gamepad, GamepadButton: "East", Description: "Default Cancel (Gamepad East)")
        ],
        ["Pause"] =
        [
            new InputBinding(InputChannel.Keyboard, Key: "Escape", Description: "Default Pause (Escape)"),
            new InputBinding(InputChannel.Gamepad, GamepadButton: "Start", Description: "Default Pause (Gamepad Start)")
        ],
        ["MoveUp"] =
        [
            new InputBinding(InputChannel.Keyboard, Key: "UpArrow", Description: "Default MoveUp"),
            new InputBinding(InputChannel.Gamepad, GamepadButton: "DPadUp", Description: "Default DPadUp")
        ],
        ["MoveDown"] =
        [
            new InputBinding(InputChannel.Keyboard, Key: "DownArrow", Description: "Default MoveDown"),
            new InputBinding(InputChannel.Gamepad, GamepadButton: "DPadDown", Description: "Default DPadDown")
        ],
        ["MoveLeft"] =
        [
            new InputBinding(InputChannel.Keyboard, Key: "LeftArrow", Description: "Default MoveLeft"),
            new InputBinding(InputChannel.Gamepad, GamepadButton: "DPadLeft", Description: "Default DPadLeft")
        ],
        ["MoveRight"] =
        [
            new InputBinding(InputChannel.Keyboard, Key: "RightArrow", Description: "Default MoveRight"),
            new InputBinding(InputChannel.Gamepad, GamepadButton: "DPadRight", Description: "Default DPadRight")
        ]
    };

    public static Result<VisionInputRequest, VisionError> ResolveAction(
        string actionName,
        GameControlProfile? profile,
        IReadOnlyCollection<string>? supportedCapabilities = null,
        int screenWidth = 1280,
        int screenHeight = 720,
        bool allowBuiltInFallback = true)
    {
        if (string.IsNullOrWhiteSpace(actionName))
        {
            return Result.Fail<VisionInputRequest, VisionError>(new VisionError(
                "EMPTY_ACTION",
                "Action name cannot be empty.",
                "Pass a semantic action name such as 'Confirm', 'Cancel', 'Pause', or register custom actions via 'set-controls'."));
        }

        string trimmed = actionName.Trim();
        IReadOnlyList<InputBinding>? candidates = null;

        if (profile != null)
        {
            var matched = profile.Actions.FirstOrDefault(a =>
                string.Equals(a.ActionName, trimmed, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(a.DisplayName, trimmed, StringComparison.OrdinalIgnoreCase));
            if (matched != null && matched.Bindings.Count > 0)
            {
                candidates = matched.Bindings;
            }
        }

        if (candidates == null && allowBuiltInFallback && BuiltInFallbackActions.TryGetValue(trimmed, out var defaults))
        {
            candidates = defaults;
        }

        if (candidates == null || candidates.Count == 0)
        {
            var known = profile != null && profile.Actions.Count > 0
                ? string.Join(", ", profile.Actions.Select(a => a.ActionName))
                : string.Join(", ", BuiltInFallbackActions.Keys);
            return Result.Fail<VisionInputRequest, VisionError>(new VisionError(
                "UNMAPPED_ACTION",
                $"Action '{trimmed}' is not mapped for this target. Available actions: [{known}].",
                "Inspect the game's controls menu or code and register the mapping with 'set-controls', or send a raw input via 'send-input'."));
        }

        var selected = SelectBestBinding(candidates, supportedCapabilities);
        return Result.Ok<VisionInputRequest, VisionError>(ToWireRequest(selected, screenWidth, screenHeight));
    }

    public static VisionInputRequest ToWireRequest(InputBinding binding, int screenWidth = 1280, int screenHeight = 720)
    {
        ArgumentNullException.ThrowIfNull(binding);

        return binding.Channel switch
        {
            InputChannel.Keyboard => new VisionInputRequest
            {
                Channel = VisionConstants.ChannelKeyboard,
                Action = "press",
                Key = binding.Key ?? "Enter",
                HoldFrames = Math.Max(1, binding.HoldFrames)
            },
            InputChannel.Gamepad => new VisionInputRequest
            {
                Channel = VisionConstants.ChannelGamepad,
                Action = "press",
                GamepadButton = binding.GamepadButton ?? "South",
                HoldFrames = Math.Max(1, binding.HoldFrames)
            },
            _ => new VisionInputRequest
            {
                Channel = VisionConstants.ChannelPointer,
                Action = "click",
                Button = binding.MouseButton,
                X = binding.NormalizedX.HasValue ? binding.NormalizedX.Value * Math.Max(1, screenWidth) : screenWidth / 2f,
                Y = binding.NormalizedY.HasValue ? binding.NormalizedY.Value * Math.Max(1, screenHeight) : screenHeight / 2f,
                HoldFrames = Math.Max(1, binding.HoldFrames)
            }
        };
    }

    private static InputBinding SelectBestBinding(
        IReadOnlyList<InputBinding> candidates,
        IReadOnlyCollection<string>? supportedCapabilities)
    {
        if (supportedCapabilities == null || supportedCapabilities.Count == 0)
        {
            return candidates[0];
        }

        foreach (var candidate in candidates)
        {
            string requiredCap = candidate.Channel switch
            {
                InputChannel.Keyboard => VisionConstants.CapKeyboard,
                InputChannel.Gamepad => VisionConstants.CapGamepad,
                _ => VisionConstants.CapPointer
            };

            if (supportedCapabilities.Any(c => string.Equals(c, requiredCap, StringComparison.OrdinalIgnoreCase) ||
                (requiredCap == VisionConstants.CapPointer && string.Equals(c, VisionConstants.CapPointerIsolated, StringComparison.OrdinalIgnoreCase))))
            {
                return candidate;
            }
        }

        return candidates[0];
    }
}
