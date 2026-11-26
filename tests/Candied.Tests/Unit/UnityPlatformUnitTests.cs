using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Candied.Core.Models;
using Candied.Platforms.Unity.Mono.Internal;
using Candied.Platforms.Unity.Shared.Internal;
using Candied.Platforms.Unity.Shared.Internal.Assets;
using Candied.Primitives;
using Candied.Vision.Controls;
using Xunit;

namespace Candied.Tests.Unit;

public sealed class UnityPlatformUnitTests
{
    [Theory]
    [InlineData("mscorlib.dll", true)]
    [InlineData("UnityEngine.CoreModule.dll", true)]
    [InlineData("DOTween.dll", true)]
    [InlineData("Assembly-CSharp.dll", false)]
    [InlineData("SampleGame.Scripts.dll", false)]
    public void UnityAssemblyFilter_ClassifiesFrameworkVsGameModulesAccurately(string dllName, bool expectedExcluded)
    {
        Assert.Equal(expectedExcluded, UnityAssemblyFilter.IsExcludedAssembly(dllName));
    }

    [Fact]
    public void UnityAssemblyFilter_ClassifiesModulesIntoCoreModularAndThirdParty()
    {
        string[] paths =
        [
            @"C:\Games\SampleGame\Managed\SampleGame.Core.dll",
            @"C:\Games\SampleGame\Managed\Assembly-CSharp.dll",
            @"C:\Games\SampleGame\Managed\CustomModLogic.dll",
            @"C:\Games\SampleGame\Managed\UnityEngine.dll"
        ];

        var modules = UnityAssemblyFilter.ClassifyModules(
            paths,
            "Sample Game",
            name => name.Equals("SampleGame.Core.dll", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(4, modules.Count);
        Assert.Contains(modules, m => m.Name == "SampleGame.Core.dll" && m.Tier == CodeModuleTier.CoreLogic && m.IsExtracted);
        Assert.Contains(modules, m => m.Name == "CustomModLogic.dll" && m.Tier == CodeModuleTier.ModularLogic);
        Assert.Contains(modules, m => m.Name == "UnityEngine.dll" && m.Tier == CodeModuleTier.AuxiliaryOrThirdParty);
    }

    [Fact]
    public void UnityTextureDecoder_And_FileNameDisambiguator_OperatePurelyAndSafely()
    {
        byte[] src = [0, 0, 255, 255, 255, 0, 0, 255];
        byte[] rotated = UnityTextureDecoder.RotateBgra90Clockwise(src, 2, 1);
        Assert.Equal(8, rotated.Length);
        Assert.Equal(255, rotated[2]);
        Assert.Equal(255, rotated[4]);

        Assert.Equal("name_with_illegal_chars", FileNameDisambiguator.SanitizeFileName("name:with*illegal?chars"));
    }

    [Fact]
    public void BepInExLogParser_ParsesHeadersTimestampsAndMultilineStackTraces()
    {
        string[] lines =
        [
            "[2026-11-26 12:00:00.000][Error  :TestPlugin] System.NullReferenceException: Boom",
            "  at TestPlugin.Plugin.Update () [0x00000]",
            "[Message:   BepInEx] Chainloader ready"
        ];

        var entries = BepInExLogParser.ParseLines(lines);
        Assert.Equal(2, entries.Count);
        Assert.Equal(LogLevel.Error, entries[0].Level);
        Assert.Contains("TestPlugin.Plugin.Update", entries[0].StackTrace);
        Assert.Equal("BepInEx", entries[1].Source);
    }

    [Fact]
    public void MsBuildDiagnosticParser_ExtractsDiagnosticsAndMissingCs0012Assemblies()
    {
        string msbuildOutput = """
            C:\Mods\MyMod\Plugin.cs(12,5): error CS0012: The type 'Animator' is defined in an assembly that is not referenced. You must add a reference to assembly 'UnityEngine.AnimationModule, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null'.
            C:\Mods\MyMod\Plugin.cs(25,9): error CS0012: Il tipo 'TMP_Text' è definito in un assembly a cui non viene fatto riferimento. Aggiungere un riferimento all'assembly 'Unity.TextMeshPro, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null'.
            """;

        var diags = MsBuildDiagnosticParser.ParseDiagnostics(msbuildOutput);
        Assert.Equal(2, diags.Count);

        var missing = MsBuildDiagnosticParser.ExtractMissingAssemblyNames(diags);
        Assert.Equal(2, missing.Count);
        Assert.Contains("UnityEngine.AnimationModule", missing);
        Assert.Contains("Unity.TextMeshPro", missing);
    }

    [Fact]
    public void UnityMonoTemplateRenderers_And_CsprojReferenceMutator_GenerateValidProjectFiles()
    {
        var target = new TargetDescriptor(
            Id: new TargetId("TestGame"),
            Name: "TestGame",
            TargetPath: @"C:\Games\TestGame",
            RunnerPath: null,
            PlatformId: "unity.mono",
            PlatformDisplayName: "Unity (Mono Runtime)",
            Architecture: "x64",
            EngineVersion: "2022.3",
            RuntimeFramework: "netstandard2.1",
            ModLoader: new ModLoaderStatus(true, "BepInEx", "5.4.23"),
            Capabilities: [PlatformCapability.ModWorkspace],
            CodeModules:
            [
                new DiscoveredCodeModule("TestGame.Scripts.dll", @"C:\Games\TestGame\TestGame_Data\Managed\TestGame.Scripts.dll", 1024, CodeModuleTier.CoreLogic, "Core")
            ],
            Metadata: new Dictionary<string, string>
            {
                ["managedPath"] = @"C:\Games\TestGame\TestGame_Data\Managed"
            },
            Warnings: []);

        var spec = new ModScaffoldSpec(
            ModName: "SuperMod",
            ModId: new ModId("com.author.supermod"),
            Version: "1.2.0",
            OutputDirectory: @"C:\Mods\SuperMod",
            Target: target,
            IncludeAssetPipeline: true);

        string csproj = UnityMonoTemplateRenderers.RenderCsproj(spec);
        Assert.Contains("<Reference Include=\"TestGame.Scripts\">", csproj);
        Assert.Contains("<Publicize>true</Publicize>", csproj);
        Assert.Contains("$(ModPluginsDeployDir)Assets", csproj);

        var mutOutcome = CsprojReferenceMutator.AddReference(csproj, "UnityEngine.AudioModule", @"C:\Games\TestGame\TestGame_Data\Managed\UnityEngine.AudioModule.dll", publicize: false);
        Assert.False(mutOutcome.AlreadyReferenced);
        Assert.Contains("UnityEngine.AudioModule", mutOutcome.UpdatedXml);

        string pluginCs = UnityMonoTemplateRenderers.RenderPlugin(spec);
        Assert.Contains("public const string PluginGuid = \"com.author.supermod\";", pluginCs);

        string cfg = UnityMonoTemplateRenderers.RenderBepInExConfig(enableConsole: true);
        Assert.Contains("Enabled = true", cfg);
    }

    [Fact]
    public async Task UnityInputProfileExtractor_ExtractsKeyboardAndGamepadBindingsFromInputActionsJson()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "Candied_InputExt_" + Guid.NewGuid().ToString("N"));
        string dataDir = Path.Combine(tempDir, "Sample_Data", "StreamingAssets");
        Directory.CreateDirectory(dataDir);

        try
        {
            string inputActionsJson = """
                {
                  "name": "PlayerControls",
                  "maps": [
                    {
                      "name": "Gameplay",
                      "actions": [
                        { "name": "Jump" },
                        { "name": "Inventory" }
                      ],
                      "bindings": [
                        { "action": "Jump", "path": "<Keyboard>/space" },
                        { "action": "Jump", "path": "<Gamepad>/buttonSouth" },
                        { "action": "Inventory", "path": "<Keyboard>/i" }
                      ]
                    }
                  ]
                }
                """;
            await File.WriteAllTextAsync(Path.Combine(dataDir, "controls.inputactions"), inputActionsJson);

            var extractor = new UnityInputProfileExtractor();
            var extracted = await extractor.ExtractControlsOfflineAsync(tempDir, Path.Combine(tempDir, "Sample_Data"));

            Assert.Equal(2, extracted.Count);
            var jump = Assert.Single(extracted, a => a.ActionName == "Jump");
            Assert.Equal("auto", jump.Source);
            Assert.Contains(jump.Bindings, b => b.Channel == InputChannel.Keyboard);
            Assert.Contains(jump.Bindings, b => b.Channel == InputChannel.Gamepad);
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
