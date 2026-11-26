using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Candied.Core.Models;

namespace Candied.Platforms.Unity.Mono.Internal;

internal static partial class MsBuildDiagnosticParser
{
    private static readonly string[] LineSeparators = ["\r\n", "\r", "\n"];

    [GeneratedRegex(
        @"^(?:(?<file>.+?)(?:\((?<line>\d+)(?:,\s*\d+)?\))?\s*:\s*)?(?<severity>warning|error|info)\s+(?<code>[A-Za-z0-9_]+)\s*:\s*(?<message>.*?)(?:\s*\[.*?\])?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DiagnosticRegex();

    [GeneratedRegex(@"'([A-Za-z0-9_.\-]+),\s*Version=", RegexOptions.CultureInvariant)]
    private static partial Regex AssemblyDisplayNameRegex();

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
            var match = DiagnosticRegex().Match(line.Trim());
            if (!match.Success) continue;

            string severityStr = match.Groups["severity"].Value;
            var severity = severityStr.Equals("error", StringComparison.OrdinalIgnoreCase)
                ? DiagnosticSeverity.Error
                : severityStr.Equals("warning", StringComparison.OrdinalIgnoreCase)
                    ? DiagnosticSeverity.Warning
                    : DiagnosticSeverity.Info;

            diagnostics.Add(new BuildDiagnostic(
                Code: match.Groups["code"].Value.Trim(),
                Message: match.Groups["message"].Value.Trim(),
                FilePath: match.Groups["file"].Value.Trim(),
                LineNumber: int.TryParse(match.Groups["line"].Value, CultureInfo.InvariantCulture, out int l) ? l : 0,
                Severity: severity));
        }

        return diagnostics.Distinct().ToList();
    }

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
            if (diag == null || !string.Equals(diag.Code, "CS0012", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(diag.Message))
            {
                continue;
            }

            var match = AssemblyDisplayNameRegex().Match(diag.Message);
            if (match.Success)
            {
                string asmName = match.Groups[1].Value.Trim();
                if (!string.IsNullOrEmpty(asmName) && seen.Add(asmName))
                {
                    result.Add(asmName);
                }
            }
        }

        return result;
    }
}

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
            .FirstOrDefault(r => string.Equals((string?)r.Attribute("Include"), assemblyName, StringComparison.OrdinalIgnoreCase));

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
        if (string.IsNullOrWhiteSpace(csprojXml)) return null;
        try
        {
            var doc = XDocument.Parse(csprojXml);
            var root = doc.Root;
            if (root == null) return null;
            var ns = root.Name.Namespace;

            foreach (var hint in root.Descendants(ns + "HintPath"))
            {
                string? value = hint.Value?.Trim();
                if (string.IsNullOrWhiteSpace(value)) continue;
                string? dir = Path.GetDirectoryName(value);
                if (!string.IsNullOrWhiteSpace(dir) &&
                    string.Equals(Path.GetFileName(dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)), "Managed", StringComparison.OrdinalIgnoreCase))
                {
                    return dir;
                }
            }
        }
        catch
        {
        }

        return null;
    }

    public static string? TryExtractGameRootPath(string csprojXml)
    {
        if (string.IsNullOrWhiteSpace(csprojXml)) return null;
        try
        {
            var doc = XDocument.Parse(csprojXml);
            var root = doc.Root;
            if (root == null) return null;
            var ns = root.Name.Namespace;
            string? gameRoot = root.Descendants(ns + "GameRootPath").FirstOrDefault()?.Value?.Trim();
            return string.IsNullOrWhiteSpace(gameRoot) ? null : gameRoot;
        }
        catch
        {
            return null;
        }
    }
}

internal static class UnityMonoTemplateRenderers
{
    public static string RenderBepInExConfig(bool enableConsole) => $"""
[Caching]
EnableAssemblyCache = true

[Logging.Console]
Enabled = {enableConsole.ToString().ToLowerInvariant()}
PreventClose = false
ShiftJisEncoding = false
StandardOutType = Auto
LogLevels = Fatal, Error, Warning, Message, Info

[Logging.Disk]
WriteUnityLog = true
AppendLog = false
Enabled = true
LogLevels = Fatal, Error, Warning, Message, Info
""";

    public static string RenderCsproj(ModScaffoldSpec spec)
    {
        string gameRoot = Directory.Exists(spec.Target.TargetPath)
            ? spec.Target.TargetPath
            : (Path.GetDirectoryName(spec.Target.TargetPath) ?? spec.Target.TargetPath);

        string managedPath = spec.Target.Metadata.TryGetValue("managedPath", out var mp) && !string.IsNullOrWhiteSpace(mp)
            ? mp
            : Path.Combine(gameRoot, $"{spec.Target.Name}_Data", "Managed");

        var assemblyPaths = spec.Target.CodeModules
            .Where(m => m.Tier != CodeModuleTier.AuxiliaryOrThirdParty)
            .Select(m => m.FullPath)
            .ToList();

        if (Directory.Exists(managedPath))
        {
            foreach (var coreDll in new[] { "UnityEngine.dll", "UnityEngine.CoreModule.dll" })
            {
                string full = Path.Combine(managedPath, coreDll);
                if (File.Exists(full) && !assemblyPaths.Contains(full, StringComparer.OrdinalIgnoreCase))
                {
                    assemblyPaths.Add(full);
                }
            }

            if (spec.IncludeAssetPipeline)
            {
                string imgConv = Path.Combine(managedPath, "UnityEngine.ImageConversionModule.dll");
                if (File.Exists(imgConv) && !assemblyPaths.Contains(imgConv, StringComparer.OrdinalIgnoreCase))
                {
                    assemblyPaths.Add(imgConv);
                }
            }
        }

        var publicizedSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (spec.PlatformOptions != null && spec.PlatformOptions.TryGetValue("publicize", out var pubCsv) && !string.IsNullOrWhiteSpace(pubCsv))
        {
            foreach (var p in pubCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                publicizedSet.Add(p);
            }
        }

        if (publicizedSet.Count == 0)
        {
            foreach (var path in assemblyPaths)
            {
                string name = Path.GetFileNameWithoutExtension(path);
                if (!name.StartsWith("UnityEngine", StringComparison.OrdinalIgnoreCase) &&
                    !name.Equals("mscorlib", StringComparison.OrdinalIgnoreCase) &&
                    !name.StartsWith("System", StringComparison.OrdinalIgnoreCase))
                {
                    publicizedSet.Add(name);
                }
            }

            if (publicizedSet.Count == 0)
            {
                publicizedSet.Add("Assembly-CSharp");
            }
        }

        var referencesSb = new StringBuilder();
        foreach (var path in assemblyPaths)
        {
            string asmName = Path.GetFileNameWithoutExtension(path);
            bool isPublicized = publicizedSet.Contains(asmName);
            if (isPublicized || asmName.Equals("Assembly-CSharp", StringComparison.OrdinalIgnoreCase) || asmName.StartsWith("UnityEngine", StringComparison.OrdinalIgnoreCase))
            {
                referencesSb.AppendLine(CultureInfo.InvariantCulture, $"    <Reference Include=\"{asmName}\">");
                referencesSb.AppendLine(CultureInfo.InvariantCulture, $"      <HintPath>{path}</HintPath>");
                if (isPublicized)
                {
                    referencesSb.AppendLine("      <Publicize>true</Publicize>");
                }

                referencesSb.AppendLine("      <Private>false</Private>");
                referencesSb.AppendLine("    </Reference>");
            }
        }

        foreach (var pubName in publicizedSet)
        {
            if (!assemblyPaths.Any(p => Path.GetFileNameWithoutExtension(p).Equals(pubName, StringComparison.OrdinalIgnoreCase)))
            {
                string fallbackPath = !string.IsNullOrWhiteSpace(managedPath)
                    ? Path.Combine(managedPath, $"{pubName}.dll")
                    : $"{pubName}.dll";

                referencesSb.AppendLine(CultureInfo.InvariantCulture, $"    <Reference Include=\"{pubName}\">");
                referencesSb.AppendLine(CultureInfo.InvariantCulture, $"      <HintPath>{fallbackPath}</HintPath>");
                referencesSb.AppendLine("      <Publicize>true</Publicize>");
                referencesSb.AppendLine("      <Private>false</Private>");
                referencesSb.AppendLine("    </Reference>");
            }
        }

        string version = string.IsNullOrWhiteSpace(spec.Version) ? "1.0.0" : spec.Version;
        string looseAssetsXml = spec.IncludeAssetPipeline
            ? """

  <ItemGroup>
    <None Update="Assets\**" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
"""
            : string.Empty;

        string deployAssetsTargetXml = spec.IncludeAssetPipeline
            ? """

    <MakeDir Directories="$(ModPluginsDeployDir)Assets" Condition="Exists('$(MSBuildProjectDirectory)\Assets')" />
    <ItemGroup Condition="Exists('$(MSBuildProjectDirectory)\Assets')">
      <ModAssets Include="$(MSBuildProjectDirectory)\Assets\**\*.*" />
    </ItemGroup>
    <Copy SourceFiles="@(ModAssets)"
          DestinationFolder="$(ModPluginsDeployDir)Assets\%(RecursiveDir)"
          SkipUnchangedFiles="true"
          Condition="Exists('$(MSBuildProjectDirectory)\Assets')" />
"""
            : string.Empty;

        return $"""
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.1</TargetFramework>
    <AssemblyName>{spec.ModName}</AssemblyName>
    <Product>{spec.ModName}</Product>
    <Version>{version}</Version>
    <Nullable>enable</Nullable>
    <LangVersion>latest</LangVersion>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <NoWarn>$(NoWarn);CA1707;NU1900;NU1901;NU1902;NU1903;NU1904</NoWarn>
    <RestoreAdditionalProjectSources>
      https://nuget.bepinex.dev/v3/index.json
    </RestoreAdditionalProjectSources>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="BepInEx.Core" Version="5.4.21" />
    <PackageReference Include="BepInEx.AssemblyPublicizer.MSBuild" Version="0.4.3" PrivateAssets="all" />
  </ItemGroup>

  <ItemGroup>
{referencesSb.ToString().TrimEnd()}
  </ItemGroup>{looseAssetsXml}

  <PropertyGroup>
    <GameRootPath>{gameRoot}</GameRootPath>
    <ModPluginsDeployDir>$(GameRootPath)\BepInEx\plugins\{spec.ModName}\</ModPluginsDeployDir>
  </PropertyGroup>

  <Target Name="DeployToBepInEx" AfterTargets="Build" Condition="Exists('$(GameRootPath)')">
    <MakeDir Directories="$(ModPluginsDeployDir)" />
    <Copy SourceFiles="$(TargetPath)" DestinationFolder="$(ModPluginsDeployDir)" SkipUnchangedFiles="true" />{deployAssetsTargetXml}
  </Target>
</Project>
""";
    }

    public static string RenderPlugin(ModScaffoldSpec spec)
    {
        string version = string.IsNullOrWhiteSpace(spec.Version) ? "1.0.0" : spec.Version;
        return $$"""
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

namespace {{spec.ModName}};

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public class Plugin : BaseUnityPlugin
{
    public const string PluginGuid = "{{spec.ModId.Value}}";
    public const string PluginName = "{{spec.ModName}}";
    public const string PluginVersion = "{{version}}";

    internal static ManualLogSource Log { get; private set; } = null!;
    private Harmony? _harmony;

    private void Awake()
    {
        Log = Logger;
        Log.LogInfo($"Plugin {PluginName} (v{PluginVersion}) loaded successfully.");

        _harmony = new Harmony(PluginGuid);
        _harmony.PatchAll();
    }

    private void OnDestroy()
    {
        _harmony?.UnpatchSelf();
    }
}
""";
    }

    public static string RenderExamplePatch(ModScaffoldSpec spec) => $$"""
using HarmonyLib;

namespace {{spec.ModName}}.Patches;

// Replace TargetType and TargetMethod with actual game symbols discovered via 'query' and 'type'.
// [HarmonyPatch(typeof(TargetType), nameof(TargetType.TargetMethod))]
internal static class ExamplePatch
{
    private static void Postfix()
    {
        Plugin.Log.LogInfo("ExamplePatch executed.");
    }
}
""";

    public static string RenderAssetLoader(ModScaffoldSpec spec) => $$"""
using System.IO;
using UnityEngine;

namespace {{spec.ModName}};

public static class AssetLoader
{
    private static string? s_modDirectory;

    public static string ModDirectory => s_modDirectory ??=
        Path.GetDirectoryName(typeof(AssetLoader).Assembly.Location)
        ?? Path.Combine(BepInEx.Paths.PluginPath, "{{spec.ModName}}");

    public static string AssetsDirectory => Path.Combine(ModDirectory, "Assets");

    public static Sprite? LoadSprite(string relativePath, float pixelsPerUnit = 100f)
    {
        string candidate = Path.Combine(AssetsDirectory, relativePath);
        if (!File.Exists(candidate)) candidate = Path.Combine(ModDirectory, relativePath);
        if (!File.Exists(candidate)) return null;

        byte[] bytes = File.ReadAllBytes(candidate);
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!ImageConversion.LoadImage(texture, bytes)) return null;

        return Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f), pixelsPerUnit);
    }
}
""";
}
