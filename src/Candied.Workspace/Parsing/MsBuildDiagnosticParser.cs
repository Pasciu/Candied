using System.Text.RegularExpressions;
using Candied.Core.Models;

namespace Candied.Workspace.Parsing;

internal static partial class MsBuildDiagnosticParser
{
    private static readonly string[] LineSeparators = ["\r\n", "\r", "\n"];

    [GeneratedRegex(
        @"^(?:(?<file>.+?)(?:\((?<line>\d+)(?:,\s*\d+)?\))?\s*:\s*)?(?<severity>warning|error|info)\s+(?<code>[A-Za-z0-9_]+)\s*:\s*(?<message>.*?)(?:\s*\[.*?\])?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DiagnosticRegex();

    public static List<BuildDiagnostic> ParseDiagnostics(string output)
    {
        var diagnostics = new List<BuildDiagnostic>();
        if (string.IsNullOrWhiteSpace(output))
        {
            return diagnostics;
        }

        var lines = output.Split(LineSeparators, StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            var match = DiagnosticRegex().Match(trimmed);
            if (!match.Success)
            {
                continue;
            }

            var severityStr = match.Groups["severity"].Value;
            var severity = severityStr.Equals("error", StringComparison.OrdinalIgnoreCase)
                ? DiagnosticSeverity.Error
                : severityStr.Equals("warning", StringComparison.OrdinalIgnoreCase)
                    ? DiagnosticSeverity.Warning
                    : DiagnosticSeverity.Info;

            var file = match.Groups["file"].Value.Trim();
            var code = match.Groups["code"].Value.Trim();
            var message = match.Groups["message"].Value.Trim();
            var lineNum = int.TryParse(match.Groups["line"].Value, out var l) ? l : 0;

            diagnostics.Add(new BuildDiagnostic(
                Code: code,
                Message: message,
                FilePath: file,
                LineNumber: lineNum,
                Severity: severity
            ));
        }

        return diagnostics.Distinct().ToList();
    }
}
