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

public class AssetUseCaseTests
{
    private static GameEnvironment CreateSampleGame() => new(
        Id: new GameId("TestGame"),
        GameName: "TestGame",
        GameRootPath: "C:/Games/TestGame",
        ManagedPath: "C:/Games/TestGame/Managed",
        EngineType: UnityEngineType.Mono,
        Architecture: "x64",
        TargetFramework: "net472",
        BepInExInfo: new BepInExInstallation(false, null, null),
        DiscoveredAssemblies: new List<DiscoveredAssembly>(),
        EnvironmentWarnings: new List<string>()
    );

    private sealed class FakeAssetExtractor : IAssetExtractor
    {
        public bool FailDiscover { get; set; }
        public bool FailExtract { get; set; }

        public Task<Result<IReadOnlyList<DiscoveredAssetInfo>, AssetExtractionError>> DiscoverAssetsAsync(
            GameEnvironment game,
            AssetDiscoveryFilter filter,
            CancellationToken ct = default)
        {
            if (FailDiscover)
            {
                return Task.FromResult(Result.Fail<IReadOnlyList<DiscoveredAssetInfo>, AssetExtractionError>(
                    new AssetExtractionError("Disk read error")));
            }

            IReadOnlyList<DiscoveredAssetInfo> list =
            [
                new DiscoveredAssetInfo("Beard_01", "Sprite", "resources.assets", "C:/Games/Data/resources.assets", 1, 64, 64, 2048, "png"),
                new DiscoveredAssetInfo("Face_02", "Sprite", "sharedassets1.assets", "C:/Games/Data/sharedassets1.assets", 2, 128, 128, 4096, "png")
            ];

            return Task.FromResult(Result.Ok<IReadOnlyList<DiscoveredAssetInfo>, AssetExtractionError>(list));
        }

        public Task<Result<IReadOnlyList<ExtractedAssetInfo>, AssetExtractionError>> ExtractAssetsAsync(
            GameEnvironment game,
            AssetExtractionFilter filter,
            string outputDirectory,
            CancellationToken ct = default)
        {
            if (FailExtract)
            {
                return Task.FromResult(Result.Fail<IReadOnlyList<ExtractedAssetInfo>, AssetExtractionError>(
                    new AssetExtractionError("Disk write error")));
            }

            IReadOnlyList<ExtractedAssetInfo> list =
            [
                new ExtractedAssetInfo("Beard_01", "Sprite", $"{outputDirectory}/Beard_01.png", 2048, 64, 64, 1, "resources.assets")
            ];

            return Task.FromResult(Result.Ok<IReadOnlyList<ExtractedAssetInfo>, AssetExtractionError>(list));
        }
    }

    [Fact]
    public async Task DiscoverAssetsUseCase_Returns_Discovered_Assets()
    {
        var extractor = new FakeAssetExtractor();
        var useCase = new DiscoverAssetsUseCase(extractor);

        var result = await useCase.ExecuteAsync(CreateSampleGame(), new AssetDiscoveryFilter("Beard"));

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Count);
        Assert.Equal("Beard_01", result.Value[0].Name);
    }

    [Fact]
    public async Task DiscoverAssetsUseCase_Returns_Error_When_Game_Is_Null()
    {
        var extractor = new FakeAssetExtractor();
        var useCase = new DiscoverAssetsUseCase(extractor);

        var result = await useCase.ExecuteAsync(null!);

        Assert.True(result.IsFailure);
        Assert.Equal("Game environment cannot be null.", result.Error.Message);
    }

    [Fact]
    public async Task ExtractAssetsUseCase_Uses_StorageLayout_Default_Path_When_Output_Not_Specified()
    {
        var extractor = new FakeAssetExtractor();
        var layout = new CandiedStorageLayout("C:/MyProject");
        var useCase = new ExtractAssetsUseCase(extractor, layout);

        var result = await useCase.ExecuteAsync(CreateSampleGame(), new AssetExtractionFilter("Beard_01"));

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value);
        Assert.Contains("assets/Beard_01.png", result.Value[0].OutputFilePath);
    }

    [Fact]
    public async Task ExtractAssetsUseCase_Returns_Error_When_Game_Is_Null()
    {
        var extractor = new FakeAssetExtractor();
        var layout = new CandiedStorageLayout("C:/MyProject");
        var useCase = new ExtractAssetsUseCase(extractor, layout);

        var result = await useCase.ExecuteAsync(null!);

        Assert.True(result.IsFailure);
        Assert.Equal("Game environment cannot be null.", result.Error.Message);
    }
}
