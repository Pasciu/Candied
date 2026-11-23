using Candied.Core.Models;
using Candied.Workspace.Parsing;
using Xunit;

namespace Candied.Tests.Unit;

public class MsBuildDiagnosticParserTests
{
    [Fact]
    public void ParseDiagnostics_Returns_Empty_For_Empty_Input()
    {
        var diagnostics = MsBuildDiagnosticParser.ParseDiagnostics(string.Empty);
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void ParseDiagnostics_Parses_Errors_And_Warnings_Correctly()
    {
        var input = """
Microsoft (R) Build Engine version 17.8.0
C:\Game\Mods\MyMod\Plugin.cs(42,15): error CS0103: The name 'NonExistent' does not exist in the current context [C:\Game\Mods\MyMod\MyMod.csproj]
C:\Game\Mods\MyMod\Patches\Patch.cs(12): warning CS0168: The variable 'ex' is declared but never used
/home/user/mod/Source.cs: error CS1002: ; expected
""";

        var diagnostics = MsBuildDiagnosticParser.ParseDiagnostics(input);

        Assert.Equal(3, diagnostics.Count);

        var first = diagnostics[0];
        Assert.Equal("CS0103", first.Code);
        Assert.Equal(DiagnosticSeverity.Error, first.Severity);
        Assert.Equal(@"C:\Game\Mods\MyMod\Plugin.cs", first.FilePath);
        Assert.Equal(42, first.LineNumber);
        Assert.Contains("The name 'NonExistent' does not exist", first.Message);

        var second = diagnostics[1];
        Assert.Equal("CS0168", second.Code);
        Assert.Equal(DiagnosticSeverity.Warning, second.Severity);
        Assert.Equal(@"C:\Game\Mods\MyMod\Patches\Patch.cs", second.FilePath);
        Assert.Equal(12, second.LineNumber);

        var third = diagnostics[2];
        Assert.Equal("CS1002", third.Code);
        Assert.Equal(DiagnosticSeverity.Error, third.Severity);
        Assert.Equal("/home/user/mod/Source.cs", third.FilePath);
        Assert.Equal(0, third.LineNumber);
    }

    [Fact]
    public void ParseDiagnostics_Deduplicates_Duplicate_Messages()
    {
        var input = """
C:\Game\Plugin.cs(10,5): error CS0103: The name 'X' does not exist
C:\Game\Plugin.cs(10,5): error CS0103: The name 'X' does not exist
""";

        var diagnostics = MsBuildDiagnosticParser.ParseDiagnostics(input);
        Assert.Single(diagnostics);
    }
}
