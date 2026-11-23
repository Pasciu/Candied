using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace Candied.Workspace.Rendering;

/// <summary>
/// Pure XML helper for inspecting and mutating &lt;Reference&gt; entries inside MSBuild .csproj files
/// without performing disk I/O.
/// </summary>
internal static class CsprojReferenceMutator
{
    public readonly record struct MutationOutcome(string UpdatedXml, bool AlreadyReferenced);

    public static MutationOutcome AddReference(
        string csprojXml,
        string assemblyName,
        string dllFullPath,
        bool publicize)
    {
        var doc = XDocument.Parse(csprojXml, LoadOptions.PreserveWhitespace);
        var projectRoot = doc.Root ?? throw new InvalidOperationException("Invalid .csproj XML: missing root element.");
        var ns = projectRoot.Name.Namespace;

        var existing = projectRoot
            .Descendants(ns + "Reference")
            .FirstOrDefault(r => string.Equals(
                (string?)r.Attribute("Include"),
                assemblyName,
                StringComparison.OrdinalIgnoreCase));

        if (existing != null)
        {
            return new MutationOutcome(csprojXml, AlreadyReferenced: true);
        }

        var refItemGroup = projectRoot
            .Elements(ns + "ItemGroup")
            .FirstOrDefault(g => g.Elements(ns + "Reference").Any());

        if (refItemGroup == null)
        {
            refItemGroup = new XElement(ns + "ItemGroup");
            projectRoot.Add(new XText("\n  "), refItemGroup, new XText("\n"));
        }

        var refElement = new XElement(ns + "Reference",
            new XAttribute("Include", assemblyName),
            new XText("\n      "),
            new XElement(ns + "HintPath", dllFullPath));

        if (publicize)
        {
            refElement.Add(new XText("\n      "), new XElement(ns + "Publicize", "true"));
        }

        refElement.Add(
            new XText("\n      "),
            new XElement(ns + "Private", "false"),
            new XText("\n    "));

        refItemGroup.Add(new XText("  "), refElement, new XText("\n  "));

        return new MutationOutcome(doc.ToString(SaveOptions.DisableFormatting), AlreadyReferenced: false);
    }

    public static string? TryExtractManagedDirectory(string csprojXml)
    {
        if (string.IsNullOrWhiteSpace(csprojXml))
        {
            return null;
        }

        try
        {
            var doc = XDocument.Parse(csprojXml);
            var root = doc.Root;
            if (root == null) return null;
            var ns = root.Name.Namespace;

            // Prefer existing <HintPath> pointing into a Managed/ directory
            foreach (var hint in root.Descendants(ns + "HintPath"))
            {
                var value = hint.Value?.Trim();
                if (string.IsNullOrWhiteSpace(value)) continue;
                var dir = Path.GetDirectoryName(value);
                if (!string.IsNullOrWhiteSpace(dir) &&
                    string.Equals(Path.GetFileName(dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)), "Managed", StringComparison.OrdinalIgnoreCase))
                {
                    return dir;
                }
            }
        }
        catch
        {
            // Ignore malformed XML
        }

        return null;
    }

    public static string? TryExtractGameRootPath(string csprojXml)
    {
        if (string.IsNullOrWhiteSpace(csprojXml))
        {
            return null;
        }

        try
        {
            var doc = XDocument.Parse(csprojXml);
            var root = doc.Root;
            if (root == null) return null;
            var ns = root.Name.Namespace;

            var gameRoot = root.Descendants(ns + "GameRootPath").FirstOrDefault()?.Value?.Trim();
            return string.IsNullOrWhiteSpace(gameRoot) ? null : gameRoot;
        }
        catch
        {
            return null;
        }
    }
}
