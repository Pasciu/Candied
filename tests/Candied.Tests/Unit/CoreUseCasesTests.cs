using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Candied.Core.Capabilities;
using Candied.Core.Errors;
using Candied.Core.Models;
using Candied.Core.Ports;
using Candied.Core.UseCases;
using Candied.Primitives;
using Xunit;

namespace Candied.Tests.Unit;

public sealed class CoreUseCasesTests
{
    private sealed class MinimalTargetPlatform : ITargetPlatform
    {
        public string PlatformId => "test.minimal";
        public string DisplayName => "Minimal Test Platform";
        public int Priority => 50;

        public IEnvironmentSetupCapability? EnvironmentSetup => null;
        public ICodeKnowledgeCapability? CodeKnowledge => null;
        public IAssetPipelineCapability? AssetPipeline => null;
        public IModWorkspaceCapability? ModWorkspace => null;
        public IRuntimeLogCapability? RuntimeLogs => null;

        public bool CanHandle(string targetPath, string? runnerPath = null) => true;

        public Task<Result<TargetDescriptor, DetectionError>> InspectAsync(
            string targetPath,
            string? runnerPath,
            CandiedStorageLayout storageLayout,
            CancellationToken ct = default)
        {
            var desc = new TargetDescriptor(
                Id: new TargetId("test-rom"),
                Name: "TestRom",
                TargetPath: targetPath,
                RunnerPath: runnerPath,
                PlatformId: PlatformId,
                PlatformDisplayName: DisplayName,
                Architecture: "mips",
                EngineVersion: "1.0",
                RuntimeFramework: null,
                ModLoader: new ModLoaderStatus(false),
                Capabilities: [],
                CodeModules: [],
                Metadata: new Dictionary<string, string>(),
                Warnings: []);
            return Task.FromResult(Result.Ok<TargetDescriptor, DetectionError>(desc));
        }
    }

    private sealed class FakePlatformRegistry(ITargetPlatform platform) : ITargetPlatformRegistry
    {
        public IReadOnlyList<ITargetPlatform> GetAllPlatforms() => [platform];
        public ITargetPlatform? GetPlatformById(string platformId) => platform.PlatformId == platformId ? platform : null;
        public ITargetPlatform ResolvePlatform(string targetPath, string? runnerPath = null) => platform;
        public ITargetPlatform? ResolveModBuildPlatform(string projectOrManifestPath) => platform.ModWorkspace != null ? platform : null;

        public Task<Result<TargetDescriptor, DetectionError>> InspectTargetAsync(
            string targetPath,
            string? runnerPath,
            CandiedStorageLayout storageLayout,
            CancellationToken ct = default) =>
            platform.InspectAsync(targetPath, runnerPath, storageLayout, ct);
    }

    [Fact]
    public async Task InspectTargetUseCase_RejectsEmptyPath_AndSucceedsForValidTarget()
    {
        var registry = new FakePlatformRegistry(new MinimalTargetPlatform());
        var useCase = new InspectTargetUseCase(registry);
        var layout = new CandiedStorageLayout("C:/Workspace");

        var emptyRes = await useCase.ExecuteAsync("", null, layout);
        Assert.True(emptyRes.IsFailure);

        var okRes = await useCase.ExecuteAsync("C:/Roms/Crash3.iso", "C:/Emu/pcsx2.exe", layout);
        Assert.True(okRes.IsSuccess);
        Assert.Equal("TestRom", okRes.Value.Name);
        Assert.Equal("C:/Emu/pcsx2.exe", okRes.Value.RunnerPath);
    }

    [Fact]
    public async Task CapabilityUseCases_ReturnUnsupportedCapabilityError_WhenPlatformLacksTrait()
    {
        var registry = new FakePlatformRegistry(new MinimalTargetPlatform());
        var layout = new CandiedStorageLayout("C:/Workspace");

        var setupUseCase = new SetupTargetEnvironmentUseCase(registry);
        var setupRes = await setupUseCase.ExecuteAsync("C:/Roms/Game.iso", null, new ProvisioningSpec(), layout);
        Assert.True(setupRes.IsFailure);
        Assert.IsType<UnsupportedCapabilityError>(setupRes.Error);

        var rollbackUseCase = new RollbackTargetEnvironmentUseCase(registry);
        var rbRes = await rollbackUseCase.ExecuteAsync("C:/Roms/Game.iso", null, layout);
        Assert.True(rbRes.IsFailure);
        Assert.IsType<UnsupportedCapabilityError>(rbRes.Error);

        var extractAssetsUseCase = new ExtractAssetsUseCase(registry);
        var assetRes = await extractAssetsUseCase.ExecuteAsync("C:/Roms/Game.iso", null, new AssetExtractionFilter(), layout);
        Assert.True(assetRes.IsFailure);
        Assert.IsType<UnsupportedCapabilityError>(assetRes.Error);
    }
}
