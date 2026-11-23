using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Candied.Agent;
using Candied.Core.Models;
using Candied.Core.UseCases;
using Candied.Decompiler;
using Candied.Primitives;
using Candied.Workspace;
using Xunit;

namespace Candied.Tests.Integration;

public sealed class ModMakerIntegrationTests : IDisposable
{
    private static readonly string[] PublicizedAssemblies = ["Assembly-CSharp"];
    private readonly string _tempDir;
    private readonly ServiceProvider _serviceProvider;

    public ModMakerIntegrationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "Candied_ModMakerTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);

        var services = new ServiceCollection();
        services.AddDecompilerInfrastructure();
        services.AddWorkspaceInfrastructure();
        services.AddTransient<ScaffoldModUseCase>();
        services.AddTransient<BuildModUseCase>();
        services.AddTransient<ReadLogsUseCase>();
        services.AddTransient<GetTypeDetailUseCase>();
        services.AddTransient<AgentWorkbenchService>();

        _serviceProvider = services.BuildServiceProvider();
    }

    public void Dispose()
    {
        _serviceProvider.Dispose();

        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch
        {
            // Ignore cleanup failure
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task AgentWorkbenchService_Can_Scaffold_Mod_And_Return_CamelCase_Json()
    {
        // Arrange
        var workbench = _serviceProvider.GetRequiredService<AgentWorkbenchService>();

        var gameRoot = Path.Combine(_tempDir, "Game");
        var managedDir = Path.Combine(gameRoot, "Game_Data", "Managed");
        Directory.CreateDirectory(managedDir);

        var targetGame = new GameEnvironment(
            Id: new GameId("sample-game"),
            GameName: "SampleGame",
            GameRootPath: gameRoot,
            ManagedPath: managedDir,
            EngineType: UnityEngineType.Mono,
            GameAssemblyPaths: Array.Empty<string>(),
            HasBepInExInstalled: true
        );

        var spec = new ModScaffoldSpec(
            ModName: "JumpBoostMod",
            PluginGuid: new PluginGuid("com.author.jumpboost"),
            Version: "1.0.0",
            OutputDirectory: _tempDir,
            TargetGame: targetGame,
            PublicizedAssemblyNames: PublicizedAssemblies
        );

        // Act
        var responseJson = await workbench.ScaffoldModAsync(spec);

        // Assert
        using var doc = JsonDocument.Parse(responseJson);
        var root = doc.RootElement;

        Assert.True(root.GetProperty("success").GetBoolean());
        var manifest = root.GetProperty("manifest");
        var projectFilePath = manifest.GetProperty("projectFilePath").GetString();
        var mainPluginSourcePath = manifest.GetProperty("mainPluginSourcePath").GetString();

        Assert.NotNull(projectFilePath);
        Assert.True(File.Exists(projectFilePath));

        Assert.NotNull(mainPluginSourcePath);
        Assert.True(File.Exists(mainPluginSourcePath));
    }
}
