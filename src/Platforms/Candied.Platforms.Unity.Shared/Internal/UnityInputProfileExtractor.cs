using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Candied.Platforms.Unity.Shared.Contracts;
using Candied.Vision.Controls;

namespace Candied.Platforms.Unity.Shared.Internal;

/// <summary>
/// Automatically discovers Unity Input System (.inputactions JSON) bindings when present in StreamingAssets or game data.
/// </summary>
internal sealed class UnityInputProfileExtractor : IUnityInputExtractor
{
    public async Task<IReadOnlyList<ControlActionEntry>> ExtractControlsOfflineAsync(
        string gameRootPath,
        string dataDirectoryPath,
        CancellationToken ct = default)
    {
        var results = new List<ControlActionEntry>();
        if (string.IsNullOrWhiteSpace(dataDirectoryPath) || !Directory.Exists(dataDirectoryPath))
        {
            return results;
        }

        try
        {
            var jsonCandidates = Directory.EnumerateFiles(dataDirectoryPath, "*.inputactions", SearchOption.AllDirectories)
                .Concat(Directory.EnumerateFiles(dataDirectoryPath, "*input*.json", SearchOption.AllDirectories))
                .Take(10)
                .ToList();

            foreach (var file in jsonCandidates)
            {
                if (ct.IsCancellationRequested) break;

                try
                {
                    string content = await File.ReadAllTextAsync(file, ct).ConfigureAwait(false);
                    using var doc = JsonDocument.Parse(content);
                    if (!doc.RootElement.TryGetProperty("maps", out var maps) || maps.ValueKind != JsonValueKind.Array)
                    {
                        continue;
                    }

                    foreach (var map in maps.EnumerateArray())
                    {
                        if (!map.TryGetProperty("actions", out var actionsElem) || !map.TryGetProperty("bindings", out var bindingsElem))
                        {
                            continue;
                        }

                        var actionNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        foreach (var act in actionsElem.EnumerateArray())
                        {
                            if (act.TryGetProperty("name", out var n) && n.GetString() is { Length: > 0 } nameStr)
                            {
                                actionNames.Add(nameStr);
                            }
                        }

                        var byAction = new Dictionary<string, List<InputBinding>>(StringComparer.OrdinalIgnoreCase);
                        foreach (var b in bindingsElem.EnumerateArray())
                        {
                            string? action = b.TryGetProperty("action", out var aProp) ? aProp.GetString() : null;
                            string? path = b.TryGetProperty("path", out var pProp) ? pProp.GetString() : null;
                            if (string.IsNullOrWhiteSpace(action) || string.IsNullOrWhiteSpace(path) || !actionNames.Contains(action))
                            {
                                continue;
                            }

                            var parsed = TryParseUnityBindingPath(path);
                            if (parsed != null)
                            {
                                if (!byAction.TryGetValue(action, out var list))
                                {
                                    list = new List<InputBinding>();
                                    byAction[action] = list;
                                }

                                list.Add(parsed);
                            }
                        }

                        foreach (var kvp in byAction)
                        {
                            results.Add(new ControlActionEntry(
                                ActionName: kvp.Key,
                                DisplayName: kvp.Key,
                                Source: "auto",
                                Bindings: kvp.Value));
                        }
                    }
                }
                catch
                {
                }
            }
        }
        catch
        {
        }

        return results;
    }

    internal static InputBinding? TryParseUnityBindingPath(string bindingPath)
    {
        if (string.IsNullOrWhiteSpace(bindingPath))
        {
            return null;
        }

        // Examples: "<Keyboard>/space", "<Keyboard>/enter", "<Gamepad>/buttonSouth", "<Gamepad>/start"
        string trimmed = bindingPath.Trim();
        int slashIdx = trimmed.IndexOf('/', StringComparison.Ordinal);
        if (slashIdx <= 0 || slashIdx + 1 >= trimmed.Length)
        {
            return null;
        }

        string device = trimmed[..slashIdx].Trim('<', '>');
        string control = trimmed[(slashIdx + 1)..].Trim();

        if (device.Equals("Keyboard", StringComparison.OrdinalIgnoreCase))
        {
            string normalizedKey = control.ToLowerInvariant() switch
            {
                "return" or "enter" or "numpadenter" => "Enter",
                "escape" => "Escape",
                "space" => "Space",
                "tab" => "Tab",
                "backspace" => "Backspace",
                "uparrow" => "UpArrow",
                "downarrow" => "DownArrow",
                "leftarrow" => "LeftArrow",
                "rightarrow" => "RightArrow",
                _ => control.Length == 1 ? control.ToUpperInvariant() : control
            };

            return new InputBinding(InputChannel.Keyboard, Key: normalizedKey, Description: trimmed);
        }

        if (device.Equals("Gamepad", StringComparison.OrdinalIgnoreCase))
        {
            string normalizedPad = control.ToLowerInvariant() switch
            {
                "buttonsouth" => "South",
                "buttoneast" => "East",
                "buttonwest" => "West",
                "buttonnorth" => "North",
                "start" => "Start",
                "select" => "Select",
                "dpad/up" => "DPadUp",
                "dpad/down" => "DPadDown",
                "dpad/left" => "DPadLeft",
                "dpad/right" => "DPadRight",
                "leftshoulder" => "LeftShoulder",
                "rightshoulder" => "RightShoulder",
                _ => control
            };

            return new InputBinding(InputChannel.Gamepad, GamepadButton: normalizedPad, Description: trimmed);
        }

        return null;
    }
}
