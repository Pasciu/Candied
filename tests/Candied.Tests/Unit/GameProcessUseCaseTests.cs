using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Candied.Core.Errors;
using Candied.Core.Models;
using Candied.Core.Ports;
using Candied.Core.UseCases;
using Candied.Primitives;
using Xunit;

namespace Candied.Tests.Unit;

public class GameProcessUseCaseTests
{
    private static GameEnvironment CreateSampleGame() => new(
        Id: new GameId("TestGame"),
        GameName: "TestGame",
        GameRootPath: "C:/Games/TestGame",
        ManagedPath: "C:/Games/TestGame/Managed",
        EngineType: UnityEngineType.Mono,
        Architecture: "x64",
        TargetFramework: "net472",
        BepInExInfo: new BepInExInstallation(true, "5.4.21.0", "C:/Games/TestGame/BepInEx"),
        DiscoveredAssemblies: new List<DiscoveredAssembly>(),
        EnvironmentWarnings: new List<string>()
    );

    private sealed class FakeGameProcessManager : IGameProcessManager
    {
        public GameProcessInfo? CurrentProcess { get; set; }
        public bool FailLaunch { get; set; }
        public bool FailStop { get; set; }

        public Task<Result<GameProcessInfo?, ProcessManagementError>> GetRunningGameAsync(GameEnvironment game, CancellationToken ct = default)
        {
            return Task.FromResult(Result.Ok<GameProcessInfo?, ProcessManagementError>(CurrentProcess));
        }

        public Task<Result<GameProcessInfo, ProcessManagementError>> LaunchGameAsync(GameEnvironment game, GameLaunchOptions options, CancellationToken ct = default)
        {
            if (FailLaunch)
            {
                return Task.FromResult(Result.Fail<GameProcessInfo, ProcessManagementError>(
                    new ProcessManagementError("Failed to launch executable")));
            }

            var started = new GameProcessInfo(
                Id: new ProcessId(1234),
                ProcessName: "TestGame",
                MainWindowTitle: "Test Game Window",
                IsResponding: true,
                HasBridgeAttached: false,
                StartTime: DateTime.UtcNow
            );
            CurrentProcess = started;
            return Task.FromResult(Result.Ok<GameProcessInfo, ProcessManagementError>(started));
        }

        public Task<Result<Candied.Primitives.Unit, ProcessManagementError>> StopGameAsync(GameEnvironment game, CancellationToken ct = default)
        {
            if (FailStop)
            {
                return Task.FromResult(Result.Fail<Candied.Primitives.Unit, ProcessManagementError>(
                    new ProcessManagementError("Process could not be terminated")));
            }

            CurrentProcess = null;
            return Task.FromResult(Result.Ok<Candied.Primitives.Unit, ProcessManagementError>(new Candied.Primitives.Unit()));
        }
    }

    [Fact]
    public async Task GetGameProcessStatus_Returns_Null_When_Not_Running()
    {
        var pm = new FakeGameProcessManager { CurrentProcess = null };
        var useCase = new GetGameProcessStatusUseCase(pm);

        var result = await useCase.ExecuteAsync(CreateSampleGame());

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task GetGameProcessStatus_Returns_Process_When_Running()
    {
        var pm = new FakeGameProcessManager
        {
            CurrentProcess = new GameProcessInfo(new ProcessId(999), "TestGame", "Window", true, false, DateTime.UtcNow)
        };
        var useCase = new GetGameProcessStatusUseCase(pm);

        var result = await useCase.ExecuteAsync(CreateSampleGame());

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(999, result.Value.Id.Value);
    }

    [Fact]
    public async Task LaunchGame_When_Already_Running_Attaches_Without_Relaunching()
    {
        var existing = new GameProcessInfo(new ProcessId(777), "TestGame", "Window", true, false, DateTime.UtcNow);
        var pm = new FakeGameProcessManager { CurrentProcess = existing, FailLaunch = true };
        var useCase = new LaunchGameUseCase(pm);

        var result = await useCase.ExecuteAsync(CreateSampleGame());

        Assert.True(result.IsSuccess);
        Assert.Equal(777, result.Value.Id.Value);
    }

    [Fact]
    public async Task LaunchGame_When_Not_Running_Launches_New_Process()
    {
        var pm = new FakeGameProcessManager { CurrentProcess = null };
        var useCase = new LaunchGameUseCase(pm);

        var result = await useCase.ExecuteAsync(CreateSampleGame());

        Assert.True(result.IsSuccess);
        Assert.Equal(1234, result.Value.Id.Value);
    }

    [Fact]
    public async Task StopGame_Terminates_Process_Successfully()
    {
        var pm = new FakeGameProcessManager
        {
            CurrentProcess = new GameProcessInfo(new ProcessId(1234), "TestGame", "Window", true, false, DateTime.UtcNow)
        };
        var useCase = new StopGameUseCase(pm);

        var result = await useCase.ExecuteAsync(CreateSampleGame());

        Assert.True(result.IsSuccess);
        Assert.Null(pm.CurrentProcess);
    }
}
