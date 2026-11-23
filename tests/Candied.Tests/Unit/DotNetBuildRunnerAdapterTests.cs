using Candied.Core.Models;
using Candied.Workspace.Adapters;
using Xunit;

namespace Candied.Tests.Unit;

public sealed class DotNetBuildRunnerAdapterTests
{
    [Fact]
    public void ParseDiagnostics_Correctly_Parses_Errors_And_Warnings()
    {
        // Arrange
        var sampleOutput = """
C:\Mods\SpeedMod\Plugin.cs(14,20): error CS0103: The name 'Foo' does not exist in the current context [C:\Mods\SpeedMod\SpeedMod.csproj]
C:\Mods\SpeedMod\Plugin.cs(25,12): warning CS0168: The variable 'ex' is declared but never used [C:\Mods\SpeedMod\SpeedMod.csproj]
MSBUILD : error MSB1009: Project file does not exist.
""";

        // Act
        var diagnostics = DotNetBuildRunnerAdapter.ParseDiagnostics(sampleOutput);

        // Assert
        Assert.Equal(3, diagnostics.Count);

        var first = diagnostics[0];
        Assert.Equal("CS0103", first.Code);
        Assert.Equal(DiagnosticSeverity.Error, first.Severity);
        Assert.Equal(14, first.LineNumber);
        Assert.Equal(@"C:\Mods\SpeedMod\Plugin.cs", first.FilePath);
        Assert.Equal("The name 'Foo' does not exist in the current context", first.Message);

        var second = diagnostics[1];
        Assert.Equal("CS0168", second.Code);
        Assert.Equal(DiagnosticSeverity.Warning, second.Severity);
        Assert.Equal(25, second.LineNumber);
        Assert.Equal("The variable 'ex' is declared but never used", second.Message);

        var third = diagnostics[2];
        Assert.Equal("MSB1009", third.Code);
        Assert.Equal(DiagnosticSeverity.Error, third.Severity);
        Assert.Equal("Project file does not exist.", third.Message);
    }

    [Fact]
    public async Task BuildModAsync_Fails_When_ProjectFile_Does_Not_Exist()
    {
        var runner = new DotNetBuildRunnerAdapter();
        var fakePath = Path.Combine(Path.GetTempPath(), "NonExistent_" + Guid.NewGuid().ToString("N") + ".csproj");

        var result = await runner.BuildModAsync(fakePath);

        Assert.True(result.IsFailure);
        Assert.Contains("does not exist", result.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BuildModAsync_Fails_When_ProjectFile_Path_Is_Empty()
    {
        var runner = new DotNetBuildRunnerAdapter();

        var result = await runner.BuildModAsync(string.Empty);

        Assert.True(result.IsFailure);
        Assert.Contains("cannot be empty", result.Error.Message, StringComparison.OrdinalIgnoreCase);
    }
}
