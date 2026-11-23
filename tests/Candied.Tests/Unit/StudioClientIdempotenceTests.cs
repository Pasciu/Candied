using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Candied.Agent;
using Candied.Core.Errors;
using Candied.Core.Models;
using Candied.Core.Ports;
using Candied.Core.UseCases;
using Candied.Primitives;
using Candied.VisionBridge.Protocol;
using Candied.Workspace.Adapters;
using Xunit;

namespace Candied.Tests.Unit;

public sealed class StudioClientIdempotenceTests : IDisposable
{
    private readonly string _tempDir;

    public StudioClientIdempotenceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "Candied_IdempotenceTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
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

    private sealed class FakeRegistry : IRegisteredGameRegistry
    {
        private RegisteredGameInfo? _game;

        public FakeRegistry(RegisteredGameInfo? game = null)
        {
            _game = game;
        }

        public Task<IReadOnlyList<RegisteredGameInfo>> ListRegisteredGamesAsync(CancellationToken ct = default)
        {
            return Task.FromResult<IReadOnlyList<RegisteredGameInfo>>(_game != null ? [_game] : []);
        }

        public Task<Result<RegisteredGameInfo, string>> GetRegisteredGameAsync(string name, CancellationToken ct = default)
        {
            if (_game != null && string.Equals(_game.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(Result.Ok<RegisteredGameInfo, string>(_game));
            }
            return Task.FromResult(Result.Fail<RegisteredGameInfo, string>(
                $"Il gioco '{name}' non è registrato nell'allowlist. Per registrarlo eseguire: dotnet run --project src/Candied.Agent -- register-game <nome> <percorso_exe>"));
        }

        public Task<Result<RegisteredGameInfo, string>> RegisterGameAsync(string name, string executablePath, CancellationToken ct = default)
        {
            _game = new RegisteredGameInfo(name, executablePath, Path.GetDirectoryName(executablePath) ?? "", "x64", "2023.1", false, DateTimeOffset.UtcNow);
            return Task.FromResult(Result.Ok<RegisteredGameInfo, string>(_game));
        }

        public Task<Result<bool, string>> UnregisterGameAsync(string name, CancellationToken ct = default)
        {
            _game = null;
            return Task.FromResult(Result.Ok<bool, string>(true));
        }

        public Task<Result<bool, string>> UpdateBridgeStatusAsync(string name, bool hasBridgeInstalled, CancellationToken ct = default)
        {
            if (_game != null && string.Equals(_game.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                _game = _game with { HasBridgeInstalled = hasBridgeInstalled };
                return Task.FromResult(Result.Ok<bool, string>(true));
            }
            return Task.FromResult(Result.Fail<bool, string>("Not found"));
        }
    }

    private sealed class FakeStudioClient : IStudioClient
    {
        public bool EnsureRunningCalled { get; private set; }
        public int LaunchCallCount { get; private set; }
        public int CloseCallCount { get; private set; }
        public bool IsRunning { get; set; }
        public string? ActiveGame { get; set; }
        public StudioWindowInfo? Window { get; set; } = new StudioWindowInfo(
            Handle: 123456,
            IsVisible: true,
            WindowState: "Normal",
            Visibility: "Visible",
            RectLeft: 100,
            RectTop: 100,
            RectRight: 1100,
            RectBottom: 800,
            Width: 1000,
            Height: 700,
            IsWithinMonitor: true,
            IsResponding: true
        );

        public Task<StudioSessionInfo?> GetCurrentSessionAsync(CancellationToken ct = default)
        {
            return Task.FromResult<StudioSessionInfo?>(new StudioSessionInfo(
                StudioPid: Environment.ProcessId,
                StudioPort: 12345,
                StudioToken: "fake_token",
                ActiveGameName: ActiveGame,
                GamePid: 8888,
                BridgePort: 54321,
                BridgeToken: "fake_bridge_token"
            ));
        }

        public Task<Result<StudioStatusInfo, string>> GetStatusAsync(CancellationToken ct = default)
        {
            return Task.FromResult(Result.Ok<StudioStatusInfo, string>(new StudioStatusInfo(
                StudioPid: Environment.ProcessId,
                IsRunning: IsRunning,
                ActiveGameName: ActiveGame,
                GamePid: 8888,
                BridgePort: 54321,
                EmbeddingMode: "Direct (-parentHWND)",
                IsVisionConnected: true,
                Window: Window
            )));
        }

        public Task<Result<StudioSessionInfo, string>> EnsureStudioRunningAsync(CancellationToken ct = default)
        {
            EnsureRunningCalled = true;
            return Task.FromResult(Result.Ok<StudioSessionInfo, string>(new StudioSessionInfo(
                StudioPid: Environment.ProcessId,
                StudioPort: 12345,
                StudioToken: "fake_token"
            )));
        }

        public Task<Result<StudioStatusInfo, string>> LaunchGameAsync(string gameName, string executablePath, CancellationToken ct = default)
        {
            LaunchCallCount++;
            IsRunning = true;
            ActiveGame = gameName;
            return Task.FromResult(Result.Ok<StudioStatusInfo, string>(new StudioStatusInfo(
                StudioPid: Environment.ProcessId,
                IsRunning: true,
                ActiveGameName: gameName,
                GamePid: 8888,
                BridgePort: 54321,
                EmbeddingMode: "Direct (-parentHWND)",
                IsVisionConnected: true,
                Window: Window
            )));
        }

        public Task<Result<bool, string>> CloseGameAsync(CancellationToken ct = default)
        {
            CloseCallCount++;
            IsRunning = false;
            ActiveGame = null;
            return Task.FromResult(Result.Ok<bool, string>(true));
        }
    }

    private sealed class FakeGameDetector : IGameDetector
    {
        public Task<Result<GameEnvironment, DetectionError>> DetectGameAsync(string gameDirectoryPath, CancellationToken ct = default)
        {
            var env = new GameEnvironment(
                Id: new GameId("potion-craft"),
                GameName: "PotionCraft",
                GameRootPath: gameDirectoryPath,
                ManagedPath: Path.Combine(gameDirectoryPath, "Managed"),
                EngineType: UnityEngineType.Mono,
                Architecture: "x64",
                TargetFramework: "net472",
                BepInExInfo: new BepInExInstallation(true, "5.4.21", Path.Combine(gameDirectoryPath, "BepInEx")),
                DiscoveredAssemblies: [],
                EnvironmentWarnings: []
            );
            return Task.FromResult(Result.Ok<GameEnvironment, DetectionError>(env));
        }
    }

    private sealed class FakeVisionBridgeClient : IVisionBridgeClient
    {
        public Task<Result<VisionHelloDto, VisionBridgeError>> GetHelloAsync(string baseUrl, string token, CancellationToken ct = default)
        {
            return Task.FromResult(Result.Ok<VisionHelloDto, VisionBridgeError>(new VisionHelloDto
            {
                ProtocolVersion = "1.0.0",
                PluginVersion = "1.0.0",
                UnityVersion = "2023.1",
                ScreenWidth = 1024,
                ScreenHeight = 720
            }));
        }

        public Task<Result<VisionCapabilitiesDto, VisionBridgeError>> GetCapabilitiesAsync(string baseUrl, string token, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<Result<VisionScreenshot, VisionBridgeError>> CaptureScreenshotAsync(string baseUrl, string token, CancellationToken ct = default)
        {
            byte[] dummyPng = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0];
            return Task.FromResult(Result.Ok<VisionScreenshot, VisionBridgeError>(new VisionScreenshot(dummyPng, false, 1024, 720)));
        }

        public Task<Result<IReadOnlyList<VisionUiElementDto>, VisionBridgeError>> GetUiElementsAsync(string baseUrl, string token, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<Result<VisionClickResponse, VisionBridgeError>> SendClickAsync(string baseUrl, string token, VisionClickRequest request, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<Result<VisionClickResponse, VisionBridgeError>> SendInputClickAsync(string baseUrl, string token, float x, float y, int button = 0, string? method = null, string? action = null, int holdFrames = 5, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<Result<VisionWaitIdleResponse, VisionBridgeError>> WaitForIdleAsync(string baseUrl, string token, int maxFrames = 10, int timeoutMs = 5000, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<Result<VisionResizeResponse, VisionBridgeError>> ResizeAsync(string baseUrl, string token, int width, int height, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<Result<VisionSceneInfoDto, VisionBridgeError>> GetSceneInfoAsync(string baseUrl, string token, CancellationToken ct = default) =>
            Task.FromResult(Result.Ok<VisionSceneInfoDto, VisionBridgeError>(new VisionSceneInfoDto
            {
                ActiveSceneName = "MainMenu",
                ActiveSceneIndex = 0,
                RootCount = 12,
                IsLoaded = true,
                LoadedSceneCount = 1,
                ScreenWidth = 1024,
                ScreenHeight = 720,
                ActiveRendererCount = 24,
                TimeSinceLevelLoad = 5.0f
            }));

        public Task<Result<VisionSceneObjectsResponse, VisionBridgeError>> InspectSceneObjectsAsync(string baseUrl, string token, SceneObjectsQuery? query = null, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<Result<VisionRuntimeAssetsResponse, VisionBridgeError>> InspectRuntimeAssetsAsync(string baseUrl, string token, RuntimeAssetsQuery? query = null, CancellationToken ct = default) =>
            throw new NotImplementedException();
    }

    [Fact]
    public async Task LaunchGameByName_WhenGameNotRegistered_ReturnsActionableError()
    {
        var registry = new FakeRegistry(null);
        var studioClient = new FakeStudioClient();
        var workbench = new AgentWorkbenchService(
            null!, null!, null!, null!, new FakeGameDetector(),
            storageLayout: new CandiedStorageLayout(_tempDir),
            gameRegistry: registry,
            studioClient: studioClient,
            visionBridgeClient: new FakeVisionBridgeClient()
        );

        var responseJson = await workbench.LaunchGameByNameAsync("UnregisteredGame");
        using var doc = JsonDocument.Parse(responseJson);

        Assert.False(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains("non è registrato", doc.RootElement.GetProperty("error").GetString()!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("register-game", doc.RootElement.GetProperty("suggestion").GetString()!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, studioClient.LaunchCallCount);
    }

    [Fact]
    public async Task LaunchGameByName_WhenBridgeNotInstalled_ReturnsActionableInstructionWithoutAutoInstalling()
    {
        // Requirement 13: "Se il bridge non è installato nel gioco, game_launch non lo installa da solo:
        // risponde con l'istruzione esatta ('esegui: <comando install-bridge> <nome>')"
        var registeredGame = new RegisteredGameInfo(
            "TestGame",
            Path.Combine(_tempDir, "Game.exe"),
            _tempDir,
            "x64",
            "2023.1",
            HasBridgeInstalled: false, // Bridge is NOT installed
            RegisteredAtUtc: DateTimeOffset.UtcNow
        );

        var registry = new FakeRegistry(registeredGame);
        var studioClient = new FakeStudioClient();
        var workbench = new AgentWorkbenchService(
            null!, null!, null!, null!, new FakeGameDetector(),
            storageLayout: new CandiedStorageLayout(_tempDir),
            gameRegistry: registry,
            studioClient: studioClient,
            visionBridgeClient: new FakeVisionBridgeClient()
        );

        var responseJson = await workbench.LaunchGameByNameAsync("TestGame");
        using var doc = JsonDocument.Parse(responseJson);

        Assert.False(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains("VisionBridge non è installato", doc.RootElement.GetProperty("error").GetString()!);
        Assert.Contains("install-bridge TestGame", doc.RootElement.GetProperty("suggestion").GetString()!);
        Assert.Equal(0, studioClient.LaunchCallCount);
    }

    [Fact]
    public async Task LaunchGameByName_WhenAlreadyRunningSameGame_IsIdempotentAndDoesNotSpawnSecondInstance()
    {
        // Requirement 13: "Se Studio e gioco sono già attivi, non ne crea un secondo: restituisce lo stato attuale."
        var registeredGame = new RegisteredGameInfo(
            "PotionCraft",
            Path.Combine(_tempDir, "PotionCraft.exe"),
            _tempDir,
            "x64",
            "2023.1",
            HasBridgeInstalled: true,
            RegisteredAtUtc: DateTimeOffset.UtcNow
        );

        var registry = new FakeRegistry(registeredGame);
        var studioClient = new FakeStudioClient
        {
            IsRunning = true,
            ActiveGame = "PotionCraft" // Already active!
        };

        var workbench = new AgentWorkbenchService(
            null!, null!, null!, null!, new FakeGameDetector(),
            storageLayout: new CandiedStorageLayout(_tempDir),
            gameRegistry: registry,
            studioClient: studioClient,
            visionBridgeClient: new FakeVisionBridgeClient()
        );

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var responseJson = await workbench.LaunchGameByNameAsync("PotionCraft", cts.Token);

        // Studio launch should NOT be called because it is already active
        Assert.Equal(0, studioClient.LaunchCallCount);

        using var doc = JsonDocument.Parse(responseJson);
        Assert.True(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("Running", doc.RootElement.GetProperty("status").GetString());
        Assert.Equal("PotionCraft", doc.RootElement.GetProperty("gameName").GetString());
        Assert.True(doc.RootElement.TryGetProperty("window", out var winProp));
        Assert.True(winProp.GetProperty("isWindowVisible").GetBoolean());
    }

    [Fact]
    public async Task LaunchGameByName_WhenStudioWindowNotVisibleOrOffscreen_FailsWithActionableErrorAndLogPath()
    {
        var registeredGame = new RegisteredGameInfo(
            "PotionCraft",
            Path.Combine(_tempDir, "PotionCraft.exe"),
            _tempDir,
            "x64",
            "2023.1",
            HasBridgeInstalled: true,
            RegisteredAtUtc: DateTimeOffset.UtcNow
        );

        var registry = new FakeRegistry(registeredGame);
        var studioClient = new FakeStudioClient
        {
            IsRunning = false,
            // Window is not visible / zero handle
            Window = new StudioWindowInfo(
                Handle: 0,
                IsVisible: false,
                WindowState: "Hidden",
                Visibility: "Hidden",
                RectLeft: 0,
                RectTop: 0,
                RectRight: 0,
                RectBottom: 0,
                Width: 0,
                Height: 0,
                IsWithinMonitor: false,
                IsResponding: false
            )
        };

        var workbench = new AgentWorkbenchService(
            null!, null!, null!, null!, new FakeGameDetector(),
            storageLayout: new CandiedStorageLayout(_tempDir),
            gameRegistry: registry,
            studioClient: studioClient,
            visionBridgeClient: new FakeVisionBridgeClient()
        );

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var responseJson = await workbench.LaunchGameByNameAsync("PotionCraft", cts.Token);

        using var doc = JsonDocument.Parse(responseJson);
        Assert.False(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains("non è valida, non è visibile o è fuori dai limiti del monitor", doc.RootElement.GetProperty("error").GetString()!);
        Assert.Contains("studio.log", doc.RootElement.GetProperty("logPath").GetString()!);
    }

    [Fact]
    public async Task LaunchGameByName_WithWaitForLogMarker_SucceedsWhenMarkerPresentAndTwoStableWindowsReached()
    {
        var bepDir = Path.Combine(_tempDir, "BepInEx");
        Directory.CreateDirectory(bepDir);
        await File.WriteAllTextAsync(Path.Combine(bepDir, "LogOutput.log"), "[Info : SampleMod] Main menu ready!");

        var registeredGame = new RegisteredGameInfo(
            "SampleGame",
            Path.Combine(_tempDir, "SampleGame.exe"),
            _tempDir,
            "x64",
            "2023.1",
            HasBridgeInstalled: true,
            RegisteredAtUtc: DateTimeOffset.UtcNow
        );

        var registry = new FakeRegistry(registeredGame);
        var studioClient = new FakeStudioClient
        {
            IsRunning = true,
            ActiveGame = "SampleGame"
        };

        var workbench = new AgentWorkbenchService(
            null!, null!, null!, null!, new FakeGameDetector(),
            storageLayout: new CandiedStorageLayout(_tempDir),
            gameRegistry: registry,
            studioClient: studioClient,
            visionBridgeClient: new FakeVisionBridgeClient()
        );

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));
        var responseJson = await workbench.LaunchGameByNameAsync("SampleGame", waitForLog: "Main menu ready!", ct: cts.Token);

        using var doc = JsonDocument.Parse(responseJson);
        Assert.True(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains("2 finestre consecutive", doc.RootElement.GetProperty("waitOutcome").GetString()!);
        Assert.Equal("MainMenu", doc.RootElement.GetProperty("activeSceneName").GetString());
        Assert.Equal(24, doc.RootElement.GetProperty("activeRendererCount").GetInt32());
    }
}
