using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Candied.Agent;
using Candied.Core.Models;
using Candied.Core.UseCases;
using Candied.Decompiler;
using Candied.Primitives;
using Candied.Workspace;
using Xunit;

namespace Candied.Tests.E2E;

public class VerticalSliceEndToEndTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly IServiceProvider _serviceProvider;
    private readonly CandiedStorageLayout _storageLayout;

    public VerticalSliceEndToEndTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "Candied_E2E_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
        _storageLayout = new CandiedStorageLayout(_tempDirectory);

        var services = new ServiceCollection();

        // Register infrastructure adapters
        services.AddDecompilerInfrastructure();
        services.AddWorkspaceInfrastructure();

        // Register Core use cases and Agent presentation
        services.AddSingleton(_storageLayout);
        services.AddTransient<InspectGameEnvironmentUseCase>();
        services.AddTransient<DecompileAssemblyUseCase>();
        services.AddTransient<SearchGameSymbolsUseCase>();
        services.AddTransient<ScaffoldModUseCase>();
        services.AddTransient<BuildModUseCase>();
        services.AddTransient<ReadLogsUseCase>();
        services.AddTransient<GetTypeDetailUseCase>();
        services.AddTransient<AgentWorkbenchService>();

        _serviceProvider = services.BuildServiceProvider();
    }

    [Fact]
    public async Task EndToEnd_ProgressivePipeline_Decompiles_Indexes_And_Generates_KnowledgeHub()
    {
        // Setup realistic Unity game folder structure
        var gameRoot = Path.Combine(_tempDirectory, "HollowKnight");
        var managedDir = Path.Combine(gameRoot, "HollowKnight_Data", "Managed");
        Directory.CreateDirectory(managedDir);
        Directory.CreateDirectory(Path.Combine(gameRoot, "BepInEx"));

        // Copy a real assembly (Candied.Primitives.dll) as Assembly-CSharp.dll
        var sourceAssembly = typeof(Result<,>).Assembly.Location;
        var targetAssembly = Path.Combine(managedDir, "Assembly-CSharp.dll");
        File.Copy(sourceAssembly, targetAssembly, true);

        var workbench = _serviceProvider.GetRequiredService<AgentWorkbenchService>();

        // 1. Audit game environment
        var inspectJson = await workbench.InspectGameEnvironmentAsync(gameRoot);
        using var inspectDoc = JsonDocument.Parse(inspectJson);
        Assert.True(inspectDoc.RootElement.GetProperty("success").GetBoolean());

        // 2. Decompile assembly
        var decompileJson = await workbench.DecompileAssemblyAsync(gameRoot, "Assembly-CSharp.dll");
        using var decompileDoc = JsonDocument.Parse(decompileJson);
        var decompileRoot = decompileDoc.RootElement;

        Assert.True(decompileRoot.GetProperty("success").GetBoolean());
        Assert.Equal("Assembly-CSharp", decompileRoot.GetProperty("assembly").GetString());
        Assert.True(decompileRoot.GetProperty("sourceFileCount").GetInt32() >= 1);

        // 3. Verify Knowledge Hub artifacts created in Zone 3
        var overviewDocPath = _storageLayout.GetOverviewDocPath("HollowKnight");
        var namespacesDocPath = _storageLayout.GetNamespacesDocPath("HollowKnight");
        var symbolsIndexPath = _storageLayout.GetSymbolsIndexJsonPath("HollowKnight");
        var databasePath = _storageLayout.GetDatabasePath("HollowKnight");

        Assert.True(File.Exists(overviewDocPath), $"Expected overview doc at '{overviewDocPath}'");
        Assert.True(File.Exists(namespacesDocPath), $"Expected namespaces doc at '{namespacesDocPath}'");
        Assert.True(File.Exists(symbolsIndexPath), $"Expected symbols index JSON at '{symbolsIndexPath}'");
        Assert.True(File.Exists(databasePath), $"Expected SQLite DB at '{databasePath}'");

        var overviewContent = await File.ReadAllTextAsync(overviewDocPath);
        Assert.Contains("HollowKnight", overviewContent);
        Assert.Contains("Assembly-CSharp", overviewContent);

        // 4. Query symbols
        var queryJson = await workbench.SearchGameSymbolsOnlyAsync("Result", gameRoot);
        using var queryDoc = JsonDocument.Parse(queryJson);
        var queryRoot = queryDoc.RootElement;

        Assert.True(queryRoot.GetProperty("success").GetBoolean());
        Assert.True(queryRoot.GetProperty("count").GetInt32() >= 1);

        var symbols = queryRoot.GetProperty("symbols");
        Assert.True(symbols.GetArrayLength() >= 1);

        var hasResultSymbol = false;
        foreach (var sym in symbols.EnumerateArray())
        {
            var fullName = sym.GetProperty("fullName").GetString();
            if (fullName != null && fullName.Contains("Result"))
            {
                hasResultSymbol = true;
                break;
            }
        }

        Assert.True(hasResultSymbol, "Expected at least one symbol containing 'Result'.");
    }

    [Fact]
    public async Task EndToEnd_InspectGameEnvironment_Returns_Structured_Error_For_NonExistent_Game()
    {
        var workbench = _serviceProvider.GetRequiredService<AgentWorkbenchService>();
        var nonExistentDir = Path.Combine(_tempDirectory, "NonExistentGame");

        var responseJson = await workbench.InspectGameEnvironmentAsync(nonExistentDir);

        using var doc = JsonDocument.Parse(responseJson);
        var root = doc.RootElement;

        Assert.False(root.GetProperty("success").GetBoolean());
        Assert.True(root.TryGetProperty("error", out var errorProp));
        Assert.Contains("Game directory does not exist", errorProp.GetString()!);
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
