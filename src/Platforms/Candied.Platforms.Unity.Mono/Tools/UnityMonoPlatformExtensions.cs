using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Candied.Core.Capabilities;
using Candied.Core.Models;
using Candied.Platforms.Unity.Mono.Capabilities;
using Candied.Platforms.Unity.Mono.Internal;

namespace Candied.Platforms.Unity.Mono.Tools;

internal sealed class UnityMonoPlatformToolProvider : IPlatformToolProvider
{
    public const string PlatformIdentifier = "unity.mono";
    public const string ToolAddReference = "unity-add-reference";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public string PlatformId => PlatformIdentifier;

    public IReadOnlyList<PlatformToolDefinition> GetTools() =>
    [
        new PlatformToolDefinition(
            Name: ToolAddReference,
            Description: "[Unity Mono Platform Tool] Safely adds a verified <Reference> entry from '<Game>_Data/Managed/*.dll' into a BepInEx mod .csproj file, optionally enabling <Publicize>true</Publicize>. Use this when 'build' reports CS0012 missing assembly references.",
            PlatformId: PlatformIdentifier,
            Parameters:
            [
                new PlatformToolParameter("projectFilePath", "string", "Path to the mod .csproj file.", Required: true),
                new PlatformToolParameter("moduleName", "string", "Name of the managed assembly (e.g. 'UnityEngine.AudioModule' or 'Unity.TextMeshPro').", Required: true),
                new PlatformToolParameter("publicize", "boolean", "Whether to set <Publicize>true</Publicize> on the reference. Defaults to false.", Required: false),
                new PlatformToolParameter("targetPath", "string", "Optional path to the Unity game directory if not inferable from the .csproj.", Required: false)
            ])
    ];

    public async Task<PlatformToolResult> ExecuteToolAsync(
        string toolName,
        IReadOnlyDictionary<string, string?> arguments,
        TargetDescriptor? activeTarget,
        CandiedStorageLayout storageLayout,
        CancellationToken ct = default)
    {
        if (!string.Equals(toolName, ToolAddReference, StringComparison.OrdinalIgnoreCase))
        {
            return new PlatformToolResult(false, "{}", $"Unknown Unity Mono tool: '{toolName}'.");
        }

        string? projectFilePath = GetArg(arguments, "projectFilePath", "project_file_path");
        string? moduleName = GetArg(arguments, "moduleName", "module_name");
        bool publicize = bool.TryParse(GetArg(arguments, "publicize"), out bool pub) && pub;
        string? targetPath = GetArg(arguments, "targetPath", "target_path") ?? activeTarget?.TargetPath;

        if (string.IsNullOrWhiteSpace(projectFilePath) || !File.Exists(projectFilePath))
        {
            return Fail($"Mod project file does not exist: '{projectFilePath}'.");
        }

        if (string.IsNullOrWhiteSpace(moduleName))
        {
            return Fail("Parameter 'moduleName' cannot be empty.");
        }

        string normalizedName = moduleName.Trim();
        if (normalizedName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            normalizedName = normalizedName[..^4];
        }

        try
        {
            string fullProjectPath = Path.GetFullPath(projectFilePath);
            string originalXml = await File.ReadAllTextAsync(fullProjectPath, ct).ConfigureAwait(false);

            string? managedDir = null;
            if (!string.IsNullOrWhiteSpace(targetPath))
            {
                string root = Directory.Exists(targetPath) ? targetPath : (Path.GetDirectoryName(targetPath) ?? string.Empty);
                managedDir = BepInEx5ModWorkspaceCapability.FindManagedDirectory(root);
            }

            managedDir ??= CsprojReferenceMutator.TryExtractManagedDirectory(originalXml);
            if (managedDir == null)
            {
                string? inferredGameRoot = CsprojReferenceMutator.TryExtractGameRootPath(originalXml);
                if (!string.IsNullOrWhiteSpace(inferredGameRoot))
                {
                    managedDir = BepInEx5ModWorkspaceCapability.FindManagedDirectory(inferredGameRoot);
                }
            }

            if (managedDir == null || !Directory.Exists(managedDir))
            {
                return Fail("Could not locate '<Game>_Data/Managed'. Provide 'targetPath'.");
            }

            string dllPath = Path.Combine(managedDir, normalizedName + ".dll");
            if (!File.Exists(dllPath))
            {
                string? match = Directory.EnumerateFiles(managedDir, "*.dll", SearchOption.TopDirectoryOnly)
                    .FirstOrDefault(f => string.Equals(Path.GetFileNameWithoutExtension(f), normalizedName, StringComparison.OrdinalIgnoreCase));

                if (match == null)
                {
                    var similar = Directory.EnumerateFiles(managedDir, "*.dll", SearchOption.TopDirectoryOnly)
                        .Select(Path.GetFileNameWithoutExtension)
                        .Where(n => n != null && n.Contains(normalizedName, StringComparison.OrdinalIgnoreCase))
                        .Take(5)
                        .ToList();

                    string hint = similar.Count > 0 ? $" Did you mean: {string.Join(", ", similar)}?" : string.Empty;
                    return Fail($"Managed module '{normalizedName}.dll' was not found in '{managedDir}'.{hint}");
                }

                dllPath = match;
                normalizedName = Path.GetFileNameWithoutExtension(match);
            }

            var outcome = CsprojReferenceMutator.AddReference(originalXml, normalizedName, dllPath, publicize);
            if (!outcome.AlreadyReferenced)
            {
                await File.WriteAllTextAsync(fullProjectPath, outcome.UpdatedXml, ct).ConfigureAwait(false);
            }

            string payload = JsonSerializer.Serialize(new
            {
                success = true,
                projectFilePath = fullProjectPath,
                moduleName = normalizedName,
                dllPath,
                publicized = publicize,
                alreadyReferenced = outcome.AlreadyReferenced
            }, JsonOptions);

            return new PlatformToolResult(true, payload);
        }
        catch (Exception ex)
        {
            return Fail($"Failed to update .csproj references: {ex.Message}");
        }
    }

    private static string? GetArg(IReadOnlyDictionary<string, string?> args, params string[] keys)
    {
        foreach (var k in keys)
        {
            if (args.TryGetValue(k, out var val) && !string.IsNullOrWhiteSpace(val))
            {
                return val;
            }
        }

        return null;
    }

    private static PlatformToolResult Fail(string error)
    {
        string json = JsonSerializer.Serialize(new { success = false, error }, JsonOptions);
        return new PlatformToolResult(false, json, error);
    }
}

internal sealed class UnityMonoPlatformGuideProvider : IPlatformGuideProvider
{
    public string PlatformId => UnityMonoPlatformToolProvider.PlatformIdentifier;

    public PlatformModdingGuide GetGuide() => new(
        PlatformId: PlatformId,
        Title: "Unity Mono (BepInEx 5 & Harmony) Modding Playbook",
        MarkdownContent: """
        ## Unity Mono Platform Notes (`unity.mono`)
        - **Code Extraction:** Run `extract-code` on the primary `CoreLogic` module (typically `Assembly-CSharp` or `<GameName>.Core`).
        - **Mod Loader:** `setup` provisions BepInEx 5 (`5.4.23.2`), pre-configures console/disk logging, and deploys `Candied.VisionSensors.UnityMono`.
        - **Missing References (`CS0012`):** Use `unity-add-reference` with `projectFilePath` and `moduleName` to inject verified `<Reference>` items from `<Game>_Data/Managed/`.
        - **Harmony Patching:** Scaffolded mods inherit from `BaseUnityPlugin` and invoke `Harmony.PatchAll()` in `Awake()`.
        """);
}
