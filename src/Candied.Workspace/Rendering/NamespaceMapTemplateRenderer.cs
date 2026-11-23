using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Candied.Core.Models;

namespace Candied.Workspace.Rendering;

/// <summary>
/// Pure template renderer that formats a list of GameNamespaceOverview records
/// into an indexed, readable Markdown cheat-sheet.
/// </summary>
internal static class NamespaceMapTemplateRenderer
{
    public static string Render(IReadOnlyList<GameNamespaceOverview> namespaces, string gameName)
    {
        var sb = new StringBuilder();

        sb.AppendLine(CultureInfo.InvariantCulture, $"# {gameName} - Namespaces & Classes Cheat-Sheet");
        sb.AppendLine();
        sb.AppendLine("> Comprehensive index of namespaces and primary game classes.");
        sb.AppendLine();

        if (namespaces.Count == 0)
        {
            sb.AppendLine("*No namespaces indexed.*");
            return sb.ToString();
        }

        // Table of Contents
        sb.AppendLine("## Table of Contents");
        sb.AppendLine();
        foreach (var ns in namespaces)
        {
            var displayName = string.IsNullOrWhiteSpace(ns.NamespaceName) ? "(Global / Root)" : ns.NamespaceName;
            var anchor = displayName.ToLowerInvariant()
                .Replace(" ", "-", StringComparison.Ordinal)
                .Replace(".", "", StringComparison.Ordinal)
                .Replace("(", "", StringComparison.Ordinal)
                .Replace(")", "", StringComparison.Ordinal)
                .Replace("/", "", StringComparison.Ordinal);

            sb.AppendLine(CultureInfo.InvariantCulture, $"- [{displayName}](#{anchor}) *({ns.TotalTypesCount} types)*");
        }
        sb.AppendLine();

        // Namespaces Detail
        sb.AppendLine("---");
        sb.AppendLine();

        foreach (var ns in namespaces)
        {
            var displayName = string.IsNullOrWhiteSpace(ns.NamespaceName) ? "(Global / Root)" : ns.NamespaceName;
            sb.AppendLine(CultureInfo.InvariantCulture, $"### {displayName}");
            sb.AppendLine();
            sb.AppendLine(CultureInfo.InvariantCulture, $"**Total Types:** {ns.TotalTypesCount}");
            sb.AppendLine();

            if (ns.KeyTypes.Count == 0)
            {
                sb.AppendLine("*No prominent types listed.*");
            }
            else
            {
                sb.AppendLine("#### Prominent Types");
                sb.AppendLine();
                foreach (var t in ns.KeyTypes)
                {
                    sb.AppendLine(CultureInfo.InvariantCulture, $"- `{t}`");
                }

                if (ns.TotalTypesCount > ns.KeyTypes.Count)
                {
                    var remaining = ns.TotalTypesCount - ns.KeyTypes.Count;
                    sb.AppendLine();
                    sb.AppendLine(CultureInfo.InvariantCulture, $"*... and {remaining} additional types in this namespace.*");
                }
            }
            sb.AppendLine();
        }

        return sb.ToString();
    }
}
