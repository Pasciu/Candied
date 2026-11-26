using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Candied.Vision.Controls;
using Candied.Vision.Ports;

namespace Candied.Vision.Adapters;

internal sealed class JsonGameControlProfileStoreAdapter : IGameControlProfileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public async Task<GameControlProfile?> LoadProfileAsync(string profileFilePath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(profileFilePath) || !File.Exists(profileFilePath))
        {
            return null;
        }

        try
        {
            string json = await File.ReadAllTextAsync(profileFilePath, ct).ConfigureAwait(false);
            return JsonSerializer.Deserialize<GameControlProfile>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public async Task SaveProfileAsync(string profileFilePath, GameControlProfile profile, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileFilePath);
        ArgumentNullException.ThrowIfNull(profile);

        string? dir = Path.GetDirectoryName(profileFilePath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        string json = JsonSerializer.Serialize(profile, JsonOptions);
        string tempPath = profileFilePath + ".tmp";
        await File.WriteAllTextAsync(tempPath, json, ct).ConfigureAwait(false);
        File.Move(tempPath, profileFilePath, overwrite: true);
    }

    public async Task<GameControlProfile> MergeAndSaveActionsAsync(
        string profileFilePath,
        string targetId,
        string targetName,
        IReadOnlyList<ControlActionEntry> incomingActions,
        bool overwriteHigherPrioritySources = false,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileFilePath);
        ArgumentNullException.ThrowIfNull(incomingActions);

        var existing = await LoadProfileAsync(profileFilePath, ct).ConfigureAwait(false);
        var byName = new Dictionary<string, ControlActionEntry>(StringComparer.OrdinalIgnoreCase);

        if (existing != null)
        {
            foreach (var action in existing.Actions)
            {
                byName[action.ActionName] = action;
            }
        }

        foreach (var incoming in incomingActions)
        {
            if (string.IsNullOrWhiteSpace(incoming.ActionName))
            {
                continue;
            }

            if (byName.TryGetValue(incoming.ActionName, out var current))
            {
                int currentPriority = GetSourcePriority(current.Source);
                int incomingPriority = GetSourcePriority(incoming.Source);

                if (overwriteHigherPrioritySources || incomingPriority >= currentPriority)
                {
                    byName[incoming.ActionName] = incoming;
                }
            }
            else
            {
                byName[incoming.ActionName] = incoming;
            }
        }

        var merged = new GameControlProfile(
            TargetId: string.IsNullOrWhiteSpace(targetId) ? (existing?.TargetId ?? targetName) : targetId,
            TargetName: string.IsNullOrWhiteSpace(targetName) ? (existing?.TargetName ?? targetId) : targetName,
            UpdatedAtUtc: DateTimeOffset.UtcNow,
            Actions: byName.Values.OrderBy(a => a.ActionName, StringComparer.OrdinalIgnoreCase).ToList());

        await SaveProfileAsync(profileFilePath, merged, ct).ConfigureAwait(false);
        return merged;
    }

    private static int GetSourcePriority(string? source)
    {
        if (string.Equals(source, "user", StringComparison.OrdinalIgnoreCase))
        {
            return 3;
        }

        if (string.Equals(source, "agent", StringComparison.OrdinalIgnoreCase))
        {
            return 2;
        }

        return 1; // "auto"
    }
}
