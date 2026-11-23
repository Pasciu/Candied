using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Candied.Core.Models;
using Candied.Core.Ports;
using Candied.Primitives;

namespace Candied.Workspace.Adapters;

/// <summary>
/// Manages user-registered games stored in JSON configuration outside the repository.
/// </summary>
internal sealed class JsonFileGameRegistryAdapter : IRegisteredGameRegistry
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };
    private readonly IGameDetector _gameDetector;
    private readonly string _registryFilePath;

    public JsonFileGameRegistryAdapter(IGameDetector gameDetector, string? customRegistryFilePath = null)
    {
        _gameDetector = gameDetector ?? throw new ArgumentNullException(nameof(gameDetector));
        _registryFilePath = customRegistryFilePath ?? CandiedUserConfigPaths.GetRegisteredGamesPath();
    }

    public async Task<IReadOnlyList<RegisteredGameInfo>> ListRegisteredGamesAsync(CancellationToken ct = default)
    {
        var games = await LoadGamesInternalAsync(ct).ConfigureAwait(false);

        // Re-probe bridge existence on disk to reflect live installation status
        var updated = new List<RegisteredGameInfo>(games.Count);
        bool modified = false;

        foreach (var game in games)
        {
            bool bridgeOnDisk = CheckBridgeInstalled(game.GameRootPath);
            if (bridgeOnDisk != game.HasBridgeInstalled)
            {
                updated.Add(game with { HasBridgeInstalled = bridgeOnDisk });
                modified = true;
            }
            else
            {
                updated.Add(game);
            }
        }

        if (modified)
        {
            await SaveGamesInternalAsync(updated, ct).ConfigureAwait(false);
        }

        return updated;
    }

    public async Task<Result<RegisteredGameInfo, string>> GetRegisteredGameAsync(string name, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Result.Fail<RegisteredGameInfo, string>("Game name cannot be empty.");
        }

        var games = await ListRegisteredGamesAsync(ct).ConfigureAwait(false);
        var match = games.FirstOrDefault(g => string.Equals(g.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));

        if (match == null)
        {
            return Result.Fail<RegisteredGameInfo, string>(
                $"Il gioco '{name}' non è registrato nell'allowlist. Per registrarlo eseguire: dotnet run --project src/Candied.Agent -- register-game <nome> <percorso_exe>");
        }

        return Result.Ok<RegisteredGameInfo, string>(match);
    }

    public async Task<Result<RegisteredGameInfo, string>> RegisterGameAsync(string name, string executablePath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Result.Fail<RegisteredGameInfo, string>("Game name cannot be empty.");
        }

        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return Result.Fail<RegisteredGameInfo, string>("Executable path cannot be empty.");
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(executablePath.Trim());
        }
        catch (Exception ex)
        {
            return Result.Fail<RegisteredGameInfo, string>($"Percorso non valido: {ex.Message}");
        }

        if (!File.Exists(fullPath))
        {
            return Result.Fail<RegisteredGameInfo, string>($"Il file eseguibile specificato non esiste: '{fullPath}'");
        }

        var gameDir = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrEmpty(gameDir))
        {
            return Result.Fail<RegisteredGameInfo, string>("Impossibile determinare la cartella del gioco.");
        }

        var detectResult = await _gameDetector.DetectGameAsync(gameDir, ct).ConfigureAwait(false);
        if (detectResult.IsFailure)
        {
            return Result.Fail<RegisteredGameInfo, string>($"Rilevamento del gioco fallito: {detectResult.Error.Message}");
        }

        var detectedGame = detectResult.Value;

        // Strictly reject IL2CPP per requirement
        if (detectedGame.EngineType == UnityEngineType.Il2Cpp)
        {
            return Result.Fail<RegisteredGameInfo, string>(
                "Il gioco selezionato utilizza il runtime Unity IL2CPP. Candied supporta esclusivamente giochi Unity Mono.");
        }

        bool hasBridge = CheckBridgeInstalled(gameDir);

        var record = new RegisteredGameInfo(
            Name: name.Trim(),
            ExecutablePath: fullPath,
            GameRootPath: gameDir,
            Architecture: detectedGame.Architecture,
            UnityVersion: detectedGame.UnityVersion,
            HasBridgeInstalled: hasBridge,
            RegisteredAtUtc: DateTimeOffset.UtcNow
        );

        var games = (await LoadGamesInternalAsync(ct).ConfigureAwait(false)).ToList();
        int existingIdx = games.FindIndex(g => string.Equals(g.Name, record.Name, StringComparison.OrdinalIgnoreCase));
        if (existingIdx >= 0)
        {
            games[existingIdx] = record;
        }
        else
        {
            games.Add(record);
        }

        await SaveGamesInternalAsync(games, ct).ConfigureAwait(false);
        return Result.Ok<RegisteredGameInfo, string>(record);
    }

    public async Task<Result<bool, string>> UnregisterGameAsync(string name, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Result.Fail<bool, string>("Game name cannot be empty.");
        }

        var games = (await LoadGamesInternalAsync(ct).ConfigureAwait(false)).ToList();
        int removedCount = games.RemoveAll(g => string.Equals(g.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));

        if (removedCount == 0)
        {
            return Result.Fail<bool, string>($"Nessun gioco registrato con nome '{name}'.");
        }

        await SaveGamesInternalAsync(games, ct).ConfigureAwait(false);
        return Result.Ok<bool, string>(true);
    }

    public async Task<Result<bool, string>> UpdateBridgeStatusAsync(string name, bool hasBridgeInstalled, CancellationToken ct = default)
    {
        var games = (await LoadGamesInternalAsync(ct).ConfigureAwait(false)).ToList();
        int idx = games.FindIndex(g => string.Equals(g.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));

        if (idx < 0)
        {
            return Result.Fail<bool, string>($"Nessun gioco registrato con nome '{name}'.");
        }

        games[idx] = games[idx] with { HasBridgeInstalled = hasBridgeInstalled };
        await SaveGamesInternalAsync(games, ct).ConfigureAwait(false);
        return Result.Ok<bool, string>(true);
    }

    private static bool CheckBridgeInstalled(string gameRoot)
    {
        var p1 = Path.Combine(gameRoot, "BepInEx", "plugins", "Candied.VisionBridge.dll");
        var p2 = Path.Combine(gameRoot, "BepInEx", "plugins", "Candied.VisionBridge", "Candied.VisionBridge.dll");
        return File.Exists(p1) || File.Exists(p2);
    }

    private async Task<List<RegisteredGameInfo>> LoadGamesInternalAsync(CancellationToken ct)
    {
        if (!File.Exists(_registryFilePath))
        {
            return new List<RegisteredGameInfo>();
        }

        try
        {
            var json = await File.ReadAllTextAsync(_registryFilePath, ct).ConfigureAwait(false);
            var list = JsonSerializer.Deserialize<List<RegisteredGameInfo>>(json);
            return list ?? new List<RegisteredGameInfo>();
        }
        catch
        {
            return new List<RegisteredGameInfo>();
        }
    }

    private async Task SaveGamesInternalAsync(IReadOnlyList<RegisteredGameInfo> games, CancellationToken ct)
    {
        var dir = Path.GetDirectoryName(_registryFilePath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var json = JsonSerializer.Serialize(games, SerializerOptions);
        var tempPath = _registryFilePath + ".tmp";
        await File.WriteAllTextAsync(tempPath, json, ct).ConfigureAwait(false);
        File.Move(tempPath, _registryFilePath, overwrite: true);
    }
}
