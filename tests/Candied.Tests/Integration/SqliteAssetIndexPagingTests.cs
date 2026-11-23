using Candied.Core.Models;
using Candied.Core.Ports;
using Candied.Decompiler;
using Candied.Primitives;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Candied.Tests.Integration;

public class SqliteAssetIndexPagingTests : IDisposable
{
    private readonly string _tempDir;
    private readonly IAssetIndex _assetIndex;

    public SqliteAssetIndexPagingTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "Candied_AssetPaging_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);

        var services = new ServiceCollection();
        services.AddDecompilerInfrastructure();
        var sp = services.BuildServiceProvider();
        _assetIndex = sp.GetRequiredService<IAssetIndex>();
    }

    [Fact]
    public async Task SearchAssetsPageAsync_ReturnsAccurateTotalMatchesAndPaginatedSlice()
    {
        var dbPath = Path.Combine(_tempDir, "index.db");
        var game = new GameEnvironment(
            Id: new GameId("SampleGame"),
            GameName: "SampleGame",
            GameRootPath: _tempDir,
            ManagedPath: Path.Combine(_tempDir, "Managed"),
            EngineType: UnityEngineType.Mono,
            GameAssemblyPaths: Array.Empty<string>(),
            HasBepInExInstalled: true);

        var assets = Enumerable.Range(1, 15)
            .Select(i => new DiscoveredAssetInfo(
                Name: $"Icon_Item_{i:D2}",
                AssetType: "Sprite",
                SourceContainer: "sharedassets0.assets",
                SourceContainerPath: Path.Combine(_tempDir, "sharedassets0.assets"),
                PathId: 1000 + i,
                Width: 64,
                Height: 64,
                ByteSize: 4096,
                Extension: ".png"))
            .ToList();

        var indexRes = await _assetIndex.IndexAssetsAsync(game, assets, dbPath);
        Assert.True(indexRes.IsSuccess);
        Assert.Equal(15, indexRes.Value);

        var page1 = await _assetIndex.SearchAssetsPageAsync(
            new AssetDiscoveryFilter(SearchPattern: "Icon_Item_", AssetType: "Sprite", MaxResults: 5, Offset: 0),
            dbPath);
        Assert.True(page1.IsSuccess);
        Assert.Equal(15, page1.Value.TotalMatches);
        Assert.Equal(5, page1.Value.Assets.Count);
        Assert.Equal("Icon_Item_01", page1.Value.Assets[0].Name);

        var page2 = await _assetIndex.SearchAssetsPageAsync(
            new AssetDiscoveryFilter(SearchPattern: "Icon_Item_", AssetType: "Sprite", MaxResults: 5, Offset: 5),
            dbPath);
        Assert.True(page2.IsSuccess);
        Assert.Equal(15, page2.Value.TotalMatches);
        Assert.Equal(5, page2.Value.Assets.Count);
        Assert.Equal("Icon_Item_06", page2.Value.Assets[0].Name);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true);
        }
        catch { }
        GC.SuppressFinalize(this);
    }
}
