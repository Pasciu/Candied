using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Candied.Core.Models;
using Candied.Decompiler.Adapters;
using Candied.Primitives;
using Xunit;

namespace Candied.Tests.Unit;

public class AssetExtractorAdapterTests : IDisposable
{
    private readonly string _tempDir;
    private readonly GameEnvironment _sampleGame;

    public AssetExtractorAdapterTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "Candied_Asset_Tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);

        _sampleGame = new GameEnvironment(
            Id: new GameId("DummyGame"),
            GameName: "DummyGame",
            GameRootPath: _tempDir,
            ManagedPath: Path.Combine(_tempDir, "DummyGame_Data", "Managed"),
            EngineType: UnityEngineType.Mono,
            Architecture: "x64",
            TargetFramework: "net472",
            BepInExInfo: new BepInExInstallation(false, null, null),
            DiscoveredAssemblies: new List<DiscoveredAssembly>(),
            EnvironmentWarnings: new List<string>()
        );
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }
        catch { }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task DiscoverAssetsAsync_Returns_Empty_List_When_No_Assets_Present()
    {
        var adapter = new AssetsToolsAssetExtractorAdapter();
        var filter = new AssetDiscoveryFilter();
        var result = await adapter.DiscoverAssetsAsync(_sampleGame, filter);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
    }

    [Fact]
    public async Task ExtractAssetsAsync_Returns_Empty_List_When_No_Assets_Present()
    {
        var adapter = new AssetsToolsAssetExtractorAdapter();
        var filter = new AssetExtractionFilter();
        var outDir = Path.Combine(_tempDir, "assets");
        var result = await adapter.ExtractAssetsAsync(_sampleGame, filter, outDir);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
        Assert.True(Directory.Exists(outDir));
    }
}
