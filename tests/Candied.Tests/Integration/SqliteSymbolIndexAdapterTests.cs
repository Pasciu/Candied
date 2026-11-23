using Microsoft.Extensions.DependencyInjection;
using Candied.Core.Models;
using Candied.Core.Ports;
using Candied.Decompiler;
using Xunit;

namespace Candied.Tests.Integration;

public class SqliteSymbolIndexAdapterTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly ISymbolIndex _symbolIndex;

    public SqliteSymbolIndexAdapterTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "Candied_Test_Index_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);

        var services = new ServiceCollection();
        services.AddDecompilerInfrastructure();
        var provider = services.BuildServiceProvider();
        _symbolIndex = provider.GetRequiredService<ISymbolIndex>();
    }

    [Fact]
    public async Task Index_And_Search_Symbols_From_Source_Files()
    {
        // 1. Create a dummy C# file in temp dir
        var code = @"
namespace MyGame.Combat;

public class WeaponController
{
    public float damage = 25f;
    public void Attack(float power) {}
}
";
        var srcFile = Path.Combine(_tempDirectory, "WeaponController.cs");
        await File.WriteAllTextAsync(srcFile, code);

        // 2. Index directory
        var indexResult = await _symbolIndex.IndexSourceTreeAsync(_tempDirectory);
        Assert.True(indexResult.IsSuccess);

        // 3. Search for WeaponController
        var searchResult = await _symbolIndex.SearchSymbolsAsync(new SymbolQuery("WeaponController"));
        Assert.True(searchResult.IsSuccess);
        Assert.NotEmpty(searchResult.Value);

        var classSym = searchResult.Value.First(s => s.Kind == SymbolKind.Class);
        Assert.Equal("MyGame.Combat.WeaponController", classSym.FullName);

        // 4. Search for Attack method with MemberOnly filter
        var methodSearchResult = await _symbolIndex.SearchSymbolsAsync(
            new SymbolQuery("Attack", SymbolKindFilter.MemberOnly));
        Assert.True(methodSearchResult.IsSuccess);
        Assert.Contains(methodSearchResult.Value, s => s.Kind == SymbolKind.Method);

        // 5. Retrieve Type Details
        var detailResult = await _symbolIndex.GetTypeDetailAsync("MyGame.Combat.WeaponController");
        Assert.True(detailResult.IsSuccess);
        Assert.Equal("MyGame.Combat.WeaponController", detailResult.Value.FullName);
        Assert.Contains(detailResult.Value.FieldSignatures, f => f.Contains("damage"));
        Assert.Contains(detailResult.Value.MethodSignatures, m => m.Contains("Attack"));
    }

    [Fact]
    public async Task IndexSourceTreeAsync_WhenUnchanged_ReusesCache_And_WhenNewFileAdded_UpdatesIncrementalIndex()
    {
        // 1. Initial file
        var code1 = @"
namespace MyGame.Combat;
public class SwordController { public void Slash() {} }
";
        await File.WriteAllTextAsync(Path.Combine(_tempDirectory, "SwordController.cs"), code1);

        var indexResult1 = await _symbolIndex.IndexSourceTreeAsync(_tempDirectory);
        Assert.True(indexResult1.IsSuccess);

        var search1 = await _symbolIndex.SearchSymbolsAsync(new SymbolQuery("SwordController", SymbolKindFilter.TypeOnly));
        Assert.True(search1.IsSuccess);
        Assert.Single(search1.Value);
        Assert.Equal("MyGame.Combat.SwordController", search1.Value[0].FullName);

        // 2. Second index call without changes (cache hit)
        var indexResult2 = await _symbolIndex.IndexSourceTreeAsync(_tempDirectory);
        Assert.True(indexResult2.IsSuccess);

        var search2 = await _symbolIndex.SearchSymbolsAsync(new SymbolQuery("SwordController", SymbolKindFilter.TypeOnly));
        Assert.True(search2.IsSuccess);
        Assert.Single(search2.Value);
        Assert.Equal("MyGame.Combat.SwordController", search2.Value[0].FullName);

        // 3. Add second file (cache miss / incremental update)
        var code2 = @"
namespace MyGame.Armor;
public class ShieldController { public void Block() {} }
";
        await File.WriteAllTextAsync(Path.Combine(_tempDirectory, "ShieldController.cs"), code2);

        var indexResult3 = await _symbolIndex.IndexSourceTreeAsync(_tempDirectory);
        Assert.True(indexResult3.IsSuccess);

        var searchSword = await _symbolIndex.SearchSymbolsAsync(new SymbolQuery("SwordController", SymbolKindFilter.TypeOnly));
        Assert.True(searchSword.IsSuccess);
        Assert.Single(searchSword.Value);

        var searchShield = await _symbolIndex.SearchSymbolsAsync(new SymbolQuery("ShieldController", SymbolKindFilter.TypeOnly));
        Assert.True(searchShield.IsSuccess);
        Assert.Single(searchShield.Value);
    }

    [Fact]
    public async Task IndexSourceTreeAsync_Multiple_Assemblies_Preserves_All_Symbols_And_GetAllSymbols_Returns_Them()
    {
        var dbPath = Path.Combine(_tempDirectory, "index.db");

        // 1. Assembly A folder
        var dirA = Path.Combine(_tempDirectory, "AssemblyA");
        Directory.CreateDirectory(dirA);
        await File.WriteAllTextAsync(Path.Combine(dirA, "Player.cs"), @"
namespace Game.Core;
public class Player { public void Jump() {} }
");

        // 2. Assembly B folder
        var dirB = Path.Combine(_tempDirectory, "AssemblyB");
        Directory.CreateDirectory(dirB);
        await File.WriteAllTextAsync(Path.Combine(dirB, "Inventory.cs"), @"
namespace Game.Items;
public class Inventory { public void AddItem() {} }
");

        // Index Assembly A
        var indexResA = await _symbolIndex.IndexSourceTreeAsync(dirA, "AssemblyA", dbPath);
        Assert.True(indexResA.IsSuccess);

        // Index Assembly B
        var indexResB = await _symbolIndex.IndexSourceTreeAsync(dirB, "AssemblyB", dbPath);
        Assert.True(indexResB.IsSuccess);

        // Verify both assemblies exist in GetAllSymbols
        var allSymbols = await _symbolIndex.GetAllSymbolsAsync(dbPath);
        Assert.True(allSymbols.IsSuccess);
        Assert.Contains(allSymbols.Value, s => s.AssemblyName == "AssemblyA" && s.FullName == "Game.Core.Player");
        Assert.Contains(allSymbols.Value, s => s.AssemblyName == "AssemblyB" && s.FullName == "Game.Items.Inventory");

        // Re-indexing Assembly A should not wipe Assembly B
        await File.WriteAllTextAsync(Path.Combine(dirA, "Player.cs"), @"
namespace Game.Core;
public class Player { public void Jump() {} public void Dash() {} }
");
        var reindexA = await _symbolIndex.IndexSourceTreeAsync(dirA, "AssemblyA", dbPath);
        Assert.True(reindexA.IsSuccess);

        var allSymbolsAfter = await _symbolIndex.GetAllSymbolsAsync(dbPath);
        Assert.True(allSymbolsAfter.IsSuccess);
        Assert.Contains(allSymbolsAfter.Value, s => s.AssemblyName == "AssemblyA" && s.FullName == "Game.Core.Player");
        Assert.Contains(allSymbolsAfter.Value, s => s.AssemblyName == "AssemblyB" && s.FullName == "Game.Items.Inventory");
    }

    [Fact]
    public async Task SearchSymbols_SupportsExactKindAndEnumOnly_AndSearchCodeFindsSourceMatches()
    {
        var dbPath = Path.Combine(_tempDirectory, "fts_index.db");
        var srcDir = Path.Combine(_tempDirectory, "SampleAssembly");
        Directory.CreateDirectory(srcDir);

        var code = """
            namespace SampleGame.Actors;

            public enum ActorMood
            {
                Calm,
                Happy,
                Grumpy
            }

            public interface IActorRig
            {
                void RefreshMood();
            }

            public class ActorController : IActorRig
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

        var idx = await _symbolIndex.IndexSourceTreeAsync(srcDir, "SampleAssembly", dbPath);
        Assert.True(idx.IsSuccess);

        // EnumOnly filter
        var enumRes = await _symbolIndex.SearchSymbolsAsync(
            new SymbolQuery("Actor", SymbolKindFilter.TypeOnly, EnumOnly: true), dbPath);
        Assert.True(enumRes.IsSuccess);
        Assert.Single(enumRes.Value);
        Assert.Equal("SampleGame.Actors.ActorMood", enumRes.Value[0].FullName);

        // ExactKind interface filter
        var ifaceRes = await _symbolIndex.SearchSymbolsAsync(
            new SymbolQuery("Actor", SymbolKindFilter.TypeOnly, ExactKind: SymbolKind.Interface), dbPath);
        Assert.True(ifaceRes.IsSuccess);
        Assert.Single(ifaceRes.Value);
        Assert.Equal("SampleGame.Actors.IActorRig", ifaceRes.Value[0].FullName);

        // TypeDetail exposes AssemblyName & RelativeSourcePath
        var detailRes = await _symbolIndex.GetTypeDetailAsync("SampleGame.Actors.ActorController", dbPath);
        Assert.True(detailRes.IsSuccess);
        Assert.Equal("SampleAssembly", detailRes.Value.AssemblyName);
        Assert.Equal("ActorController.cs", detailRes.Value.RelativeSourcePath);

        // SearchCodeAsync finds call site with punctuation via FTS5 + exact check
        var codeRes = await _symbolIndex.SearchCodeAsync(
            new CodeSearchQuery("ApplyShaderColor(\"_TintColor\""), dbPath);
        Assert.True(codeRes.IsSuccess);
        Assert.Single(codeRes.Value);
        Assert.Equal("SampleGame.Actors.ActorController", codeRes.Value[0].FullName);
        Assert.True(codeRes.Value[0].LineNumber > 0);
        Assert.Contains("ApplyShaderColor", codeRes.Value[0].Snippet);

        // Simulate pre-existing DB without FTS5 table -> SearchCodeAsync auto-backfills FTS5!
        await using (var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath};Pooling=False"))
        {
            await conn.OpenAsync();
            await using var dropCmd = conn.CreateCommand();
            dropCmd.CommandText = "DROP TABLE IF EXISTS type_source_fts;";
            await dropCmd.ExecuteNonQueryAsync();
        }

        var backfillRes = await _symbolIndex.SearchCodeAsync(
            new CodeSearchQuery("ApplyShaderColor(\"_TintColor\""), dbPath);
        Assert.True(backfillRes.IsSuccess);
        Assert.Single(backfillRes.Value);
        Assert.Equal("SampleGame.Actors.ActorController", backfillRes.Value[0].FullName);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, true);
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
        GC.SuppressFinalize(this);
    }
}
