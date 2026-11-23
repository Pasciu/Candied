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

public class InspectGameEnvironmentUseCaseTests
{
    private sealed class FakeGameDetector : IGameDetector
    {
        public bool ShouldFail { get; set; }

        public Task<Result<GameEnvironment, DetectionError>> DetectGameAsync(string gameDirectoryPath, CancellationToken ct = default)
        {
            if (ShouldFail)
            {
                return Task.FromResult(Result.Fail<GameEnvironment, DetectionError>(new DetectionError("Directory not found")));
            }

            var env = new GameEnvironment(
                Id: new GameId("TestGame"),
                GameName: "TestGame",
                GameRootPath: gameDirectoryPath,
                ManagedPath: $"{gameDirectoryPath}/Managed",
                EngineType: UnityEngineType.Mono,
                Architecture: "x64",
                TargetFramework: "net472",
                BepInExInfo: new BepInExInstallation(true, "5.4.21.0", $"{gameDirectoryPath}/BepInEx"),
                DiscoveredAssemblies: new List<DiscoveredAssembly>
                {
                    new("Assembly-CSharp.dll", $"{gameDirectoryPath}/Managed/Assembly-CSharp.dll", 1024, AssemblyTier.CoreGame, "Core", false),
                    new("TestGame.Logic.dll", $"{gameDirectoryPath}/Managed/TestGame.Logic.dll", 2048, AssemblyTier.CoreGame, "Game prefix", false)
                },
                EnvironmentWarnings: new List<string>()
            );

            return Task.FromResult(Result.Ok<GameEnvironment, DetectionError>(env));
        }
    }

    [Fact]
    public async Task ExecuteAsync_Returns_GameEnvironment_On_Success()
    {
        var detector = new FakeGameDetector();
        var useCase = new InspectGameEnvironmentUseCase(detector);

        var result = await useCase.ExecuteAsync("C:/Games/TestGame");

        Assert.True(result.IsSuccess);
        Assert.Equal("TestGame", result.Value.GameName);
        Assert.Equal("x64", result.Value.Architecture);
        Assert.Equal("net472", result.Value.TargetFramework);
        Assert.True(result.Value.BepInExInfo.IsInstalled);
        Assert.Equal("5.4.21.0", result.Value.BepInExInfo.DetectedVersion);
        Assert.Equal(2, result.Value.DiscoveredAssemblies.Count);
    }

    [Fact]
    public async Task ExecuteAsync_Returns_Failure_When_Detector_Fails()
    {
        var detector = new FakeGameDetector { ShouldFail = true };
        var useCase = new InspectGameEnvironmentUseCase(detector);

        var result = await useCase.ExecuteAsync("C:/Games/Invalid");

        Assert.True(result.IsFailure);
        Assert.Contains("Directory not found", result.Error.Message);
    }
}
