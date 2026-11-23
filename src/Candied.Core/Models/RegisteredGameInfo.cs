using System;

namespace Candied.Core.Models;

/// <summary>
/// Represents a game registered in the user's allowlist.
/// </summary>
public sealed record RegisteredGameInfo(
    string Name,
    string ExecutablePath,
    string GameRootPath,
    string Architecture,
    string? UnityVersion,
    bool HasBridgeInstalled,
    DateTimeOffset RegisteredAtUtc
);
