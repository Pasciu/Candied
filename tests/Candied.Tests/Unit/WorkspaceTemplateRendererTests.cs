using Candied.Core.Models;
using Candied.Primitives;
using Candied.Workspace.Rendering;
using Xunit;

namespace Candied.Tests.Unit;

public class WorkspaceTemplateRendererTests
{
    private static ModScaffoldSpec CreateSampleSpec(IReadOnlyList<string>? publicized = null)
    {
        var game = new GameEnvironment(
            Id: new GameId("TestGame"),
            GameName: "TestGame",
            GameRootPath: @"C:\Games\TestGame",
            ManagedPath: @"C:\Games\TestGame\TestGame_Data\Managed",
            EngineType: UnityEngineType.Mono,
            GameAssemblyPaths: new[]
            {
                @"C:\Games\TestGame\TestGame_Data\Managed\Assembly-CSharp.dll",
                @"C:\Games\TestGame\TestGame_Data\Managed\UnityEngine.dll"
            },
            HasBepInExInstalled: true
        );

        return new ModScaffoldSpec(
            ModName: "SuperMod",
            PluginGuid: new PluginGuid("com.author.supermod"),
            Version: "1.2.3",
            OutputDirectory: @"C:\Mods\SuperMod",
            TargetGame: game,
            PublicizedAssemblyNames: publicized ?? new[] { "Assembly-CSharp" }
        );
    }

    [Fact]
    public void CsprojTemplateRenderer_Renders_Valid_Xml_With_Publicized_References()
    {
        var spec = CreateSampleSpec();
        var csproj = CsprojTemplateRenderer.Render(spec);

        Assert.Contains("<TargetFramework>netstandard2.1</TargetFramework>", csproj);
        Assert.Contains("<AssemblyName>SuperMod</AssemblyName>", csproj);
        Assert.Contains("<Version>1.2.3</Version>", csproj);
        Assert.Contains("<NoWarn>$(NoWarn);CA1707;NU1900;NU1901;NU1902;NU1903;NU1904</NoWarn>", csproj);
        Assert.Contains("<PackageReference Include=\"BepInEx.AssemblyPublicizer.MSBuild\"", csproj);
        Assert.Contains("<Publicize>true</Publicize>", csproj);
        Assert.Contains(@"<ModPluginsDeployDir>$(GameRootPath)\BepInEx\plugins\SuperMod\</ModPluginsDeployDir>", csproj);
    }


    [Fact]
    public void CsprojTemplateRenderer_Dynamically_Selects_Game_Assemblies_When_AssemblyCSharp_Is_Absent()
    {
        var game = new GameEnvironment(
            Id: new GameId("PotionCraft"),
            GameName: "PotionCraft",
            GameRootPath: @"C:\Games\PotionCraft",
            ManagedPath: @"C:\Games\PotionCraft\PotionCraft_Data\Managed",
            EngineType: UnityEngineType.Mono,
            GameAssemblyPaths: new[]
            {
                @"C:\Games\PotionCraft\PotionCraft_Data\Managed\PotionCraft.Scripts.dll",
                @"C:\Games\PotionCraft\PotionCraft_Data\Managed\PotionCraft.Core.dll"
            },
            HasBepInExInstalled: true
        );

        var spec = new ModScaffoldSpec(
            ModName: "LaboratoryRoom",
            PluginGuid: new PluginGuid("com.author.labroom"),
            Version: "1.0.0",
            OutputDirectory: @"C:\Mods\LaboratoryRoom",
            TargetGame: game,
            PublicizedAssemblyNames: Array.Empty<string>()
        );

        var csproj = CsprojTemplateRenderer.Render(spec);

        Assert.Contains("<Reference Include=\"PotionCraft.Scripts\">", csproj);
        Assert.Contains("<Reference Include=\"PotionCraft.Core\">", csproj);
        Assert.DoesNotContain("<Reference Include=\"Assembly-CSharp\">", csproj);
        Assert.Contains("<NoWarn>$(NoWarn);CA1707;NU1900;NU1901;NU1902;NU1903;NU1904</NoWarn>", csproj);
    }

    [Fact]
    public void CsprojTemplateRenderer_Replaces_Default_AssemblyCSharp_When_Absent_On_Disk()
    {
        var game = new GameEnvironment(
            Id: new GameId("ModularGame"),
            GameName: "ModularGame",
            GameRootPath: @"C:\Games\ModularGame",
            ManagedPath: @"C:\Games\ModularGame\ModularGame_Data\Managed",
            EngineType: UnityEngineType.Mono,
            GameAssemblyPaths: new[]
            {
                @"C:\Games\ModularGame\ModularGame_Data\Managed\ModularGame.GameLogic.dll"
            },
            HasBepInExInstalled: true
        );

        // Spec has default ["Assembly-CSharp"], but game is modular and lacks Assembly-CSharp.dll
        var spec = new ModScaffoldSpec(
            ModName: "TestMod",
            PluginGuid: new PluginGuid("com.author.testmod"),
            Version: "1.0.0",
            OutputDirectory: @"C:\Mods\TestMod",
            TargetGame: game,
            PublicizedAssemblyNames: new[] { "Assembly-CSharp" }
        );

        var csproj = CsprojTemplateRenderer.Render(spec);

        Assert.Contains("<Reference Include=\"ModularGame.GameLogic\">", csproj);
        Assert.DoesNotContain("<Reference Include=\"Assembly-CSharp\">", csproj);
    }

    [Fact]
    public void PluginTemplateRenderer_Renders_Plugin_With_Guid_And_Harmony_Bootstrap()
    {
        var spec = CreateSampleSpec();
        var plugin = PluginTemplateRenderer.Render(spec);

        Assert.Contains("[BepInPlugin(PluginGuid, PluginName, PluginVersion)]", plugin);
        Assert.Contains("public const string PluginGuid = \"com.author.supermod\";", plugin);
        Assert.Contains("public const string PluginName = \"SuperMod\";", plugin);
        Assert.Contains("public const string PluginVersion = \"1.2.3\";", plugin);
        Assert.Contains("_harmony.PatchAll();", plugin);
        Assert.Contains("_harmony?.UnpatchSelf();", plugin);
    }

    [Fact]
    public void HarmonyPatchTemplateRenderer_Renders_Sample_Patch_Structure()
    {
        var spec = CreateSampleSpec();
        var patch = HarmonyPatchTemplateRenderer.Render(spec);

        Assert.Contains("namespace SuperMod.Patches;", patch);
        Assert.Contains("// [HarmonyPatch(typeof(TargetClass), nameof(TargetClass.TargetMethod))]", patch);
        Assert.Contains("Prefix()", patch);
        Assert.Contains("Postfix()", patch);
    }

    [Fact]
    public void CsprojTemplateRenderer_When_IncludeAssetPipeline_Is_True_Includes_Loose_Assets_And_DeployTarget()
    {
        var spec = CreateSampleSpec() with { IncludeAssetPipeline = true };
        var csproj = CsprojTemplateRenderer.Render(spec);

        Assert.DoesNotContain("<EmbeddedResource", csproj);
        Assert.Contains("<None Update=\"Assets\\**\" CopyToOutputDirectory=\"PreserveNewest\" />", csproj);
        Assert.Contains("<MakeDir Directories=\"$(ModPluginsDeployDir)Assets\"", csproj);
        Assert.Contains("<ModAssets Include=\"$(MSBuildProjectDirectory)\\Assets\\**\\*.*\" />", csproj);
        Assert.Contains("DestinationFolder=\"$(ModPluginsDeployDir)Assets\\%(RecursiveDir)\"", csproj);
    }

    [Fact]
    public void CsprojTemplateRenderer_When_IncludeAssetPipeline_Is_False_Does_Not_Include_Loose_Assets_Or_DeployTarget()
    {
        var spec = CreateSampleSpec() with { IncludeAssetPipeline = false };
        var csproj = CsprojTemplateRenderer.Render(spec);

        Assert.DoesNotContain("<None Update=\"Assets\\**\"", csproj);
        Assert.DoesNotContain("<EmbeddedResource", csproj);
        Assert.DoesNotContain("$(ModPluginsDeployDir)Assets", csproj);
    }

    [Fact]
    public void AssetLoaderTemplateRenderer_Renders_Valid_Loose_Sprite_Loader()
    {
        var spec = CreateSampleSpec();
        var code = AssetLoaderTemplateRenderer.Render(spec);

        Assert.Contains("namespace SuperMod;", code);
        Assert.Contains("public static class AssetLoader", code);
        Assert.Contains("public static string ModDirectory", code);
        Assert.Contains("public static string AssetsDirectory", code);
        Assert.Contains("public static Sprite? LoadSprite(string relativePath, float pixelsPerUnit = 100f)", code);
        Assert.Contains("public static Texture2D? LoadTexture(string relativePath)", code);
        Assert.Contains("public static Sprite? LoadEmbeddedSprite(string relativePath, float pixelsPerUnit = 100f)", code);
        Assert.Contains("File.ReadAllBytes(filePath)", code);
        Assert.Contains("ImageConversion.LoadImage(texture, bytes)", code);
        Assert.DoesNotContain("GetManifestResourceStream", code);
    }

    [Fact]
    public void ModRootReadmeTemplateRenderer_Renders_Natural_Language_Without_Code_References()
    {
        var spec = CreateSampleSpec();
        var readme = ModRootReadmeTemplateRenderer.Render(spec);

        Assert.Contains("# SuperMod", readme);
        Assert.Contains("TestGame", readme);
        Assert.Contains("## Overview", readme);
        Assert.Contains("## Features & Player Experience", readme);
        Assert.Contains("## Project Structure", readme);
        Assert.Contains("Patches/README.md", readme);

        // Strict requirement: zero source code references in the root README
        Assert.DoesNotContain("class ", readme);
        Assert.DoesNotContain("void ", readme);
        Assert.DoesNotContain("namespace ", readme);
        Assert.DoesNotContain("[HarmonyPatch", readme);
        Assert.DoesNotContain("Plugin.cs", readme);
    }

    [Fact]
    public void PatchesReadmeTemplateRenderer_Renders_Implementation_Guide()
    {
        var spec = CreateSampleSpec();
        var readme = PatchesReadmeTemplateRenderer.Render(spec);

        Assert.Contains("# SuperMod - Gameplay Patches Implementation Guide", readme);
        Assert.Contains("## Architecture & Hook Mechanics", readme);
        Assert.Contains("[HarmonyPatch(typeof(TargetClass)", readme);
        Assert.Contains("[HarmonyPrefix]", readme);
        Assert.Contains("[HarmonyPostfix]", readme);
        Assert.Contains("Publicized Access", readme);
        Assert.Contains("ExamplePatch.cs", readme);
    }

    [Fact]
    public void AssetsReadmeTemplateRenderer_Renders_Loose_Asset_Guide()
    {
        var spec = CreateSampleSpec();
        var readme = AssetsReadmeTemplateRenderer.Render(spec);

        Assert.Contains("# SuperMod - Loose Assets & Resources Guide", readme);
        Assert.Contains("## Architecture & Build Deployment", readme);
        Assert.Contains("AssetLoader.LoadSprite", readme);
        Assert.Contains("AssetLoader.LoadTexture", readme);
        Assert.Contains("BepInEx/plugins/SuperMod/Assets/", readme);
    }
}
