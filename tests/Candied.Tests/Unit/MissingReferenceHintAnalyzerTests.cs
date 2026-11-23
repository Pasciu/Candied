using Candied.Core.Analysis;
using Candied.Core.Models;
using Xunit;

namespace Candied.Tests.Unit;

public class MissingReferenceHintAnalyzerTests
{
    [Fact]
    public void ExtractMissingAssemblyNames_ParsesEnglishAndItalianCs0012Messages()
    {
        var diagnostics = new List<BuildDiagnostic>
        {
            new("CS0012",
                "The type 'Animator' is defined in an assembly that is not referenced. You must add a reference to assembly 'UnityEngine.AnimationModule, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null'.",
                "Plugin.cs",
                12,
                DiagnosticSeverity.Error),
            new("CS0012",
                "Il tipo 'TMP_Text' è definito in un assembly a cui non viene fatto riferimento. Aggiungere un riferimento all'assembly 'Unity.TextMeshPro, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null'.",
                "Plugin.cs",
                25,
                DiagnosticSeverity.Error),
            new("CS0012",
                "The type 'AnimationClip' is defined in an assembly that is not referenced. You must add a reference to assembly 'UnityEngine.AnimationModule, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null'.",
                "Plugin.cs",
                30,
                DiagnosticSeverity.Error),
            new("CS0103",
                "The name 'foo' does not exist in the current context",
                "Plugin.cs",
                40,
                DiagnosticSeverity.Error)
        };

        var missing = MissingReferenceHintAnalyzer.ExtractMissingAssemblyNames(diagnostics);

        Assert.Equal(2, missing.Count);
        Assert.Contains("UnityEngine.AnimationModule", missing);
        Assert.Contains("Unity.TextMeshPro", missing);
    }

    [Fact]
    public void ExtractMissingAssemblyNames_ReturnsEmptyWhenNoCs0012()
    {
        var diagnostics = new List<BuildDiagnostic>
        {
            new("CS0246", "The type or namespace name 'Foo' could not be found", "Plugin.cs", 5, DiagnosticSeverity.Error)
        };

        var missing = MissingReferenceHintAnalyzer.ExtractMissingAssemblyNames(diagnostics);
        Assert.Empty(missing);
    }
}
