using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Candied.Core.Models;

namespace Candied.Core.Analysis;

/// <summary>
/// Pure analyzer that extracts missing assembly names from Roslyn compiler diagnostics
/// (specifically CS0012 across all localized MSBuild messages) so build tools can suggest
/// verified DLL paths from the target game's Managed folder.
/// </summary>
public static partial class MissingReferenceHintAnalyzer
{
    // Matches quoted assembly display names like: 'UnityEngine.AudioModule, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null'
    [GeneratedRegex(@"'([A-Za-z0-9_.\-]+),\s*Version=", RegexOptions.CultureInvariant)]
    private static partial Regex AssemblyDisplayNameRegex();

    public static IReadOnlyList<string> ExtractMissingAssemblyNames(IReadOnlyList<BuildDiagnostic> diagnostics)
    {
        if (diagnostics == null || diagnostics.Count == 0)
        {
            return Array.Empty<string>();
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();

        foreach (var diag in diagnostics)
        {
            if (diag == null || !string.Equals(diag.Code, "CS0012", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(diag.Message))
            {
                continue;
            }

            var match = AssemblyDisplayNameRegex().Match(diag.Message);
            if (match.Success)
            {
                var asmName = match.Groups[1].Value.Trim();
                if (!string.IsNullOrEmpty(asmName) && seen.Add(asmName))
                {
                    result.Add(asmName);
                }
            }
        }

        return result;
    }
}
