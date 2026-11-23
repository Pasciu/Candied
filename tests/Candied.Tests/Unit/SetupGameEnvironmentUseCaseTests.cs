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

public class SetupGameEnvironmentUseCaseTests
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
                BepInExInfo: new BepInExInstallation(false, null, null),
                DiscoveredAssemblies: new List<DiscoveredAssembly>(),
                EnvironmentWarnings: new List<string>()
            );

            return Task.FromResult(Result.Ok<GameEnvironment, DetectionError>(env));
        }
    }

    private sealed class FakeProvisioner : IEnvironmentProvisioner
    {
        public bool ShouldFail { get; set; }
        public bool ProvisionCalled { get; private set; }

        public Task<Result<ProvisioningReport, ProvisioningError>> ProvisionEnvironmentAsync(
            GameEnvironment gameEnv,
            ProvisioningSpec spec,
            CandiedStorageLayout storageLayout,
            CancellationToken ct = default)
        {
            ProvisionCalled = true;
            if (ShouldFail)
            {
                return Task.FromResult(Result.Fail<ProvisioningReport, ProvisioningError>(new ProvisioningError("Download failed")));
            }

            return Task.FromResult(Result.Ok<ProvisioningReport, ProvisioningError>(
                new ProvisioningReport(true, "BepInEx", "5.4.23.2", "x64", 12, true, $"{gameEnv.GameRootPath}/.candied_manifest.json")));
        }

        public Task<Result<RollbackReport, ProvisioningError>> RollbackEnvironmentAsync(
            GameEnvironment gameEnv,
            CandiedStorageLayout storageLayout,
            CancellationToken ct = default) =>
            throw new NotImplementedException();
    }

    [Fact]
    public async Task ExecuteAsync_Provisions_Environment_On_Success()
    {
        var detector = new FakeGameDetector();
        var provisioner = new FakeProvisioner();
        var layout = new CandiedStorageLayout("C:/Workspace");
        var useCase = new SetupGameEnvironmentUseCase(detector, provisioner);

        var result = await useCase.ExecuteAsync("C:/Games/TestGame", new ProvisioningSpec(), layout);

        Assert.True(result.IsSuccess);
        Assert.True(provisioner.ProvisionCalled);
        Assert.Equal("BepInEx", result.Value.ModLoaderName);
        Assert.Equal("5.4.23.2", result.Value.Version);
        Assert.Equal("x64", result.Value.Architecture);
        Assert.Equal(12, result.Value.FilesInstalledCount);
    }

    [Fact]
    public async Task ExecuteAsync_Fails_When_Detector_Fails()
    {
        var detector = new FakeGameDetector { ShouldFail = true };
        var provisioner = new FakeProvisioner();
        var layout = new CandiedStorageLayout("C:/Workspace");
        var useCase = new SetupGameEnvironmentUseCase(detector, provisioner);

        var result = await useCase.ExecuteAsync("C:/Invalid/Dir", new ProvisioningSpec(), layout);

        Assert.True(result.IsFailure);
        Assert.False(provisioner.ProvisionCalled);
    }
}
