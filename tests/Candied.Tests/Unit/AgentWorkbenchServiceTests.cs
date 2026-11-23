using System.Text.Json;
using Candied.Agent;
using Candied.Core.Errors;
using Candied.Core.Models;
using Candied.Core.Ports;
using Candied.Core.UseCases;
using Candied.Primitives;
using Xunit;

namespace Candied.Tests.Unit;

public class AgentWorkbenchServiceTests
{
    private sealed class FakeGameDetector : IGameDetector
    {
        public Task<Result<GameEnvironment, DetectionError>> DetectGameAsync(string gameDirectoryPath, CancellationToken ct = default)
        {
            var gameName = Path.GetFileName(gameDirectoryPath.TrimEnd('/', '\\'));
            if (string.Equals(gameName, "PotionCraft", StringComparison.OrdinalIgnoreCase))
            {
                gameName = "Potion Craft";
            }

            var env = new GameEnvironment(
                Id: new GameId(gameName),
                GameName: gameName,
                GameRootPath: gameDirectoryPath,
                ManagedPath: Path.Combine(gameDirectoryPath, "Managed"),
                EngineType: UnityEngineType.Mono,
                GameAssemblyPaths: [Path.Combine(gameDirectoryPath, "PotionCraft.Scripts.dll")],
                HasBepInExInstalled: true
            );
            return Task.FromResult(Result.Ok<GameEnvironment, DetectionError>(env));
        }
    }

    private sealed class FakeDecompilerEngine : IDecompilerEngine
    {
        public Task<Result<DecompilationOutput, DecompilerError>> DecompileAssemblyAsync(
            string assemblyPath, string outputSourceDirectory, IProgress<DecompileProgress>? progress = null, CancellationToken ct = default)
        {
            return Task.FromResult(Result.Ok<DecompilationOutput, DecompilerError>(
                new DecompilationOutput("PotionCraft.Scripts", outputSourceDirectory, 1)));
        }
    }

    private sealed class FakeSymbolIndex : ISymbolIndex
    {
        public string? ReceivedDatabasePath { get; private set; }

        public Task<Result<Candied.Primitives.Unit, IndexError>> IndexSourceTreeAsync(
            string sourceDirectory,
            string? assemblyName = null,
            string? databasePath = null,
            CancellationToken ct = default)
        {
            ReceivedDatabasePath = databasePath;
            return Task.FromResult(Result.Ok<Candied.Primitives.Unit, IndexError>(new Candied.Primitives.Unit()));
        }

        public Task<Result<IReadOnlyList<SymbolSummary>, IndexError>> SearchSymbolsAsync(
            SymbolQuery query,
            string? databasePath = null,
            CancellationToken ct = default)
        {
            ReceivedDatabasePath = databasePath;
            IReadOnlyList<SymbolSummary> results =
            [
                new SymbolSummary("PotionCraft.RoomManager", SymbolKind.Class, "PotionCraft", "PotionCraft.Scripts", "class RoomManager", "RoomManager.cs")
            ];
            return Task.FromResult(Result.Ok<IReadOnlyList<SymbolSummary>, IndexError>(results));
        }

        public Task<Result<IReadOnlyList<SymbolSummary>, IndexError>> GetAllSymbolsAsync(
            string? databasePath = null,
            CancellationToken ct = default)
        {
            ReceivedDatabasePath = databasePath;
            IReadOnlyList<SymbolSummary> results =
            [
                new SymbolSummary("PotionCraft.RoomManager", SymbolKind.Class, "PotionCraft", "PotionCraft.Scripts", "class RoomManager", "RoomManager.cs")
            ];
            return Task.FromResult(Result.Ok<IReadOnlyList<SymbolSummary>, IndexError>(results));
        }

        public Task<Result<TypeDefinitionDetail, IndexError>> GetTypeDetailAsync(string fullTypeName, string? databasePath = null, CancellationToken ct = default)
        {
            ReceivedDatabasePath = databasePath;
            var detail = new TypeDefinitionDetail(
                FullName: fullTypeName,
                Namespace: "PotionCraft",
                BaseType: "MonoBehaviour",
                Interfaces: [],
                FieldSignatures: [],
                MethodSignatures: [],
                RawSourceCode: "class RoomManager {}"
            );
            return Task.FromResult(Result.Ok<TypeDefinitionDetail, IndexError>(detail));
        }

        public Task<Result<IReadOnlyList<CodeSearchMatch>, IndexError>> SearchCodeAsync(
            CodeSearchQuery query,
            string? databasePath = null,
            CancellationToken ct = default)
        {
            ReceivedDatabasePath = databasePath;
            return Task.FromResult(Result.Ok<IReadOnlyList<CodeSearchMatch>, IndexError>([]));
        }
    }

    private sealed class FakeDocGenerator : IDocumentationGenerator
    {
        public bool WasCalled { get; private set; }
        public string? ReceivedDocsPath { get; private set; }

        public Task<Result<Candied.Primitives.Unit, ScaffoldError>> GenerateKnowledgeHubAsync(GameArchitectureOverview overview, string docsDirectoryPath, CancellationToken ct = default)
        {
            WasCalled = true;
            ReceivedDocsPath = docsDirectoryPath;
            return Task.FromResult(Result.Ok<Candied.Primitives.Unit, ScaffoldError>(new Candied.Primitives.Unit()));
        }

        public Task<Result<Candied.Primitives.Unit, ScaffoldError>> GenerateKnowledgeHubAsync(GameEnvironment gameEnv, IReadOnlyList<SymbolSummary> symbols, string docsDirectoryPath, CancellationToken ct = default)
        {
            WasCalled = true;
            ReceivedDocsPath = docsDirectoryPath;
            return Task.FromResult(Result.Ok<Candied.Primitives.Unit, ScaffoldError>(new Candied.Primitives.Unit()));
        }

        public Task<Result<Candied.Primitives.Unit, ScaffoldError>> SaveResearchNotesAsync(string gameName, string markdownContent, string docsDirectoryPath, CancellationToken ct = default)
        {
            WasCalled = true;
            ReceivedDocsPath = docsDirectoryPath;
            return Task.FromResult(Result.Ok<Candied.Primitives.Unit, ScaffoldError>(new Candied.Primitives.Unit()));
        }
    }

    private sealed class FakeProjectGenerator : IProjectGenerator
    {
        public Task<Result<ModProjectManifest, ScaffoldError>> ScaffoldModProjectAsync(ModScaffoldSpec spec, CancellationToken ct = default) =>
            Task.FromResult(Result.Ok<ModProjectManifest, ScaffoldError>(
                new ModProjectManifest(Path.Combine(spec.OutputDirectory, "Mod.csproj"), "Plugin.cs", "bin/")));
    }

    private sealed class FakeBuildRunner : IBuildRunner
    {
        public Task<Result<BuildReport, BuildExecutionError>> BuildModAsync(string projectFilePath, string configuration = "Debug", CancellationToken ct = default) =>
            Task.FromResult(Result.Ok<BuildReport, BuildExecutionError>(
                new BuildReport(true, 0, [], "Success")));
    }

    private sealed class FakeLogMonitor : ILogMonitor
    {
        public Task<Result<IReadOnlyList<LogEntry>, LogMonitorError>> ReadRecentLogsAsync(string logFilePath, int maxLines = 200, CancellationToken ct = default) =>
            Task.FromResult(Result.Ok<IReadOnlyList<LogEntry>, LogMonitorError>([]));

        public IAsyncEnumerable<LogEntry> StreamLogsAsync(string logFilePath, CancellationToken ct = default) =>
            throw new NotImplementedException();
    }

    private sealed class FakeGameProcessManager : IGameProcessManager
    {
        public bool IsRunning { get; set; } = true;
        public bool StopCalled { get; private set; }
        public bool LaunchCalled { get; private set; }

        public Task<Result<GameProcessInfo?, ProcessManagementError>> GetRunningGameAsync(GameEnvironment game, CancellationToken ct = default)
        {
            if (!IsRunning)
            {
                return Task.FromResult(Result.Ok<GameProcessInfo?, ProcessManagementError>(null));
            }

            var info = new GameProcessInfo(
                Id: new ProcessId(4242),
                ProcessName: game.GameName,
                MainWindowTitle: game.GameName,
                HasBridgeAttached: true,
                IsResponding: true,
                StartTime: DateTime.UtcNow
            );
            return Task.FromResult(Result.Ok<GameProcessInfo?, ProcessManagementError>(info));
        }

        public Task<Result<GameProcessInfo, ProcessManagementError>> LaunchGameAsync(GameEnvironment game, GameLaunchOptions options, CancellationToken ct = default)
        {
            LaunchCalled = true;
            IsRunning = true;
            var info = new GameProcessInfo(
                Id: new ProcessId(4242),
                ProcessName: game.GameName,
                MainWindowTitle: game.GameName,
                HasBridgeAttached: true,
                IsResponding: true,
                StartTime: DateTime.UtcNow
            );
            return Task.FromResult(Result.Ok<GameProcessInfo, ProcessManagementError>(info));
        }

        public Task<Result<Candied.Primitives.Unit, ProcessManagementError>> StopGameAsync(GameEnvironment game, CancellationToken ct = default)
        {
            StopCalled = true;
            IsRunning = false;
            return Task.FromResult(Result.Ok<Candied.Primitives.Unit, ProcessManagementError>(new Candied.Primitives.Unit()));
        }
    }

    private sealed class FakeAssetExtractor : IAssetExtractor
    {
        public Task<Result<IReadOnlyList<DiscoveredAssetInfo>, AssetExtractionError>> DiscoverAssetsAsync(
            GameEnvironment game,
            AssetDiscoveryFilter filter,
            CancellationToken ct = default)
        {
            IReadOnlyList<DiscoveredAssetInfo> assets =
            [
                new DiscoveredAssetInfo("Beard_01", "Sprite", "resources.assets", "C:/Games/Data/resources.assets", 101, 64, 64, 2048, "png"),
                new DiscoveredAssetInfo("Hair_02", "Sprite", "sharedassets1.assets", "C:/Games/Data/sharedassets1.assets", 102, 128, 128, 4096, "png")
            ];
            return Task.FromResult(Result.Ok<IReadOnlyList<DiscoveredAssetInfo>, AssetExtractionError>(assets));
        }

        public Task<Result<IReadOnlyList<ExtractedAssetInfo>, AssetExtractionError>> ExtractAssetsAsync(
            GameEnvironment game,
            AssetExtractionFilter filter,
            string outputDirectory,
            CancellationToken ct = default)
        {
            IReadOnlyList<ExtractedAssetInfo> assets =
            [
                new ExtractedAssetInfo("Beard_01", "Sprite", Path.Combine(outputDirectory, "Beard_01.png"), 1234, 64, 64, 101, "resources.assets")
            ];
            return Task.FromResult(Result.Ok<IReadOnlyList<ExtractedAssetInfo>, AssetExtractionError>(assets));
        }
    }

    [Fact]
    public async Task DecompileAssemblyAsync_Routes_To_Candied_Layout_And_Generates_Docs()
    {
        var workspace = "C:/TestWorkspace";
        var layout = new CandiedStorageLayout(workspace);
        var detector = new FakeGameDetector();
        var decompiler = new FakeDecompilerEngine();
        var indexer = new FakeSymbolIndex();
        var docGen = new FakeDocGenerator();

        var scaffoldUseCase = new ScaffoldModUseCase(new FakeProjectGenerator());
        var buildUseCase = new BuildModUseCase(new FakeBuildRunner());
        var readLogsUseCase = new ReadLogsUseCase(new FakeLogMonitor());
        var typeDetailUseCase = new GetTypeDetailUseCase(indexer);
        var decompileUseCase = new DecompileAssemblyUseCase(detector, decompiler, indexer, docGen);

        var service = new AgentWorkbenchService(
            scaffoldUseCase, buildUseCase, readLogsUseCase, typeDetailUseCase, detector, docGen, layout,
            decompileAssemblyUseCase: decompileUseCase);

        var json = await service.DecompileAssemblyAsync("C:/Games/PotionCraft", "PotionCraft.Scripts.dll");
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.True(root.GetProperty("success").GetBoolean());
        Assert.Equal("PotionCraft.Scripts", root.GetProperty("assembly").GetString());
        Assert.True(docGen.WasCalled);
        Assert.Equal("C:/TestWorkspace/.candied/games/Potion-Craft/docs", docGen.ReceivedDocsPath);

        var artifacts = root.GetProperty("artifacts");
        Assert.Equal("C:/TestWorkspace/.candied/games/Potion-Craft/decompiled/PotionCraft.Scripts", artifacts.GetProperty("decompiledDirectory").GetString());
        Assert.Equal("C:/TestWorkspace/.candied/games/Potion-Craft/index.db", artifacts.GetProperty("databasePath").GetString());
    }

    [Fact]
    public async Task GetTypeDetailAsync_Uses_Contextual_Or_Explicit_DatabasePath()
    {
        var workspace = "C:/TestWorkspace";
        var layout = new CandiedStorageLayout(workspace);
        var detector = new FakeGameDetector();
        var decompiler = new FakeDecompilerEngine();
        var indexer = new FakeSymbolIndex();
        var docGen = new FakeDocGenerator();

        var scaffoldUseCase = new ScaffoldModUseCase(new FakeProjectGenerator());
        var buildUseCase = new BuildModUseCase(new FakeBuildRunner());
        var readLogsUseCase = new ReadLogsUseCase(new FakeLogMonitor());
        var typeDetailUseCase = new GetTypeDetailUseCase(indexer);
        var decompileUseCase = new DecompileAssemblyUseCase(detector, decompiler, indexer, docGen);

        var service = new AgentWorkbenchService(
            scaffoldUseCase, buildUseCase, readLogsUseCase, typeDetailUseCase, detector, docGen, layout,
            decompileAssemblyUseCase: decompileUseCase);

        // 1. Decompile sets active context
        await service.DecompileAssemblyAsync("C:/Games/PotionCraft", "PotionCraft.Scripts.dll");

        // 2. Query type without gameDirectory uses active context
        var json1 = await service.GetTypeDetailAsync("PotionCraft.RoomManager");
        using var doc1 = JsonDocument.Parse(json1);
        Assert.True(doc1.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("C:/TestWorkspace/.candied/games/Potion-Craft/index.db", indexer.ReceivedDatabasePath);

        // 3. Query type with explicit gameDirectory
        var json2 = await service.GetTypeDetailAsync("Other.Class", "C:/Games/OtherGame");
        using var doc2 = JsonDocument.Parse(json2);
        Assert.True(doc2.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("C:/TestWorkspace/.candied/games/OtherGame/index.db", indexer.ReceivedDatabasePath);
    }

    [Fact]
    public async Task InspectGameEnvironmentAsync_Returns_NextSuggestedStep_Guiding_Agent()
    {
        var workspace = "C:/TestWorkspace";
        var layout = new CandiedStorageLayout(workspace);
        var detector = new FakeGameDetector();
        var decompiler = new FakeDecompilerEngine();
        var indexer = new FakeSymbolIndex();
        var docGen = new FakeDocGenerator();

        var scaffoldUseCase = new ScaffoldModUseCase(new FakeProjectGenerator());
        var buildUseCase = new BuildModUseCase(new FakeBuildRunner());
        var readLogsUseCase = new ReadLogsUseCase(new FakeLogMonitor());
        var typeDetailUseCase = new GetTypeDetailUseCase(indexer);
        var inspectEnvUseCase = new InspectGameEnvironmentUseCase(detector);

        var service = new AgentWorkbenchService(
            scaffoldUseCase, buildUseCase, readLogsUseCase, typeDetailUseCase, detector, docGen, layout,
            inspectGameEnvironmentUseCase: inspectEnvUseCase);

        var json = await service.InspectGameEnvironmentAsync("C:/Games/PotionCraft");
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.True(root.GetProperty("success").GetBoolean());
        Assert.True(root.TryGetProperty("nextSuggestedStep", out var nextStep));
        Assert.False(string.IsNullOrWhiteSpace(nextStep.GetString()));
        Assert.Contains("decompile", nextStep.GetString());
    }

    [Fact]
    public async Task GetGameProcessStatusAsync_Returns_Process_Info_And_Bridge_Status()
    {
        var workspace = "C:/TestWorkspace";
        var layout = new CandiedStorageLayout(workspace);
        var detector = new FakeGameDetector();
        var indexer = new FakeSymbolIndex();
        var procMgr = new FakeGameProcessManager();

        var scaffoldUseCase = new ScaffoldModUseCase(new FakeProjectGenerator());
        var buildUseCase = new BuildModUseCase(new FakeBuildRunner());
        var readLogsUseCase = new ReadLogsUseCase(new FakeLogMonitor());
        var typeDetailUseCase = new GetTypeDetailUseCase(indexer);
        var statusUseCase = new GetGameProcessStatusUseCase(procMgr);

        var service = new AgentWorkbenchService(
            scaffoldUseCase, buildUseCase, readLogsUseCase, typeDetailUseCase, detector,
            storageLayout: layout,
            getGameProcessStatusUseCase: statusUseCase);

        var json = await service.GetGameProcessStatusAsync("C:/Games/PotionCraft");
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.True(root.GetProperty("success").GetBoolean());
        Assert.True(root.GetProperty("isRunning").GetBoolean());
        var process = root.GetProperty("process");
        Assert.Equal(4242, process.GetProperty("processId").GetInt32());
    }

    [Fact]
    public async Task LaunchGameAsync_Launches_Game_In_Windowed_Mode()
    {
        var workspace = "C:/TestWorkspace";
        var layout = new CandiedStorageLayout(workspace);
        var detector = new FakeGameDetector();
        var indexer = new FakeSymbolIndex();
        var procMgr = new FakeGameProcessManager { IsRunning = false };

        var scaffoldUseCase = new ScaffoldModUseCase(new FakeProjectGenerator());
        var buildUseCase = new BuildModUseCase(new FakeBuildRunner());
        var readLogsUseCase = new ReadLogsUseCase(new FakeLogMonitor());
        var typeDetailUseCase = new GetTypeDetailUseCase(indexer);
        var launchUseCase = new LaunchGameUseCase(procMgr);

        var service = new AgentWorkbenchService(
            scaffoldUseCase, buildUseCase, readLogsUseCase, typeDetailUseCase, detector,
            storageLayout: layout,
            launchGameUseCase: launchUseCase);

        var json = await service.LaunchGameAsync("C:/Games/PotionCraft", windowed: true, width: 1280, height: 720);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.True(root.GetProperty("success").GetBoolean());
        Assert.True(procMgr.LaunchCalled);
        Assert.Equal(4242, root.GetProperty("processId").GetInt32());
        Assert.Equal("1280x720", root.GetProperty("resolution").GetString());
    }

    [Fact]
    public async Task StopGameAsync_Stops_Running_Process()
    {
        var workspace = "C:/TestWorkspace";
        var layout = new CandiedStorageLayout(workspace);
        var detector = new FakeGameDetector();
        var indexer = new FakeSymbolIndex();
        var procMgr = new FakeGameProcessManager { IsRunning = true };

        var scaffoldUseCase = new ScaffoldModUseCase(new FakeProjectGenerator());
        var buildUseCase = new BuildModUseCase(new FakeBuildRunner());
        var readLogsUseCase = new ReadLogsUseCase(new FakeLogMonitor());
        var typeDetailUseCase = new GetTypeDetailUseCase(indexer);
        var stopUseCase = new StopGameUseCase(procMgr);

        var service = new AgentWorkbenchService(
            scaffoldUseCase, buildUseCase, readLogsUseCase, typeDetailUseCase, detector,
            storageLayout: layout,
            stopGameUseCase: stopUseCase);

        var json = await service.StopGameAsync("C:/Games/PotionCraft");
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.True(root.GetProperty("success").GetBoolean());
        Assert.True(procMgr.StopCalled);
        Assert.False(procMgr.IsRunning);
    }

    [Fact]
    public async Task DiscoverGameAssetsAsync_Returns_Discovered_Assets()
    {
        var workspace = "C:/TestWorkspace";
        var layout = new CandiedStorageLayout(workspace);
        var detector = new FakeGameDetector();
        var extractor = new FakeAssetExtractor();

        var scaffoldUseCase = new ScaffoldModUseCase(new FakeProjectGenerator());
        var buildUseCase = new BuildModUseCase(new FakeBuildRunner());
        var readLogsUseCase = new ReadLogsUseCase(new FakeLogMonitor());
        var typeDetailUseCase = new GetTypeDetailUseCase(new FakeSymbolIndex());
        var discoverUseCase = new DiscoverAssetsUseCase(extractor);

        var service = new AgentWorkbenchService(
            scaffoldUseCase, buildUseCase, readLogsUseCase, typeDetailUseCase, detector,
            storageLayout: layout,
            discoverAssetsUseCase: discoverUseCase);

        var json = await service.DiscoverGameAssetsAsync(searchPattern: "Beard", maxResults: 10, gameDirectory: "C:/Games/PotionCraft");
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.True(root.GetProperty("success").GetBoolean());
        Assert.Equal(2, root.GetProperty("totalDiscovered").GetInt32());
        var assets = root.GetProperty("assets");
        Assert.Equal(2, assets.GetArrayLength());
        Assert.Equal("Beard_01", assets[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task ExtractGameAssetsAsync_Returns_Extracted_Assets_Report()
    {
        var workspace = "C:/TestWorkspace";
        var layout = new CandiedStorageLayout(workspace);
        var detector = new FakeGameDetector();
        var extractor = new FakeAssetExtractor();

        var scaffoldUseCase = new ScaffoldModUseCase(new FakeProjectGenerator());
        var buildUseCase = new BuildModUseCase(new FakeBuildRunner());
        var readLogsUseCase = new ReadLogsUseCase(new FakeLogMonitor());
        var typeDetailUseCase = new GetTypeDetailUseCase(new FakeSymbolIndex());
        var extractUseCase = new ExtractAssetsUseCase(extractor, layout);

        var service = new AgentWorkbenchService(
            scaffoldUseCase, buildUseCase, readLogsUseCase, typeDetailUseCase, detector,
            storageLayout: layout,
            extractAssetsUseCase: extractUseCase);

        var json = await service.ExtractGameAssetsAsync(searchPattern: "Beard_01", gameDirectory: "C:/Games/PotionCraft");
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.True(root.GetProperty("success").GetBoolean());
        Assert.Equal(1, root.GetProperty("totalExtracted").GetInt32());
        var extractedAssets = root.GetProperty("extractedAssets");
        Assert.Equal("Beard_01", extractedAssets[0].GetProperty("name").GetString());
    }

    [Fact]
    public void ResolveDefaultWorkspaceRoot_WhenCwdIsExternalIdeFolder_ResolvesRepoRootFromBaseDirectory()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"candied_ws_test_{Guid.NewGuid():N}");
        var fakeIdeDir = Path.Combine(tempRoot, "AppData", "Local", "Programs", "antigravity");
        var fakeRepoRoot = Path.Combine(tempRoot, "GitHub", "Candied");
        var fakePublishDir = Path.Combine(fakeRepoRoot, "publish");

        Directory.CreateDirectory(fakeIdeDir);
        Directory.CreateDirectory(fakePublishDir);
        File.WriteAllText(Path.Combine(fakeRepoRoot, "Candied.sln"), string.Empty);

        try
        {
            var resolved = AgentWorkbenchService.ResolveDefaultWorkspaceRoot(
                currentDirectory: fakeIdeDir,
                baseDirectory: fakePublishDir);

            Assert.Equal(Path.GetFullPath(fakeRepoRoot), Path.GetFullPath(resolved));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ScaffoldModByPathAsync_WithRelativeOutputDirectory_ResolvesAgainstWorkspaceRoot()
    {
        var workspace = Path.Combine(Path.GetTempPath(), $"candied_scaffold_ws_{Guid.NewGuid():N}");
        var layout = new CandiedStorageLayout(workspace);
        var detector = new FakeGameDetector();

        var scaffoldUseCase = new ScaffoldModUseCase(new FakeProjectGenerator());
        var buildUseCase = new BuildModUseCase(new FakeBuildRunner());
        var readLogsUseCase = new ReadLogsUseCase(new FakeLogMonitor());
        var typeDetailUseCase = new GetTypeDetailUseCase(new FakeSymbolIndex());

        var service = new AgentWorkbenchService(
            scaffoldUseCase, buildUseCase, readLogsUseCase, typeDetailUseCase, detector,
            storageLayout: layout);

        var json = await service.ScaffoldModByPathAsync(
            modName: "NPCDesigner",
            pluginGuid: "com.candied.npcdesigner",
            version: "1.0.0",
            outputDirectory: "mods/NPCDesigner",
            gameDirectory: "C:/Games/PotionCraft");

        using var doc = JsonDocument.Parse(json);
        var projectFilePath = doc.RootElement
            .GetProperty("manifest")
            .GetProperty("projectFilePath")
            .GetString();

        var expectedDir = Path.GetFullPath(Path.Combine(workspace, "mods", "NPCDesigner"));
        Assert.StartsWith(expectedDir, Path.GetFullPath(projectFilePath!));
    }
}
