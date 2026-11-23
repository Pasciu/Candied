using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Candied.Core.Models;
using Candied.Primitives;
using Candied.Workspace.Adapters;
using Candied.Workspace.Analysis;
using Candied.Workspace.Rendering;
using Xunit;

namespace Candied.Tests.Unit;

public class DocumentationRendererAndAnalyzerTests
{
    private static GameArchitectureOverview CreateSampleOverview()
    {
        return new GameArchitectureOverview(
            GameName: "Potion Craft",
            UnityVersion: "2020.3.34f1",
            RuntimeKind: "Mono",
            TargetFramework: "netstandard2.1",
            Assemblies:
            [
                new GameAssemblyOverview("PotionCraft.Scripts", "PotionCraft.Scripts.dll", 1024 * 500, true),
                new GameAssemblyOverview("UnityEngine.CoreModule", "UnityEngine.CoreModule.dll", 1024 * 2000, false)
            ],
            KeyManagers:
            [
                "PotionCraft.ManagersSystem.Room.RoomManager",
                "PotionCraft.ManagersSystem.Potion.PotionManager"
            ],
            Namespaces:
            [
                new GameNamespaceOverview(
                    NamespaceName: "PotionCraft.ManagersSystem.Room",
                    KeyTypes: ["RoomManager", "Room"],
                    TotalTypesCount: 2
                ),
                new GameNamespaceOverview(
                    NamespaceName: "PotionCraft.ObjectBased",
                    KeyTypes: ["MovableItem", "InteractiveItem"],
                    TotalTypesCount: 5
                )
            ],
            TotalTypesIndexed: 7,
            TotalMethodsIndexed: 42
        );
    }

    [Fact]
    public void GameOverviewTemplateRenderer_Renders_Valid_Markdown()
    {
        var overview = CreateSampleOverview();
        var markdown = GameOverviewTemplateRenderer.Render(overview);

        Assert.Contains("# Potion Craft - Architecture & Modding Guide", markdown);
        Assert.Contains("Unity (Mono)", markdown);
        Assert.Contains("`netstandard2.1`", markdown);
        Assert.Contains("PotionCraft.Scripts", markdown);
        Assert.Contains("PotionCraft.ManagersSystem.Room.RoomManager", markdown);
        Assert.Contains("[Namespace & Class Cheat-Sheet](./NAMESPACES.md)", markdown);
        Assert.Contains("[Hierarchical Symbol Map](./SYMBOLS_INDEX.json)", markdown);
    }

    [Fact]
    public void NamespaceMapTemplateRenderer_Renders_Valid_CheatSheet()
    {
        var overview = CreateSampleOverview();
        var markdown = NamespaceMapTemplateRenderer.Render(overview.Namespaces, overview.GameName);

        Assert.Contains("# Potion Craft - Namespaces & Classes Cheat-Sheet", markdown);
        Assert.Contains("Table of Contents", markdown);
        Assert.Contains("### PotionCraft.ManagersSystem.Room", markdown);
        Assert.Contains("- `RoomManager`", markdown);
        Assert.Contains("*... and 3 additional types in this namespace.*", markdown);
    }

    [Fact]
    public void SymbolIndexJsonRenderer_Renders_Valid_Json()
    {
        var overview = CreateSampleOverview();
        var json = SymbolIndexJsonRenderer.Render(overview);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("Potion Craft", root.GetProperty("gameName").GetString());
        Assert.Equal("Mono", root.GetProperty("runtimeKind").GetString());
        Assert.Equal(7, root.GetProperty("totalTypes").GetInt32());
        Assert.True(root.GetProperty("namespaces").TryGetProperty("PotionCraft.ManagersSystem.Room", out var roomNs));
        Assert.Equal(2, roomNs.GetArrayLength());
    }

    [Fact]
    public void GameArchitectureAnalyzer_Extracts_Managers_And_Groups_Namespaces()
    {
        var gameEnv = new GameEnvironment(
            Id: new GameId("PotionCraft"),
            GameName: "Potion Craft",
            GameRootPath: "C:/Games/PotionCraft",
            ManagedPath: "C:/Games/PotionCraft/Managed",
            EngineType: UnityEngineType.Mono,
            GameAssemblyPaths: ["C:/Games/PotionCraft/Managed/PotionCraft.Scripts.dll"],
            HasBepInExInstalled: true
        );

        IReadOnlyList<SymbolSummary> symbols =
        [
            new SymbolSummary("PotionCraft.RoomManager", SymbolKind.Class, "PotionCraft", "PotionCraft.Scripts", "class RoomManager", "RoomManager.cs"),
            new SymbolSummary("PotionCraft.RoomManager.GoTo", SymbolKind.Method, "PotionCraft", "PotionCraft.Scripts", "void GoTo()", "RoomManager.cs"),
            new SymbolSummary("PotionCraft.InventoryController", SymbolKind.Class, "PotionCraft", "PotionCraft.Scripts", "class InventoryController", "InventoryController.cs"),
            new SymbolSummary("PotionCraft.UI.ItemView", SymbolKind.Class, "PotionCraft.UI", "PotionCraft.Scripts", "class ItemView", "ItemView.cs")
        ];

        var overview = GameArchitectureAnalyzer.Analyze(gameEnv, symbols);

        Assert.Equal("Potion Craft", overview.GameName);
        Assert.Equal("Mono", overview.RuntimeKind);
        Assert.Equal(3, overview.TotalTypesIndexed);
        Assert.Equal(1, overview.TotalMethodsIndexed);
        Assert.Contains("PotionCraft.RoomManager", overview.KeyManagers);
        Assert.Contains("PotionCraft.InventoryController", overview.KeyManagers);
        Assert.Equal(2, overview.Namespaces.Count);
    }

    [Fact]
    public async Task DiskDocumentationGeneratorAdapter_Writes_Files_To_Disk()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "Candied_DocsTest_" + Guid.NewGuid().ToString("N"));
        try
        {
            var adapter = new DiskDocumentationGeneratorAdapter();
            var overview = CreateSampleOverview();

            var result = await adapter.GenerateKnowledgeHubAsync(overview, tempDir);

            Assert.True(result.IsSuccess);
            Assert.True(File.Exists(Path.Combine(tempDir, "GAME_OVERVIEW.md")));
            Assert.True(File.Exists(Path.Combine(tempDir, "NAMESPACES.md")));
            Assert.True(File.Exists(Path.Combine(tempDir, "SYMBOLS_INDEX.json")));

            var overviewContent = await File.ReadAllTextAsync(Path.Combine(tempDir, "GAME_OVERVIEW.md"));
            Assert.Contains("# Potion Craft - Architecture & Modding Guide", overviewContent);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }
}
