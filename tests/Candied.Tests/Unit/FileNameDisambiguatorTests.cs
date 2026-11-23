using System.IO;
using Candied.Decompiler.Internal.Assets;
using Xunit;

namespace Candied.Tests.Unit;

public class FileNameDisambiguatorTests
{
    [Theory]
    [InlineData("ValidName", "ValidName")]
    [InlineData("name:with*illegal?chars", "name_with_illegal_chars")]
    [InlineData("", "unnamed")]
    [InlineData("   ", "unnamed")]
    [InlineData("sub/dir\\file", "sub_dir_file")]
    public void SanitizeFileName_Cleans_Invalid_Characters(string input, string expected)
    {
        var result = FileNameDisambiguator.SanitizeFileName(input);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void GetDisambiguatedFilePath_Appends_PathId_When_File_Already_Exists()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "Candied_Tests_" + Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);

        try
        {
            var firstPath = Path.Combine(tempDir, "avatar.png");
            File.WriteAllText(firstPath, "dummy");

            var secondPath = FileNameDisambiguator.GetDisambiguatedFilePath(
                tempDir, "avatar", "png", 12345, disambiguateCollisions: true);

            Assert.Equal(Path.Combine(tempDir, "avatar_12345.png"), secondPath);
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
