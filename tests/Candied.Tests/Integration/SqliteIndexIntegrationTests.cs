using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Candied.Core.Models;
using Candied.Core.Ports;
using Candied.Infrastructure;
using Candied.Primitives;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Candied.Tests.Integration;

public sealed class SqliteIndexIntegrationTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ServiceProvider _serviceProvider;
    private readonly ISymbolIndex _symbolIndex;
    private readonly IAssetIndex _assetIndex;

    public SqliteIndexIntegrationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "Candied_SqliteTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);

        var services = new ServiceCollection();
        services.AddCandiedCoreAndInfrastructure(_tempDir);
        _serviceProvider = services.BuildServiceProvider();
        _symbolIndex = _serviceProvider.GetRequiredService<ISymbolIndex>();
        _assetIndex = _serviceProvider.GetRequiredService<IAssetIndex>();
    }

    public void Dispose()
    {
        _serviceProvider.Dispose();
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }
        catch
        {
        }
    }

    [Fact]
    public async Task SqliteSymbolIndex_IndexesCSharpAndNonCSharpSymbols_SupportsFts5AndModuleFilter()
    {
        string dbPath = Path.Combine(_tempDir, "index.db");
        string srcDir = Path.Combine(_tempDir, "SampleModule");
        Directory.CreateDirectory(srcDir);

        string code = """
            namespace SampleGame.Actors;

            public enum ActorMood { Calm, Happy }

            public class ActorController
            {
                private ActorMood _mood = ActorMood.Calm;
                public void RefreshMood()
                {
                    ApplyShaderColor("_TintColor", _mood.ToString());
                }
                private void ApplyShaderColor(string prop, string mood) {}
            }
            """;
        await File.WriteAllTextAsync(Path.Combine(srcDir, "ActorController.cs"), code);

        var idxRes = await _symbolIndex.IndexCSharpSourceTreeAsync(srcDir, "SampleModule", dbPath);
        Assert.True(idxRes.IsSuccess);

        // Also index raw extracted symbols from a hypothetical C++/Ghidra or Lua module!
        var nativeSym = new SymbolSummary("Crash_SpinAttack", SymbolKind.Method, "Ghidra.Global", "SLUS_007.27", "void Crash_SpinAttack(Actor* self)", "funcs/Crash_SpinAttack.c");
        var nativeType = new TypeDefinitionDetail("Ghidra.Global.Actor", "Ghidra.Global", null, [], ["int health"], ["void Crash_SpinAttack(Actor* self)"], "struct Actor { int health; };", "SLUS_007.27", "types/Actor.h");
        var rawIdxRes = await _symbolIndex.IndexExtractedSymbolsAsync("SLUS_007.27", [nativeSym], [nativeType], dbPath);
        Assert.True(rawIdxRes.IsSuccess);

        // Filter by module
        var ghidraSearch = await _symbolIndex.SearchSymbolsAsync(new SymbolQuery("SpinAttack", ModuleFilter: "SLUS_007.27"), dbPath);
        Assert.True(ghidraSearch.IsSuccess);
        Assert.Single(ghidraSearch.Value);
        Assert.Equal("Crash_SpinAttack", ghidraSearch.Value[0].FullName);

        // EnumOnly filter on C# module
        var enumSearch = await _symbolIndex.SearchSymbolsAsync(new SymbolQuery("Actor", SymbolKindFilter.TypeOnly, EnumOnly: true), dbPath);
        Assert.True(enumSearch.IsSuccess);
        Assert.Single(enumSearch.Value);
        Assert.Equal("SampleGame.Actors.ActorMood", enumSearch.Value[0].FullName);

        // FTS5 code search + auto-backfill when FTS table is dropped
        await using (var conn = new SqliteConnection($"Data Source={dbPath};Pooling=False"))
        {
            await conn.OpenAsync();
            await using var dropCmd = conn.CreateCommand();
            dropCmd.CommandText = "DROP TABLE IF EXISTS type_source_fts;";
            await dropCmd.ExecuteNonQueryAsync();
        }

        var codeSearch = await _symbolIndex.SearchCodeAsync(new CodeSearchQuery("ApplyShaderColor(\"_TintColor\"", ModuleFilter: "SampleModule"), dbPath);
        Assert.True(codeSearch.IsSuccess);
        Assert.Single(codeSearch.Value);
        Assert.Equal("SampleGame.Actors.ActorController", codeSearch.Value[0].FullName);
    }

    [Fact]
    public async Task SqliteAssetIndex_SupportsPaginationAndAssetKeyFiltering()
    {
        string dbPath = Path.Combine(_tempDir, "assets.db");
        var target = new TargetDescriptor(
            Id: new TargetId("Sample"),
            Name: "Sample",
            TargetPath: _tempDir,
            RunnerPath: null,
            PlatformId: "unity.mono",
            PlatformDisplayName: "Unity",
            Architecture: "x64",
            EngineVersion: "2022.3",
            RuntimeFramework: "net472",
            ModLoader: new ModLoaderStatus(true),
            Capabilities: [],
            CodeModules: [],
            Metadata: new Dictionary<string, string>(),
            Warnings: []);

        var assets = Enumerable.Range(1, 15)
            .Select(i => new DiscoveredAssetInfo(
                Name: $"Icon_Item_{i:D2}",
                AssetType: "Sprite",
                SourceContainer: "sharedassets0.assets",
                SourceContainerPath: Path.Combine(_tempDir, "sharedassets0.assets"),
                AssetKey: 1000 + i,
                Width: 64,
                Height: 64,
                ByteSize: 4096,
                Extension: ".png"))
            .ToList();

        var indexRes = await _assetIndex.IndexAssetsAsync(target, assets, dbPath);
        Assert.True(indexRes.IsSuccess);
        Assert.Equal(15, indexRes.Value);

        var page1 = await _assetIndex.SearchAssetsPageAsync(
            new AssetDiscoveryFilter(SearchPattern: "Icon_Item_", AssetType: "Sprite", MaxResults: 5, Offset: 0),
            dbPath);
        Assert.True(page1.IsSuccess);
        Assert.Equal(15, page1.Value.TotalMatches);
        Assert.Equal(5, page1.Value.Assets.Count);
        Assert.Equal("Icon_Item_01", page1.Value.Assets[0].Name);

        var exactKey = await _assetIndex.SearchAssetsPageAsync(
            new AssetDiscoveryFilter(AssetKey: 1007),
            dbPath);
        Assert.True(exactKey.IsSuccess);
        Assert.Single(exactKey.Value.Assets);
        Assert.Equal("Icon_Item_07", exactKey.Value.Assets[0].Name);
    }
}
