using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Candied.Core.Models;
using Candied.Decompiler.Adapters;
using Candied.Primitives;
using Xunit;

namespace Candied.Tests.Unit;

public class SqliteAssetIndexAdapterTests : IDisposable
{
    private readonly string _tempDbPath;
    private readonly SqliteAssetIndexAdapter _adapter;

    public SqliteAssetIndexAdapterTests()
    {
        _tempDbPath = Path.Combine(Path.GetTempPath(), $"Candied_AssetIndex_Test_{Guid.NewGuid():N}.db");
        _adapter = new SqliteAssetIndexAdapter($"Data Source={_tempDbPath}");
    }

    public void Dispose()
    {
        if (File.Exists(_tempDbPath))
        {
            try { File.Delete(_tempDbPath); } catch { /* best effort cleanup */ }
        }
        GC.SuppressFinalize(this);
    }

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

    [Fact]
    public async Task IndexAssetsAsync_And_SearchAssetsAsync_Persists_And_Finds_Assets()
    {
        var game = CreateSampleGame();
        var sampleAssets = new List<DiscoveredAssetInfo>
        {
            new("Beard_01", "Sprite", "resources.assets", "C:/Games/Data/resources.assets", 1001, 64, 64, 1024, "png"),
            new("Face_Walter", "Sprite", "sharedassets1.assets", "C:/Games/Data/sharedassets1.assets", 1002, 128, 128, 2048, "png"),
            new("Dialogue_Quest1", "TextAsset", "resources.assets", "C:/Games/Data/resources.assets", 1003, 0, 0, 512, "json"),
            new("Config_GameSettings", "MonoBehaviour", "sharedassets2.assets", "C:/Games/Data/sharedassets2.assets", 1004, 0, 0, 4096, "json")
        };

        // 1. Initial state
        var hasInitially = await _adapter.HasIndexedAssetsAsync(_tempDbPath);
        Assert.True(hasInitially.IsSuccess);
        Assert.False(hasInitially.Value);

        // 2. Index assets
        var indexResult = await _adapter.IndexAssetsAsync(game, sampleAssets, _tempDbPath);
        Assert.True(indexResult.IsSuccess);
        Assert.Equal(4, indexResult.Value);

        // 3. Verify HasIndexed
        var hasAfter = await _adapter.HasIndexedAssetsAsync(_tempDbPath);
        Assert.True(hasAfter.IsSuccess);
        Assert.True(hasAfter.Value);

        // 4. Search by name pattern
        var searchPattern = await _adapter.SearchAssetsAsync(
            new AssetDiscoveryFilter(SearchPattern: "Walter"), _tempDbPath);
        Assert.True(searchPattern.IsSuccess);
        Assert.Single(searchPattern.Value);
        Assert.Equal("Face_Walter", searchPattern.Value[0].Name);

        // 5. Search by AssetType
        var searchType = await _adapter.SearchAssetsAsync(
            new AssetDiscoveryFilter(AssetType: "TextAsset"), _tempDbPath);
        Assert.True(searchType.IsSuccess);
        Assert.Single(searchType.Value);
        Assert.Equal("Dialogue_Quest1", searchType.Value[0].Name);

        // 6. Search by ContainerName
        var searchContainer = await _adapter.SearchAssetsAsync(
            new AssetDiscoveryFilter(ContainerName: "sharedassets1"), _tempDbPath);
        Assert.True(searchContainer.IsSuccess);
        Assert.Single(searchContainer.Value);
        Assert.Equal("Face_Walter", searchContainer.Value[0].Name);

        // 7. Search by PathId
        var searchPathId = await _adapter.SearchAssetsAsync(
            new AssetDiscoveryFilter(PathId: 1004), _tempDbPath);
        Assert.True(searchPathId.IsSuccess);
        Assert.Single(searchPathId.Value);
        Assert.Equal("Config_GameSettings", searchPathId.Value[0].Name);
    }
}
