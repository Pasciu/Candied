using Candied.Core.Models;
using Xunit;

namespace Candied.Tests.Unit;

public class CandiedStorageLayoutTests
{
    [Fact]
    public void StorageLayout_Generates_Correct_ThreeZone_Paths()
    {
        var layout = new CandiedStorageLayout("C:/MyWorkspace");

        Assert.Equal("C:/MyWorkspace/.candied", layout.CandiedRoot);
        Assert.Equal("C:/MyWorkspace/mods", layout.ModsRoot);
        Assert.Equal("C:/MyWorkspace/.candied/games/Sample-Game", layout.GetTargetArtifactsDir("Sample Game"));
        Assert.Equal("C:/MyWorkspace/.candied/games/Sample-Game/extracted", layout.GetExtractedCodeDir("Sample Game"));
        Assert.Equal("C:/MyWorkspace/.candied/games/Sample-Game/extracted/Assembly-CSharp", layout.GetExtractedCodeDir("Sample Game", "Assembly-CSharp"));
        Assert.Equal("C:/MyWorkspace/.candied/games/Sample-Game/index.db", layout.GetDatabasePath("Sample Game"));
        Assert.Equal("C:/MyWorkspace/.candied/games/Sample-Game/controls.json", layout.GetControlProfilePath("Sample Game"));
        Assert.Equal("C:/MyWorkspace/.candied/games/Sample-Game/docs", layout.GetDocsDir("Sample Game"));
        Assert.Equal("C:/MyWorkspace/.candied/games/Sample-Game/docs/TARGET_OVERVIEW.md", layout.GetOverviewDocPath("Sample Game"));
        Assert.Equal("C:/MyWorkspace/.candied/games/Sample-Game/docs/NAMESPACES.md", layout.GetNamespacesDocPath("Sample Game"));
        Assert.Equal("C:/MyWorkspace/.candied/games/Sample-Game/docs/SYMBOLS_INDEX.json", layout.GetSymbolsIndexJsonPath("Sample Game"));
        Assert.Equal("C:/MyWorkspace/mods/LaboratoryRoom", layout.GetModProjectDir("LaboratoryRoom"));
    }

    [Theory]
    [InlineData("Sample Game", "Sample-Game")]
    [InlineData("Game!@#$%^&*()_+Name", "Game_Name")]
    [InlineData("Adventure Quest: Chapter Two", "Adventure-Quest-Chapter-Two")]
    [InlineData("", "Default")]
    [InlineData("   ", "Default")]
    public void Sanitize_Cleans_Target_And_Mod_Names(string input, string expected)
    {
        string result = CandiedStorageLayout.Sanitize(input);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Combine_Handles_Various_Slashes()
    {
        Assert.Equal("root/sub/file.txt", CandiedStorageLayout.Combine("root/", "/sub/", "/file.txt"));
        Assert.Equal("root/sub", CandiedStorageLayout.Combine("root\\", "\\sub"));
    }
}
