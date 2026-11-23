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

public class IndexAssetsUseCaseTests
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
        public Task<Result<IReadOnlyList<DiscoveredAssetInfo>, AssetExtractionError>> DiscoverAssetsAsync(
            GameEnvironment game,
            AssetDiscoveryFilter filter,
            CancellationToken ct = default)
        {
            IReadOnlyList<DiscoveredAssetInfo> list =
            [
                new DiscoveredAssetInfo("Beard_01", "Sprite", "resources.assets", "C:/resources.assets", 1, 64, 64, 100, "png"),
                new DiscoveredAssetInfo("Config_01", "MonoBehaviour", "sharedassets0.assets", "C:/sharedassets0.assets", 2, 0, 0, 200, "json")
            ];
            return Task.FromResult(Result.Ok<IReadOnlyList<DiscoveredAssetInfo>, AssetExtractionError>(list));
        }

        public Task<Result<IReadOnlyList<ExtractedAssetInfo>, AssetExtractionError>> ExtractAssetsAsync(
            GameEnvironment game,
            AssetExtractionFilter filter,
            string outputDirectory,
            CancellationToken ct = default)
        {
            return Task.FromResult(Result.Ok<IReadOnlyList<ExtractedAssetInfo>, AssetExtractionError>(new List<ExtractedAssetInfo>()));
        }
    }

    private sealed class FakeAssetIndex : IAssetIndex
    {
        public readonly List<DiscoveredAssetInfo> IndexedAssets = new();

        public Task<Result<int, IndexError>> IndexAssetsAsync(
            GameEnvironment game,
            IReadOnlyList<DiscoveredAssetInfo> assets,
            string? databasePath = null,
            CancellationToken ct = default)
        {
            IndexedAssets.AddRange(assets);
            return Task.FromResult(Result.Ok<int, IndexError>(assets.Count));
        }

        public Task<Result<IReadOnlyList<DiscoveredAssetInfo>, IndexError>> SearchAssetsAsync(
            AssetDiscoveryFilter filter,
            string? databasePath = null,
            CancellationToken ct = default)
        {
            return Task.FromResult(Result.Ok<IReadOnlyList<DiscoveredAssetInfo>, IndexError>((IReadOnlyList<DiscoveredAssetInfo>)IndexedAssets));
        }

        public Task<Result<AssetDiscoveryPage, IndexError>> SearchAssetsPageAsync(
            AssetDiscoveryFilter filter,
            string? databasePath = null,
            CancellationToken ct = default)
        {
            return Task.FromResult(Result.Ok<AssetDiscoveryPage, IndexError>(
                new AssetDiscoveryPage(IndexedAssets.Count, IndexedAssets)));
        }

        public Task<Result<bool, IndexError>> HasIndexedAssetsAsync(
            string? databasePath = null,
            CancellationToken ct = default)
        {
            return Task.FromResult(Result.Ok<bool, IndexError>(IndexedAssets.Count > 0));
        }
    }

    [Fact]
    public async Task IndexAssetsUseCase_Executes_Full_Scan_And_Indexes_To_Port()
    {
        var extractor = new FakeAssetExtractor();
        var index = new FakeAssetIndex();
        var useCase = new IndexAssetsUseCase(extractor, index);

        var result = await useCase.ExecuteAsync(CreateSampleGame());

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value);
        Assert.Equal(2, index.IndexedAssets.Count);
    }

    [Fact]
    public async Task DiscoverAssetsUseCase_When_Already_Indexed_Uses_Index_Port()
    {
        var extractor = new FakeAssetExtractor();
        var index = new FakeAssetIndex();
        index.IndexedAssets.Add(new DiscoveredAssetInfo("Cached_Asset", "Sprite", "res.assets", "C:/res.assets", 99, 32, 32, 50, "png"));

        var useCase = new DiscoverAssetsUseCase(extractor, index);
        var result = await useCase.ExecuteAsync(CreateSampleGame(), new AssetDiscoveryFilter());

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value);
        Assert.Equal("Cached_Asset", result.Value[0].Name);
    }
}
