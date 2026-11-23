using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Candied.Core.Errors;
using Candied.Core.Models;
using Candied.Core.Ports;
using Candied.Primitives;
using Candied.Workspace.Adapters;
using Xunit;

namespace Candied.Tests.Unit;

public sealed class RegisteredGameRegistryTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _registryFilePath;

    public RegisteredGameRegistryTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "Candied_RegistryTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _registryFilePath = Path.Combine(_tempDir, "registered_games.json");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch { }
    }

    private sealed class FakeGameDetector : IGameDetector
    {
        private readonly UnityEngineType _engineType;

        public FakeGameDetector(UnityEngineType engineType = UnityEngineType.Mono)
        {
            _engineType = engineType;
        }

        public Task<Result<GameEnvironment, DetectionError>> DetectGameAsync(string gameDirectoryPath, CancellationToken ct = default)
        {
            var env = new GameEnvironment(
                Id: new GameId("test-game"),
                GameName: "TestGame",
                GameRootPath: gameDirectoryPath,
                ManagedPath: Path.Combine(gameDirectoryPath, "TestGame_Data", "Managed"),
                EngineType: _engineType,
                Architecture: "x64",
                TargetFramework: "net472",
                BepInExInfo: new BepInExInstallation(true, "5.4.21", Path.Combine(gameDirectoryPath, "BepInEx")),
                DiscoveredAssemblies: Array.Empty<DiscoveredAssembly>(),
                EnvironmentWarnings: Array.Empty<string>()
            );
            return Task.FromResult(Result.Ok<GameEnvironment, DetectionError>(env));
        }
    }

    [Fact]
    public async Task RegisterGame_WithValidMonoGame_SucceedsAndPersists()
    {
        // Arrange
        var fakeDetector = new FakeGameDetector(UnityEngineType.Mono);
        var registry = new JsonFileGameRegistryAdapter(fakeDetector, _registryFilePath);

        var dummyExe = Path.Combine(_tempDir, "Game.exe");
        File.WriteAllText(dummyExe, "MZ");

        // Act
        var result = await registry.RegisterGameAsync("MyGame", dummyExe);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("MyGame", result.Value.Name);
        Assert.Equal(dummyExe, result.Value.ExecutablePath);
        Assert.False(result.Value.HasBridgeInstalled);

        var list = await registry.ListRegisteredGamesAsync();
        Assert.Single(list);
        Assert.Equal("MyGame", list[0].Name);
    }

    [Fact]
    public async Task RegisterGame_WithNonExistentExecutable_ReturnsFailure()
    {
        var fakeDetector = new FakeGameDetector(UnityEngineType.Mono);
        var registry = new JsonFileGameRegistryAdapter(fakeDetector, _registryFilePath);

        var result = await registry.RegisterGameAsync("MissingGame", Path.Combine(_tempDir, "NonExistent.exe"));

        Assert.True(result.IsFailure);
        Assert.Contains("non esiste", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RegisterGame_WithIl2Cpp_StrictlyRejectedWithExactMessage()
    {
        // Requirement 12: "Registrando un gioco, rifiuta IL2CPP con messaggio chiaro."
        var fakeDetector = new FakeGameDetector(UnityEngineType.Il2Cpp);
        var registry = new JsonFileGameRegistryAdapter(fakeDetector, _registryFilePath);

        var dummyExe = Path.Combine(_tempDir, "Il2CppGame.exe");
        File.WriteAllText(dummyExe, "MZ");

        var result = await registry.RegisterGameAsync("Il2CppGame", dummyExe);

        Assert.True(result.IsFailure);
        Assert.Equal("Il gioco selezionato utilizza il runtime Unity IL2CPP. Candied supporta esclusivamente giochi Unity Mono.", result.Error);
    }

    [Fact]
    public async Task ListRegisteredGames_DetectsBridgeInstalledOnDisk()
    {
        var fakeDetector = new FakeGameDetector(UnityEngineType.Mono);
        var registry = new JsonFileGameRegistryAdapter(fakeDetector, _registryFilePath);

        var dummyExe = Path.Combine(_tempDir, "Game.exe");
        File.WriteAllText(dummyExe, "MZ");

        await registry.RegisterGameAsync("MyGame", dummyExe);

        // Initially no bridge
        var list1 = await registry.ListRegisteredGamesAsync();
        Assert.False(list1[0].HasBridgeInstalled);

        // Create bridge on disk
        var bridgeDir = Path.Combine(_tempDir, "BepInEx", "plugins", "Candied.VisionBridge");
        Directory.CreateDirectory(bridgeDir);
        File.WriteAllText(Path.Combine(bridgeDir, "Candied.VisionBridge.dll"), "DLL");

        // Listing should re-probe disk and report true
        var list2 = await registry.ListRegisteredGamesAsync();
        Assert.True(list2[0].HasBridgeInstalled);
    }

    [Fact]
    public async Task UnregisterGame_RemovesGameFromAllowlist()
    {
        var fakeDetector = new FakeGameDetector(UnityEngineType.Mono);
        var registry = new JsonFileGameRegistryAdapter(fakeDetector, _registryFilePath);

        var dummyExe = Path.Combine(_tempDir, "Game.exe");
        File.WriteAllText(dummyExe, "MZ");

        await registry.RegisterGameAsync("MyGame", dummyExe);
        var unreg = await registry.UnregisterGameAsync("MyGame");

        Assert.True(unreg.IsSuccess);
        var list = await registry.ListRegisteredGamesAsync();
        Assert.Empty(list);
    }
}
