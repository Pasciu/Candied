using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Candied.Agent;
using Candied.Core.Models;
using Candied.Infrastructure;
using Candied.Platforms.Unity.Mono;
using Candied.Primitives;
using Candied.Vision;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Candied.Tests.E2E;

public sealed class VerticalSliceEndToEndTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly ServiceProvider _serviceProvider;
    private readonly CandiedStorageLayout _storageLayout;

    public VerticalSliceEndToEndTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "Candied_E2E_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);

        var services = new ServiceCollection();
        services.AddCandiedCoreAndInfrastructure(_tempDirectory);
        services.AddVisionInfrastructure();
        services.AddUnityMonoPlatform();
        services.AddTransient<AgentWorkbenchService>();

        _serviceProvider = services.BuildServiceProvider();
        _storageLayout = _serviceProvider.GetRequiredService<CandiedStorageLayout>();
    }

    [Fact]
    public async Task EndToEnd_MultiPlatformPipeline_Inspects_ExtractsCode_GeneratesKnowledgeHub_Queries_And_ScaffoldsMod()
    {
        string gameRoot = Path.Combine(_tempDirectory, "SampleGame");
        string managedDir = Path.Combine(gameRoot, "SampleGame_Data", "Managed");
        Directory.CreateDirectory(managedDir);
        Directory.CreateDirectory(Path.Combine(gameRoot, "BepInEx"));

        string sourceAssembly = typeof(Result<,>).Assembly.Location;
        string targetAssembly = Path.Combine(managedDir, "Assembly-CSharp.dll");
        File.Copy(sourceAssembly, targetAssembly, overwrite: true);

        var workbench = _serviceProvider.GetRequiredService<AgentWorkbenchService>();

        // 1. Inspect target
        string inspectJson = await workbench.InspectTargetAsync(gameRoot);
        using var inspectDoc = JsonDocument.Parse(inspectJson);
        Assert.True(inspectDoc.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("unity.mono", inspectDoc.RootElement.GetProperty("platformId").GetString());

        // 2. Extract code module & index into SQLite
        string extractJson = await workbench.ExtractCodeAsync("Assembly-CSharp.dll", gameRoot);
        using var extractDoc = JsonDocument.Parse(extractJson);
        Assert.True(extractDoc.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("Assembly-CSharp", extractDoc.RootElement.GetProperty("moduleName").GetString());
        Assert.True(extractDoc.RootElement.GetProperty("sourceFileCount").GetInt32() >= 1);

        // 3. Verify Zone 3 Knowledge Hub artifacts
        string overviewPath = _storageLayout.GetOverviewDocPath("SampleGame");
        string namespacesPath = _storageLayout.GetNamespacesDocPath("SampleGame");
        string symbolsJsonPath = _storageLayout.GetSymbolsIndexJsonPath("SampleGame");
        string dbPath = _storageLayout.GetDatabasePath("SampleGame");

        Assert.True(File.Exists(overviewPath));
        Assert.True(File.Exists(namespacesPath));
        Assert.True(File.Exists(symbolsJsonPath));
        Assert.True(File.Exists(dbPath));

        // 4. Query symbols & FTS5 code search
        string queryJson = await workbench.QuerySymbolsAsync("Result", gameRoot);
        using var queryDoc = JsonDocument.Parse(queryJson);
        Assert.True(queryDoc.RootElement.GetProperty("success").GetBoolean());
        Assert.True(queryDoc.RootElement.GetProperty("count").GetInt32() >= 1);

        string codeJson = await workbench.SearchCodeAsync("IsSuccess", gameRoot);
        using var codeDoc = JsonDocument.Parse(codeJson);
        Assert.True(codeDoc.RootElement.GetProperty("success").GetBoolean());
        Assert.True(codeDoc.RootElement.GetProperty("count").GetInt32() >= 1);

        // 5. Scaffold mod in Zone 2
        string scaffoldJson = await workbench.ScaffoldModAsync("DashUpgradeMod", targetPath: gameRoot);
        using var scaffoldDoc = JsonDocument.Parse(scaffoldJson);
        Assert.True(scaffoldDoc.RootElement.GetProperty("success").GetBoolean());
        string csprojPath = scaffoldDoc.RootElement.GetProperty("manifest").GetProperty("projectFilePath").GetString()!;
        Assert.True(File.Exists(csprojPath));
    }

    public void Dispose()
    {
        _serviceProvider.Dispose();
        try
        {
            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, recursive: true);
            }
        }
        catch
        {
        }
    }
}
