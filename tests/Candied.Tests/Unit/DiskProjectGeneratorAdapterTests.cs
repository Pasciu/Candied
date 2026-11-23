using Candied.Core.Models;
using Candied.Primitives;
using Candied.Workspace.Adapters;
using Xunit;

namespace Candied.Tests.Unit;

public sealed class DiskProjectGeneratorAdapterTests : IDisposable
{
    private static readonly string[] PublicizedAssemblies = ["Assembly-CSharp"];
    private readonly string _tempDir;

    public DiskProjectGeneratorAdapterTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "Candied_ScaffoldTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch
        {
            // Ignore cleanup errors
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task ScaffoldModProject_Creates_Csproj_Plugin_And_PatchTemplate()
    {
        // Arrange
        var adapter = new DiskProjectGeneratorAdapter();
        var gameRoot = Path.Combine(_tempDir, "Game");
        var managedDir = Path.Combine(gameRoot, "Game_Data", "Managed");
        Directory.CreateDirectory(managedDir);

        var assemblyPath = Path.Combine(managedDir, "Assembly-CSharp.dll");
        await File.WriteAllBytesAsync(assemblyPath, new byte[] { 0x4D, 0x5A }); // mock dll

        var targetGame = new GameEnvironment(
            Id: new GameId("test-game"),
            GameName: "TestGame",
            GameRootPath: gameRoot,
            ManagedPath: managedDir,
            EngineType: UnityEngineType.Mono,
            GameAssemblyPaths: new[] { assemblyPath },
            HasBepInExInstalled: true
        );

        var spec = new ModScaffoldSpec(
            ModName: "SpeedMod",
            PluginGuid: new PluginGuid("com.author.speedmod"),
            Version: "1.2.0",
            OutputDirectory: _tempDir,
            TargetGame: targetGame,
            PublicizedAssemblyNames: PublicizedAssemblies
        );

        // Act
        var result = await adapter.ScaffoldModProjectAsync(spec);

        // Assert
        Assert.True(result.IsSuccess);
        var manifest = result.Value;

        Assert.True(File.Exists(manifest.ProjectFilePath));
        Assert.True(File.Exists(manifest.MainPluginSourcePath));

        var modDir = Path.GetDirectoryName(manifest.ProjectFilePath)!;
        var rootReadme = Path.Combine(modDir, "README.md");
        Assert.True(File.Exists(rootReadme));

        var patchesDir = Path.Combine(modDir, "Patches");
        var examplePatch = Path.Combine(patchesDir, "ExamplePatch.cs");
        var patchesReadme = Path.Combine(patchesDir, "README.md");
        Assert.True(File.Exists(examplePatch));
        Assert.True(File.Exists(patchesReadme));

        var csprojContent = await File.ReadAllTextAsync(manifest.ProjectFilePath);
        Assert.Contains("<TargetFramework>netstandard2.1</TargetFramework>", csprojContent, StringComparison.Ordinal);
        Assert.Contains("BepInEx.Core", csprojContent, StringComparison.Ordinal);
        Assert.Contains("BepInEx.AssemblyPublicizer.MSBuild", csprojContent, StringComparison.Ordinal);
        Assert.Contains("<Publicize>true</Publicize>", csprojContent, StringComparison.Ordinal);
        Assert.Contains("<Target Name=\"DeployToBepInEx\"", csprojContent, StringComparison.Ordinal);

        var pluginContent = await File.ReadAllTextAsync(manifest.MainPluginSourcePath);
        Assert.Contains("com.author.speedmod", pluginContent, StringComparison.Ordinal);
        Assert.Contains("SpeedMod", pluginContent, StringComparison.Ordinal);
        Assert.Contains("1.2.0", pluginContent, StringComparison.Ordinal);
        Assert.Contains("_harmony.PatchAll()", pluginContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ScaffoldModProject_Fails_When_ModName_Is_Empty()
    {
        var adapter = new DiskProjectGeneratorAdapter();
        var targetGame = new GameEnvironment(
            Id: new GameId("test-game"),
            GameName: "TestGame",
            GameRootPath: _tempDir,
            ManagedPath: _tempDir,
            EngineType: UnityEngineType.Mono,
            GameAssemblyPaths: Array.Empty<string>(),
            HasBepInExInstalled: false
        );

        var spec = new ModScaffoldSpec(
            ModName: string.Empty,
            PluginGuid: new PluginGuid("com.author.speedmod"),
            Version: "1.0.0",
            OutputDirectory: _tempDir,
            TargetGame: targetGame,
            PublicizedAssemblyNames: Array.Empty<string>()
        );

        var result = await adapter.ScaffoldModProjectAsync(spec);

        Assert.True(result.IsFailure);
        Assert.Contains("Mod name cannot be empty", result.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ScaffoldModProject_Fails_When_OutputDirectory_Is_Empty()
    {
        var adapter = new DiskProjectGeneratorAdapter();
        var targetGame = new GameEnvironment(
            Id: new GameId("test-game"),
            GameName: "TestGame",
            GameRootPath: _tempDir,
            ManagedPath: _tempDir,
            EngineType: UnityEngineType.Mono,
            GameAssemblyPaths: Array.Empty<string>(),
            HasBepInExInstalled: false
        );

        var spec = new ModScaffoldSpec(
            ModName: "MyMod",
            PluginGuid: new PluginGuid("com.author.mymod"),
            Version: "1.0.0",
            OutputDirectory: string.Empty,
            TargetGame: targetGame,
            PublicizedAssemblyNames: Array.Empty<string>()
        );

        var result = await adapter.ScaffoldModProjectAsync(spec);

        Assert.True(result.IsFailure);
        Assert.Contains("Output directory cannot be empty", result.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ScaffoldModProject_When_IncludeAssetPipeline_Is_True_Creates_AssetsDir_And_AssetLoader()
    {
        var adapter = new DiskProjectGeneratorAdapter();
        var targetGame = new GameEnvironment(
            Id: new GameId("test-game"),
            GameName: "TestGame",
            GameRootPath: _tempDir,
            ManagedPath: _tempDir,
            EngineType: UnityEngineType.Mono,
            GameAssemblyPaths: Array.Empty<string>(),
            HasBepInExInstalled: false
        );

        var spec = new ModScaffoldSpec(
            ModName: "VisualMod",
            PluginGuid: new PluginGuid("com.author.visualmod"),
            Version: "1.0.0",
            OutputDirectory: _tempDir,
            TargetGame: targetGame,
            PublicizedAssemblyNames: Array.Empty<string>(),
            IncludeAssetPipeline: true
        );

        var result = await adapter.ScaffoldModProjectAsync(spec);

        Assert.True(result.IsSuccess);
        var modDir = Path.GetDirectoryName(result.Value.ProjectFilePath)!;
        Assert.True(Directory.Exists(Path.Combine(modDir, "Assets")));
        Assert.True(File.Exists(Path.Combine(modDir, "AssetLoader.cs")));
        Assert.True(File.Exists(Path.Combine(modDir, "Assets", "README.md")));
    }

    [Fact]
    public async Task ScaffoldModProject_When_IncludeAssetPipeline_Is_False_Does_Not_Create_AssetsDir_Or_AssetLoader()
    {
        var adapter = new DiskProjectGeneratorAdapter();
        var targetGame = new GameEnvironment(
            Id: new GameId("test-game"),
            GameName: "TestGame",
            GameRootPath: _tempDir,
            ManagedPath: _tempDir,
            EngineType: UnityEngineType.Mono,
            GameAssemblyPaths: Array.Empty<string>(),
            HasBepInExInstalled: false
        );

        var spec = new ModScaffoldSpec(
            ModName: "LogicOnlyMod",
            PluginGuid: new PluginGuid("com.author.logiconly"),
            Version: "1.0.0",
            OutputDirectory: _tempDir,
            TargetGame: targetGame,
            PublicizedAssemblyNames: Array.Empty<string>(),
            IncludeAssetPipeline: false
        );

        var result = await adapter.ScaffoldModProjectAsync(spec);

        Assert.True(result.IsSuccess);
        var modDir = Path.GetDirectoryName(result.Value.ProjectFilePath)!;
        Assert.False(Directory.Exists(Path.Combine(modDir, "Assets")));
        Assert.False(File.Exists(Path.Combine(modDir, "AssetLoader.cs")));
    }
}
