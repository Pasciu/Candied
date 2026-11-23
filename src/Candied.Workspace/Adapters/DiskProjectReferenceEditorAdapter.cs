using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Candied.Core.Errors;
using Candied.Core.Models;
using Candied.Core.Ports;
using Candied.Primitives;
using Candied.Workspace.Rendering;

namespace Candied.Workspace.Adapters;

internal sealed class DiskProjectReferenceEditorAdapter : IProjectReferenceEditor
{
    public async Task<Result<AddReferenceResult, ScaffoldError>> AddManagedReferenceAsync(
        string projectFilePath,
        string assemblyName,
        bool publicize = false,
        string? gameDirectory = null,
        CancellationToken ct = default)
    {
        if (!File.Exists(projectFilePath))
        {
            return Result.Fail<AddReferenceResult, ScaffoldError>(
                new ScaffoldError($"Mod project file does not exist: '{projectFilePath}'."));
        }

        var trimmedName = assemblyName.Trim();
        var normalizedName = trimmedName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
            ? trimmedName[..^4]
            : trimmedName;
        if (string.IsNullOrWhiteSpace(normalizedName))
        {
            return Result.Fail<AddReferenceResult, ScaffoldError>(
                new ScaffoldError("Assembly name cannot be empty."));
        }

        try
        {
            var fullProjectPath = Path.GetFullPath(projectFilePath);
            var originalXml = await File.ReadAllTextAsync(fullProjectPath, ct).ConfigureAwait(false);

            string? managedDir = null;
            if (!string.IsNullOrWhiteSpace(gameDirectory))
            {
                if (!Directory.Exists(gameDirectory))
                {
                    return Result.Fail<AddReferenceResult, ScaffoldError>(
                        new ScaffoldError($"Game directory does not exist: '{gameDirectory}'."));
                }

                managedDir = FindManagedDirectory(gameDirectory);
            }

            if (managedDir == null)
            {
                managedDir = CsprojReferenceMutator.TryExtractManagedDirectory(originalXml);
            }

            if (managedDir == null)
            {
                var inferredGameRoot = CsprojReferenceMutator.TryExtractGameRootPath(originalXml);
                if (!string.IsNullOrWhiteSpace(inferredGameRoot))
                {
                    managedDir = FindManagedDirectory(inferredGameRoot);
                }
            }

            if (managedDir == null || !Directory.Exists(managedDir))
            {
                return Result.Fail<AddReferenceResult, ScaffoldError>(
                    new ScaffoldError("Could not locate '<Game>_Data/Managed' from gameDirectory or existing .csproj references. Provide 'gameDirectory'."));
            }

            var dllPath = Path.Combine(managedDir, normalizedName + ".dll");
            if (!File.Exists(dllPath))
            {
                // Case-insensitive search fallback
                var match = Directory.EnumerateFiles(managedDir, "*.dll", SearchOption.TopDirectoryOnly)
                    .FirstOrDefault(f => string.Equals(Path.GetFileNameWithoutExtension(f), normalizedName, StringComparison.OrdinalIgnoreCase));

                if (match == null)
                {
                    var similar = Directory.EnumerateFiles(managedDir, "*.dll", SearchOption.TopDirectoryOnly)
                        .Select(Path.GetFileNameWithoutExtension)
                        .Where(n => n != null && n.Contains(normalizedName, StringComparison.OrdinalIgnoreCase))
                        .Take(5)
                        .ToList();

                    var hint = similar.Count > 0
                        ? $" Did you mean: {string.Join(", ", similar)}?"
                        : string.Empty;

                    return Result.Fail<AddReferenceResult, ScaffoldError>(
                        new ScaffoldError($"Assembly '{normalizedName}.dll' was not found in '{managedDir}'.{hint}"));
                }

                dllPath = match;
                normalizedName = Path.GetFileNameWithoutExtension(match);
            }

            var outcome = CsprojReferenceMutator.AddReference(originalXml, normalizedName, dllPath, publicize);

            if (!outcome.AlreadyReferenced)
            {
                await File.WriteAllTextAsync(fullProjectPath, outcome.UpdatedXml, ct).ConfigureAwait(false);
            }

            return Result.Ok<AddReferenceResult, ScaffoldError>(new AddReferenceResult(
                ProjectFilePath: fullProjectPath,
                AssemblyName: normalizedName,
                DllPath: dllPath,
                Publicized: publicize,
                AlreadyReferenced: outcome.AlreadyReferenced));
        }
        catch (Exception ex)
        {
            return Result.Fail<AddReferenceResult, ScaffoldError>(
                new ScaffoldError($"Failed to update project references in '{projectFilePath}': {ex.Message}"));
        }
    }

    internal static string? FindManagedDirectory(string gameDirectory)
    {
        if (string.IsNullOrWhiteSpace(gameDirectory) || !Directory.Exists(gameDirectory))
        {
            return null;
        }

        foreach (var dataDir in Directory.EnumerateDirectories(gameDirectory, "*_Data", SearchOption.TopDirectoryOnly))
        {
            var candidate = Path.Combine(dataDir, "Managed");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        var directManaged = Path.Combine(gameDirectory, "Managed");
        return Directory.Exists(directManaged) ? directManaged : null;
    }
}
