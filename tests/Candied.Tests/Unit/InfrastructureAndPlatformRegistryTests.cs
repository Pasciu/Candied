using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Candied.Core.Models;
using Candied.Core.Ports;
using Candied.Infrastructure;
using Candied.Infrastructure.Internal;
using Candied.Platforms.Unity.Mono;
using Candied.Primitives;
using Candied.Vision;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Candied.Tests.Unit;

public sealed class InfrastructureAndPlatformRegistryTests
{
    private const string SampleCSharpCode = """
        namespace GameLogic.Characters;

        public interface IDamageable
        {
            void TakeDamage(float amount);
        }

        public class BaseCharacter
        {
            protected float _speed = 5.0f;
        }

        public class PlayerController : BaseCharacter, IDamageable
        {
            private float _health = 100f;
            public string PlayerName { get; set; } = "Knight";

            public void TakeDamage(float amount)
            {
                _health -= amount;
            }
        }
        """;

    [Fact]
    public void RoslynSymbolExtractor_ExtractsTypesMembersAndInheritance()
    {
        var (symbols, details) = RoslynSymbolExtractor.ExtractFromFile(
            SampleCSharpCode,
            "Characters/PlayerController.cs",
            "Assembly-CSharp");

        Assert.Equal(3, details.Count);
        Assert.Contains(symbols, s => s.FullName == "GameLogic.Characters.IDamageable" && s.Kind == SymbolKind.Interface);
        Assert.Contains(symbols, s => s.FullName == "GameLogic.Characters.PlayerController" && s.Kind == SymbolKind.Class);
        Assert.Contains(symbols, s => s.FullName == "GameLogic.Characters.PlayerController._health" && s.Kind == SymbolKind.Field);

        var playerDetail = details.First(d => d.FullName == "GameLogic.Characters.PlayerController");
        Assert.Equal("BaseCharacter", playerDetail.BaseType);
        Assert.Contains("IDamageable", playerDetail.Interfaces);
        Assert.Equal("Assembly-CSharp", playerDetail.ModuleName);
    }

    [Fact]
    public void TargetArchitectureAnalyzer_And_KnowledgeHubRenderers_ProduceValidArtifacts()
    {
        var target = new TargetDescriptor(
            Id: new TargetId("SampleGame"),
            Name: "Sample Game",
            TargetPath: "C:/Games/SampleGame",
            RunnerPath: null,
            PlatformId: "unity.mono",
            PlatformDisplayName: "Unity (Mono Runtime)",
            Architecture: "x64",
            EngineVersion: "2020.3.34f1",
            RuntimeFramework: "netstandard2.1",
            ModLoader: new ModLoaderStatus(true, "BepInEx", "5.4.23"),
            Capabilities: [PlatformCapability.CodeKnowledge],
            CodeModules:
            [
                new DiscoveredCodeModule("SampleGame.Scripts.dll", "C:/Games/SampleGame/Managed/SampleGame.Scripts.dll", 512000, CodeModuleTier.CoreLogic, "Core")
            ],
            Metadata: new Dictionary<string, string>(),
            Warnings: []);

        IReadOnlyList<SymbolSummary> symbols =
        [
            new SymbolSummary("SampleGame.RoomManager", SymbolKind.Class, "SampleGame", "SampleGame.Scripts", "class RoomManager", "RoomManager.cs"),
            new SymbolSummary("SampleGame.RoomManager.GoTo", SymbolKind.Method, "SampleGame", "SampleGame.Scripts", "void GoTo()", "RoomManager.cs")
        ];

        var overview = TargetArchitectureAnalyzer.Analyze(target, symbols);
        Assert.Equal("Sample Game", overview.TargetName);
        Assert.Contains("SampleGame.RoomManager", overview.KeyManagers);
        Assert.Equal(1, overview.TotalTypesIndexed);
        Assert.Equal(1, overview.TotalMethodsIndexed);

        string md = KnowledgeHubRenderers.RenderOverview(overview);
        Assert.Contains("# Target Overview: Sample Game", md);
        Assert.Contains("Unity (Mono Runtime)", md);

        string nsMd = KnowledgeHubRenderers.RenderNamespaces(overview.Namespaces, overview.TargetName);
        Assert.Contains("## `SampleGame`", nsMd);

        string json = KnowledgeHubRenderers.RenderSymbolsJson(overview);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("Sample Game", doc.RootElement.GetProperty("targetName").GetString());
    }

    [Fact]
    public async Task DefaultTargetPlatformRegistry_PrioritizesSpecificPlatform_AndFallsBackToGenericTarget()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "Candied_RegTest_" + System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var services = new ServiceCollection();
            services.AddCandiedCoreAndInfrastructure(tempDir);
            services.AddVisionInfrastructure();
            services.AddUnityMonoPlatform();
            using var sp = services.BuildServiceProvider();

            var registry = sp.GetRequiredService<ITargetPlatformRegistry>();
            var layout = sp.GetRequiredService<CandiedStorageLayout>();

            // 1. Unknown ROM + Emulator resolves to GenericTargetPlatform ("generic.target")
            string romPath = Path.Combine(tempDir, "RetroQuest.iso");
            string emuPath = Path.Combine(tempDir, "pcsx2-qt.exe");
            await File.WriteAllTextAsync(romPath, "iso");
            await File.WriteAllTextAsync(emuPath, "exe");

            var genericPlatform = registry.ResolvePlatform(romPath, emuPath);
            Assert.Equal("generic.target", genericPlatform.PlatformId);

            var genericInspect = await registry.InspectTargetAsync(romPath, emuPath, layout);
            Assert.True(genericInspect.IsSuccess);
            Assert.Equal("generic.target", genericInspect.Value.PlatformId);
            Assert.Equal("RetroQuest", genericInspect.Value.Name);

            // 2. Unity Mono directory resolves to UnityMonoTargetPlatform ("unity.mono")
            string unityDir = Path.Combine(tempDir, "AdventureGame");
            string managedDir = Path.Combine(unityDir, "AdventureGame_Data", "Managed");
            Directory.CreateDirectory(managedDir);
            await File.WriteAllTextAsync(Path.Combine(managedDir, "Assembly-CSharp.dll"), "dll");

            var unityPlatform = registry.ResolvePlatform(unityDir);
            Assert.Equal("unity.mono", unityPlatform.PlatformId);
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
