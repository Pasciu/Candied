using Candied.Core.Models;
using Xunit;

namespace Candied.Tests.Unit;

public class CandiedStorageLayoutTests
{
    [Fact]
    public void StorageLayout_Generates_Correct_Zone_Paths()
    {
        var layout = new CandiedStorageLayout("C:/MyProject");

        Assert.Equal("C:/MyProject/.candied", layout.CandiedRoot);
        Assert.Equal("C:/MyProject/mods", layout.ModsRoot);
        Assert.Equal("C:/MyProject/.candied/games/Potion-Craft", layout.GetGameArtifactsDir("Potion Craft"));
        Assert.Equal("C:/MyProject/.candied/games/Potion-Craft/decompiled", layout.GetDecompiledDir("Potion Craft"));
        Assert.Equal("C:/MyProject/.candied/games/Potion-Craft/index.db", layout.GetDatabasePath("Potion Craft"));
        Assert.Equal("C:/MyProject/.candied/games/Potion-Craft/docs", layout.GetDocsDir("Potion Craft"));
        Assert.Equal("C:/MyProject/.candied/games/Potion-Craft/docs/GAME_OVERVIEW.md", layout.GetOverviewDocPath("Potion Craft"));
        Assert.Equal("C:/MyProject/.candied/games/Potion-Craft/docs/NAMESPACES.md", layout.GetNamespacesDocPath("Potion Craft"));
        Assert.Equal("C:/MyProject/.candied/games/Potion-Craft/docs/SYMBOLS_INDEX.json", layout.GetSymbolsIndexJsonPath("Potion Craft"));
        Assert.Equal("C:/MyProject/mods/LaboratoryRoom", layout.GetModProjectDir("LaboratoryRoom"));
    }

    [Theory]
    [InlineData("Potion Craft", "Potion-Craft")]
    [InlineData("Game!@#$%^&*()_+Name", "Game_Name")]
    [InlineData("Hollow Knight: Silksong", "Hollow-Knight-Silksong")]
    [InlineData("", "Default")]
    [InlineData("   ", "Default")]
    public void Sanitize_Cleans_Game_And_Mod_Names(string input, string expected)
    {
        var result = CandiedStorageLayout.Sanitize(input);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Combine_Handles_Various_Slashes()
    {
        Assert.Equal("root/sub/file.txt", CandiedStorageLayout.Combine("root/", "/sub/", "/file.txt"));
        Assert.Equal("root/sub", CandiedStorageLayout.Combine("root\\", "\\sub"));
    }
}
