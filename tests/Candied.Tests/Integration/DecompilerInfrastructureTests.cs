using Microsoft.Extensions.DependencyInjection;
using Candied.Core.Models;
using Candied.Core.Ports;
using Candied.Decompiler;
using Candied.Primitives;
using Xunit;

namespace Candied.Tests.Integration;

public class DecompilerInfrastructureTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly IGameDetector _detector;
    private readonly IDecompilerEngine _decompiler;

    public DecompilerInfrastructureTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "Candied_Test_Decomp_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);

        var services = new ServiceCollection();
        services.AddDecompilerInfrastructure();
        var provider = services.BuildServiceProvider();
        _detector = provider.GetRequiredService<IGameDetector>();
        _decompiler = provider.GetRequiredService<IDecompilerEngine>();
    }

    [Fact]
    public async Task DetectGameAsync_Detects_Unity_Mono_Game()
    {
        var gameRoot = Path.Combine(_tempDirectory, "TestGame");
        var managedDir = Path.Combine(gameRoot, "TestGame_Data", "Managed");
        Directory.CreateDirectory(managedDir);

        var bepInExDir = Path.Combine(gameRoot, "BepInEx");
        Directory.CreateDirectory(bepInExDir);

        var primaryDll = Path.Combine(managedDir, "Assembly-CSharp.dll");
        await File.WriteAllTextAsync(primaryDll, "dummy binary content");

        var result = await _detector.DetectGameAsync(gameRoot);

        Assert.True(result.IsSuccess);
        Assert.Equal("TestGame", result.Value.GameName);
        Assert.Equal(UnityEngineType.Mono, result.Value.EngineType);
        Assert.True(result.Value.HasBepInExInstalled);
        Assert.True(result.Value.BepInExInfo.IsInstalled);
        Assert.NotEmpty(result.Value.DiscoveredAssemblies);
        Assert.Equal(AssemblyTier.CoreGame, result.Value.DiscoveredAssemblies[0].Tier);
        Assert.Single(result.Value.GameAssemblyPaths);
    }

    [Fact]
    public async Task DetectGameAsync_Filters_Framework_Assemblies_In_Modular_Game()
    {
        var gameRoot = Path.Combine(_tempDirectory, "ModularGame");
        var managedDir = Path.Combine(gameRoot, "ModularGame_Data", "Managed");
        Directory.CreateDirectory(managedDir);

        // Game assemblies
        await File.WriteAllTextAsync(Path.Combine(managedDir, "ModularGame.Core.dll"), "dummy");
        await File.WriteAllTextAsync(Path.Combine(managedDir, "ModularGame.Scripts.dll"), "dummy");
        await File.WriteAllTextAsync(Path.Combine(managedDir, "GameEngineHelper.dll"), "dummy");

        // Universal framework / engine assemblies to be filtered out
        await File.WriteAllTextAsync(Path.Combine(managedDir, "mscorlib.dll"), "dummy");
        await File.WriteAllTextAsync(Path.Combine(managedDir, "System.dll"), "dummy");
        await File.WriteAllTextAsync(Path.Combine(managedDir, "UnityEngine.dll"), "dummy");
        await File.WriteAllTextAsync(Path.Combine(managedDir, "UnityEngine.UI.dll"), "dummy");
        await File.WriteAllTextAsync(Path.Combine(managedDir, "mcs.dll"), "dummy");
        await File.WriteAllTextAsync(Path.Combine(managedDir, "DOTween.dll"), "dummy");

        var result = await _detector.DetectGameAsync(gameRoot);

        Assert.True(result.IsSuccess);
        Assert.Equal("ModularGame", result.Value.GameName);
        Assert.Equal(3, result.Value.GameAssemblyPaths.Count);
        Assert.EndsWith("ModularGame.Core.dll", result.Value.GameAssemblyPaths[0]);
        Assert.EndsWith("ModularGame.Scripts.dll", result.Value.GameAssemblyPaths[1]);
        Assert.EndsWith("GameEngineHelper.dll", result.Value.GameAssemblyPaths[2]);
    }

    [Fact]
    public async Task DecompileAssemblyAsync_Decompiles_Real_Assembly_To_Source_Files()
    {
        // Use our real compiled Candied.Primitives.dll as the target assembly
        var targetAssembly = typeof(Result<,>).Assembly.Location;
        var outputDir = Path.Combine(_tempDirectory, "DecompiledPrimitives");

        var result = await _decompiler.DecompileAssemblyAsync(targetAssembly, outputDir);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.SourceFileCount >= 1);
        Assert.True(Directory.Exists(outputDir));

        var csFiles = Directory.GetFiles(outputDir, "*.cs", SearchOption.AllDirectories);
        Assert.NotEmpty(csFiles);
    }

    [Fact]
    public async Task DecompileAssemblyAsync_UsesCache_WhenAssemblyUnchanged()
    {
        var targetAssembly = typeof(Result<,>).Assembly.Location;
        var outputDir = Path.Combine(_tempDirectory, "CachedPrimitives");

        // First call: full decompile
        var firstResult = await _decompiler.DecompileAssemblyAsync(targetAssembly, outputDir);
        Assert.True(firstResult.IsSuccess);
        Assert.True(firstResult.Value.SourceFileCount >= 1);

        // Verify cache file was created
        var cacheFile = Path.Combine(outputDir, ".Candied.Primitives.cache");
        Assert.True(File.Exists(cacheFile));

        // Second call: should hit cache
        var secondResult = await _decompiler.DecompileAssemblyAsync(targetAssembly, outputDir);
        Assert.True(secondResult.IsSuccess);
        Assert.Equal(firstResult.Value.SourceFileCount, secondResult.Value.SourceFileCount);
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
