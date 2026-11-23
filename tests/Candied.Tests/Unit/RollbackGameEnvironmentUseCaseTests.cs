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

public class RollbackGameEnvironmentUseCaseTests
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
                BepInExInfo: new BepInExInstallation(true, "5.4.23.2", $"{gameDirectoryPath}/BepInEx"),
                DiscoveredAssemblies: new List<DiscoveredAssembly>(),
                EnvironmentWarnings: new List<string>()
            );

            return Task.FromResult(Result.Ok<GameEnvironment, DetectionError>(env));
        }
    }

    private sealed class FakeProvisioner : IEnvironmentProvisioner
    {
        public bool ShouldFail { get; set; }
        public bool RollbackCalled { get; private set; }

        public Task<Result<ProvisioningReport, ProvisioningError>> ProvisionEnvironmentAsync(
            GameEnvironment gameEnv,
            ProvisioningSpec spec,
            CandiedStorageLayout storageLayout,
            CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<Result<RollbackReport, ProvisioningError>> RollbackEnvironmentAsync(
            GameEnvironment gameEnv,
            CandiedStorageLayout storageLayout,
            CancellationToken ct = default)
        {
            RollbackCalled = true;
            if (ShouldFail)
            {
                return Task.FromResult(Result.Fail<RollbackReport, ProvisioningError>(new ProvisioningError("Manifest not found")));
            }

            return Task.FromResult(Result.Ok<RollbackReport, ProvisioningError>(
                new RollbackReport(true, 5, new List<string> { "winhttp.dll", "doorstop_config.ini", ".candied_manifest.json" })));
        }
    }

    [Fact]
    public async Task ExecuteAsync_Rolls_Back_Environment_On_Success()
    {
        var detector = new FakeGameDetector();
        var provisioner = new FakeProvisioner();
        var layout = new CandiedStorageLayout("C:/Workspace");
        var useCase = new RollbackGameEnvironmentUseCase(detector, provisioner);

        var result = await useCase.ExecuteAsync("C:/Games/TestGame", layout);

        Assert.True(result.IsSuccess);
        Assert.True(provisioner.RollbackCalled);
        Assert.Equal(5, result.Value.FilesRemovedCount);
        Assert.Contains("winhttp.dll", result.Value.RemovedFiles);
    }

    [Fact]
    public async Task ExecuteAsync_Fails_When_Rollback_Fails()
    {
        var detector = new FakeGameDetector();
        var provisioner = new FakeProvisioner { ShouldFail = true };
        var layout = new CandiedStorageLayout("C:/Workspace");
        var useCase = new RollbackGameEnvironmentUseCase(detector, provisioner);

        var result = await useCase.ExecuteAsync("C:/Games/TestGame", layout);

        Assert.True(result.IsFailure);
        Assert.True(provisioner.RollbackCalled);
        Assert.Contains("Manifest not found", result.Error.Message);
    }
}
