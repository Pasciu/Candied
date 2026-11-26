using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Candied.Core.Capabilities;
using Candied.Core.Errors;
using Candied.Core.Models;
using Candied.Platforms.Unity.Mono.Internal;
using Candied.Primitives;

namespace Candied.Platforms.Unity.Mono.Capabilities;

internal sealed class BepInEx5ModWorkspaceCapability : IModWorkspaceCapability
{
    public bool CanBuildProject(string projectOrManifestPath)
    {
        return !string.IsNullOrWhiteSpace(projectOrManifestPath) &&
               projectOrManifestPath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<Result<ModProjectManifest, ScaffoldError>> ScaffoldModAsync(
        ModScaffoldSpec spec,
        CancellationToken ct = default)
    {
        if (spec is null)
        {
            return Result.Fail<ModProjectManifest, ScaffoldError>(new ScaffoldError("Mod scaffold specification cannot be null."));
        }

        if (string.IsNullOrWhiteSpace(spec.ModName))
        {
            return Result.Fail<ModProjectManifest, ScaffoldError>(new ScaffoldError("Mod name cannot be empty."));
        }

        if (string.IsNullOrWhiteSpace(spec.OutputDirectory))
        {
            return Result.Fail<ModProjectManifest, ScaffoldError>(new ScaffoldError("Output directory cannot be empty."));
        }

        if (string.IsNullOrWhiteSpace(spec.ModId.Value))
        {
            return Result.Fail<ModProjectManifest, ScaffoldError>(new ScaffoldError("Mod ID cannot be empty."));
        }

        try
        {
            string modDir = Path.GetFileName(spec.OutputDirectory).Equals(spec.ModName, StringComparison.OrdinalIgnoreCase)
                ? spec.OutputDirectory
                : Path.Combine(spec.OutputDirectory, spec.ModName);

            string patchesDir = Path.Combine(modDir, "Patches");
            Directory.CreateDirectory(modDir);
            Directory.CreateDirectory(patchesDir);

            string gameRoot = Directory.Exists(spec.Target.TargetPath)
                ? spec.Target.TargetPath
                : (Path.GetDirectoryName(spec.Target.TargetPath) ?? spec.Target.TargetPath);

            string projectFilePath = Path.Combine(modDir, $"{spec.ModName}.csproj");
            string mainSourcePath = Path.Combine(modDir, "Plugin.cs");
            string examplePatchPath = Path.Combine(patchesDir, "ExamplePatch.cs");
            string rootReadmePath = Path.Combine(modDir, "README.md");
            string postBuildDeployPath = Path.Combine(gameRoot, "BepInEx", "plugins", spec.ModName);

            await File.WriteAllTextAsync(projectFilePath, UnityMonoTemplateRenderers.RenderCsproj(spec), Encoding.UTF8, ct).ConfigureAwait(false);
            await File.WriteAllTextAsync(mainSourcePath, UnityMonoTemplateRenderers.RenderPlugin(spec), Encoding.UTF8, ct).ConfigureAwait(false);
            await File.WriteAllTextAsync(examplePatchPath, UnityMonoTemplateRenderers.RenderExamplePatch(spec), Encoding.UTF8, ct).ConfigureAwait(false);
            await File.WriteAllTextAsync(rootReadmePath, $"# {spec.ModName}\n\nUnity Mono mod targeting `{spec.Target.Name}` via BepInEx 5.\n", Encoding.UTF8, ct).ConfigureAwait(false);

            if (spec.IncludeAssetPipeline)
            {
                string assetsDir = Path.Combine(modDir, "Assets");
                Directory.CreateDirectory(assetsDir);
                await File.WriteAllTextAsync(Path.Combine(modDir, "AssetLoader.cs"), UnityMonoTemplateRenderers.RenderAssetLoader(spec), Encoding.UTF8, ct).ConfigureAwait(false);
                await File.WriteAllTextAsync(Path.Combine(assetsDir, "README.md"), "# Mod Assets\n\nPlace loose `.png` sprites/textures in this folder.\n", Encoding.UTF8, ct).ConfigureAwait(false);
            }

            return Result.Ok<ModProjectManifest, ScaffoldError>(new ModProjectManifest(
                ProjectFilePath: projectFilePath,
                MainSourcePath: mainSourcePath,
                PostBuildDeployPath: postBuildDeployPath));
        }
        catch (OperationCanceledException)
        {
            return Result.Fail<ModProjectManifest, ScaffoldError>(new ScaffoldError("Scaffold operation was cancelled."));
        }
        catch (Exception ex)
        {
            return Result.Fail<ModProjectManifest, ScaffoldError>(new ScaffoldError($"Failed to scaffold BepInEx 5 mod project: {ex.Message}"));
        }
    }

    public async Task<Result<BuildReport, BuildExecutionError>> BuildModAsync(
        string projectOrManifestPath,
        string configuration = "Debug",
        TargetDescriptor? activeTarget = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(projectOrManifestPath) || !File.Exists(projectOrManifestPath))
        {
            return Result.Fail<BuildReport, BuildExecutionError>(
                new BuildExecutionError($"Project file does not exist: '{projectOrManifestPath}'."));
        }

        string fullProjectPath = Path.GetFullPath(projectOrManifestPath);
        string workingDirectory = Path.GetDirectoryName(fullProjectPath) ?? string.Empty;

        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"build \"{fullProjectPath}\" --configuration {configuration}",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = startInfo };
        var stdoutBuilder = new StringBuilder();
        var stderrBuilder = new StringBuilder();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null)
            {
                lock (stdoutBuilder) stdoutBuilder.AppendLine(e.Data);
            }
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null)
            {
                lock (stderrBuilder) stderrBuilder.AppendLine(e.Data);
            }
        };

        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await process.WaitForExitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            return Result.Fail<BuildReport, BuildExecutionError>(new BuildExecutionError("Build execution was cancelled."));
        }
        catch (Exception ex)
        {
            return Result.Fail<BuildReport, BuildExecutionError>(new BuildExecutionError($"Failed to execute dotnet build: {ex.Message}"));
        }

        string rawOutput = stdoutBuilder.ToString();
        string errorOutput = stderrBuilder.ToString();
        string combinedOutput = string.IsNullOrWhiteSpace(errorOutput) ? rawOutput : $"{rawOutput}\n--- STDERR ---\n{errorOutput}";

        var diagnostics = MsBuildDiagnosticParser.ParseDiagnostics(combinedOutput);
        if (process.ExitCode != 0 && !diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
        {
            diagnostics.Add(new BuildDiagnostic(
                Code: "BUILD_FAILED",
                Message: string.IsNullOrWhiteSpace(errorOutput) ? $"dotnet build exited with code {process.ExitCode}" : errorOutput.Trim(),
                FilePath: fullProjectPath,
                LineNumber: 0,
                Severity: DiagnosticSeverity.Error));
        }

        bool isSuccess = process.ExitCode == 0;
        IReadOnlyList<BuildResolutionHint>? hints = !isSuccess ? BuildMissingReferenceHints(fullProjectPath, diagnostics) : null;

        return Result.Ok<BuildReport, BuildExecutionError>(new BuildReport(
            IsSuccess: isSuccess,
            ExitCode: process.ExitCode,
            Diagnostics: diagnostics,
            RawOutput: combinedOutput,
            ResolutionHints: hints));
    }

    private static List<BuildResolutionHint>? BuildMissingReferenceHints(
        string fullProjectPath,
        IReadOnlyList<BuildDiagnostic> diagnostics)
    {
        var missingNames = MsBuildDiagnosticParser.ExtractMissingAssemblyNames(diagnostics);
        if (missingNames.Count == 0)
        {
            return null;
        }

        string? managedDir = null;
        try
        {
            string xml = File.ReadAllText(fullProjectPath);
            managedDir = CsprojReferenceMutator.TryExtractManagedDirectory(xml);
            if (managedDir == null)
            {
                string? gameRoot = CsprojReferenceMutator.TryExtractGameRootPath(xml);
                if (!string.IsNullOrWhiteSpace(gameRoot))
                {
                    managedDir = FindManagedDirectory(gameRoot);
                }
            }
        }
        catch
        {
        }

        var hints = new List<BuildResolutionHint>(missingNames.Count);
        foreach (var asmName in missingNames)
        {
            string? resolvedDll = null;
            if (!string.IsNullOrWhiteSpace(managedDir) && Directory.Exists(managedDir))
            {
                string candidate = Path.Combine(managedDir, asmName + ".dll");
                if (File.Exists(candidate))
                {
                    resolvedDll = candidate;
                }
            }

            hints.Add(new BuildResolutionHint(
                MissingItem: asmName,
                ResolvedPath: resolvedDll,
                SuggestedCommand: $"unity-add-reference(projectFilePath=\"{fullProjectPath}\", moduleName=\"{asmName}\")"));
        }

        return hints;
    }

    internal static string? FindManagedDirectory(string targetDirectory)
    {
        if (string.IsNullOrWhiteSpace(targetDirectory) || !Directory.Exists(targetDirectory)) return null;
        foreach (var dataDir in Directory.EnumerateDirectories(targetDirectory, "*_Data", SearchOption.TopDirectoryOnly))
        {
            string candidate = Path.Combine(dataDir, "Managed");
            if (Directory.Exists(candidate)) return candidate;
        }

        string direct = Path.Combine(targetDirectory, "Managed");
        return Directory.Exists(direct) ? direct : null;
    }
}
