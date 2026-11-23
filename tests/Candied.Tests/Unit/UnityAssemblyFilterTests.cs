using Candied.Decompiler.Internal;
using Xunit;

namespace Candied.Tests.Unit;

public class UnityAssemblyFilterTests
{
    [Theory]
    [InlineData("mscorlib.dll", true)]
    [InlineData("netstandard.dll", true)]
    [InlineData("System.dll", true)]
    [InlineData("System.Core.dll", true)]
    [InlineData("System.Data.dll", true)]
    [InlineData("Microsoft.CSharp.dll", true)]
    [InlineData("UnityEngine.dll", true)]
    [InlineData("UnityEngine.CoreModule.dll", true)]
    [InlineData("UnityEngine.UI.dll", true)]
    [InlineData("Unity.Timeline.dll", true)]
    [InlineData("Unity.TextMeshPro.dll", true)]
    [InlineData("mcs.dll", true)]
    [InlineData("Mono.CSharp.dll", true)]
    [InlineData("Mono.Security.dll", true)]
    [InlineData("Newtonsoft.Json.dll", true)]
    [InlineData("DOTween.dll", true)]
    [InlineData("DOTweenPro.dll", true)]
    [InlineData("Steamworks.NET.dll", true)]
    [InlineData("ThirdParty.DOTween.dll", true)]
    [InlineData("zxing.unity.dll", true)]
    [InlineData("QFSW.QC.dll", true)]
    [InlineData("DarkScreenSystem.dll", true)]
    [InlineData("PotionCraft.ConverterFrom050To051.dll", true)]
    [InlineData("Assembly-CSharp.dll", false)]
    [InlineData("Assembly-CSharp-firstpass.dll", false)]
    [InlineData("PotionCraft.Scripts.dll", false)]
    [InlineData("GameLogic.dll", false)]
    [InlineData("CustomGameplay.dll", false)]
    public void IsExcludedAssembly_Correctly_Identifies_Assemblies(string assemblyName, bool expectedExcluded)
    {
        var result = UnityAssemblyFilter.IsExcludedAssembly(assemblyName);
        Assert.Equal(expectedExcluded, result);
    }

    [Fact]
    public void FilterGameAssemblies_Prioritizes_AssemblyCSharp_And_Excludes_Framework()
    {
        var paths = new[]
        {
            @"C:\Games\HollowKnight\Managed\UnityEngine.dll",
            @"C:\Games\HollowKnight\Managed\Assembly-CSharp.dll",
            @"C:\Games\HollowKnight\Managed\mscorlib.dll",
            @"C:\Games\HollowKnight\Managed\Assembly-CSharp-firstpass.dll",
            @"C:\Games\HollowKnight\Managed\System.dll"
        };

        var filtered = UnityAssemblyFilter.FilterGameAssemblies(paths, "HollowKnight");

        Assert.Equal(2, filtered.Count);
        Assert.Equal(@"C:\Games\HollowKnight\Managed\Assembly-CSharp.dll", filtered[0]);
        Assert.Equal(@"C:\Games\HollowKnight\Managed\Assembly-CSharp-firstpass.dll", filtered[1]);
    }

    [Fact]
    public void FilterGameAssemblies_Handles_Modular_Games_Without_AssemblyCSharp_Like_PotionCraft()
    {
        var paths = new[]
        {
            @"C:\Games\Potion Craft\Managed\UnityEngine.dll",
            @"C:\Games\Potion Craft\Managed\PotionCraft.Settings.dll",
            @"C:\Games\Potion Craft\Managed\mscorlib.dll",
            @"C:\Games\Potion Craft\Managed\PotionCraft.Scripts.dll",
            @"C:\Games\Potion Craft\Managed\DOTween.dll",
            @"C:\Games\Potion Craft\Managed\mcs.dll",
            @"C:\Games\Potion Craft\Managed\PotionCraft.Core.dll",
            @"C:\Games\Potion Craft\Managed\CustomModHelper.dll"
        };

        var filtered = UnityAssemblyFilter.FilterGameAssemblies(paths, "PotionCraft");

        // Should exclude UnityEngine, mscorlib, DOTween, mcs
        // Should keep PotionCraft.Core, PotionCraft.Scripts, PotionCraft.Settings (prioritized and sorted)
        // followed by CustomModHelper
        Assert.Equal(4, filtered.Count);
        Assert.Equal(@"C:\Games\Potion Craft\Managed\PotionCraft.Core.dll", filtered[0]);
        Assert.Equal(@"C:\Games\Potion Craft\Managed\PotionCraft.Scripts.dll", filtered[1]);
        Assert.Equal(@"C:\Games\Potion Craft\Managed\PotionCraft.Settings.dll", filtered[2]);
        Assert.Equal(@"C:\Games\Potion Craft\Managed\CustomModHelper.dll", filtered[3]);
    }

    [Fact]
    public void FilterGameAssemblies_Matches_GameName_With_Spaces_To_Compact_Prefix()
    {
        var paths = new[]
        {
            @"C:\Games\Potion Craft\Managed\UnityEngine.dll",
            @"C:\Games\Potion Craft\Managed\PotionCraft.Settings.dll",
            @"C:\Games\Potion Craft\Managed\mscorlib.dll",
            @"C:\Games\Potion Craft\Managed\PotionCraft.Scripts.dll",
            @"C:\Games\Potion Craft\Managed\ThirdParty.DOTween.dll",
            @"C:\Games\Potion Craft\Managed\PotionCraft.ConverterFrom050To051.dll",
            @"C:\Games\Potion Craft\Managed\PotionCraft.Core.dll"
        };

        // Pass game name WITH space as detected from "Potion Craft_Data"
        var filtered = UnityAssemblyFilter.FilterGameAssemblies(paths, "Potion Craft");

        Assert.Equal(3, filtered.Count);
        Assert.Equal(@"C:\Games\Potion Craft\Managed\PotionCraft.Core.dll", filtered[0]);
        Assert.Equal(@"C:\Games\Potion Craft\Managed\PotionCraft.Scripts.dll", filtered[1]);
        Assert.Equal(@"C:\Games\Potion Craft\Managed\PotionCraft.Settings.dll", filtered[2]);
    }

    [Fact]
    public void FilterGameAssemblies_FallsBack_To_All_When_Everything_Is_Excluded()
    {
        var paths = new[]
        {
            @"C:\Games\Test\Managed\mscorlib.dll",
            @"C:\Games\Test\Managed\System.dll"
        };

        var filtered = UnityAssemblyFilter.FilterGameAssemblies(paths, "Test");

        Assert.Equal(2, filtered.Count);
        Assert.Contains(@"C:\Games\Test\Managed\mscorlib.dll", filtered);
        Assert.Contains(@"C:\Games\Test\Managed\System.dll", filtered);
    }

    [Fact]
    public void FilterGameAssemblies_Returns_Empty_When_Input_Is_Empty()
    {
        var filtered = UnityAssemblyFilter.FilterGameAssemblies(Array.Empty<string>(), "Game");
        Assert.Empty(filtered);
    }

    [Fact]
    public void ClassifyAssemblies_Properly_Categorizes_Tiers_And_Flags_Decompiled()
    {
        var paths = new[]
        {
            @"C:\Games\PotionCraft\Managed\PotionCraft.Core.dll",
            @"C:\Games\PotionCraft\Managed\Assembly-CSharp.dll",
            @"C:\Games\PotionCraft\Managed\CustomModLogic.dll",
            @"C:\Games\PotionCraft\Managed\UnityEngine.dll",
            @"C:\Games\PotionCraft\Managed\DOTween.dll"
        };

        var classified = UnityAssemblyFilter.ClassifyAssemblies(
            paths,
            "PotionCraft",
            fileName => fileName.Equals("PotionCraft.Core.dll", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(5, classified.Count);

        // CoreGame tier
        var coreAssemblies = classified.Where(a => a.Tier == Candied.Core.Models.AssemblyTier.CoreGame).ToList();
        Assert.Equal(2, coreAssemblies.Count);
        Assert.Contains(coreAssemblies, a => a.Name == "PotionCraft.Core.dll" && a.IsDecompiled);
        Assert.Contains(coreAssemblies, a => a.Name == "Assembly-CSharp.dll" && !a.IsDecompiled);

        // GameModular tier
        var modularAssemblies = classified.Where(a => a.Tier == Candied.Core.Models.AssemblyTier.GameModular).ToList();
        Assert.Single(modularAssemblies);
        Assert.Equal("CustomModLogic.dll", modularAssemblies[0].Name);

        // AuxiliaryOrThirdParty tier
        var thirdParty = classified.Where(a => a.Tier == Candied.Core.Models.AssemblyTier.AuxiliaryOrThirdParty).ToList();
        Assert.Equal(2, thirdParty.Count);
        Assert.Contains(thirdParty, a => a.Name == "UnityEngine.dll");
        Assert.Contains(thirdParty, a => a.Name == "DOTween.dll");
    }
}
