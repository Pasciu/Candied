using System.Diagnostics;
using System.Text;
using Candied.Core.Analysis;
using Candied.Core.Errors;
using Candied.Core.Models;
using Candied.Core.Ports;
using Candied.Primitives;
using Candied.Workspace.Parsing;
using Candied.Workspace.Rendering;

namespace Candied.Workspace.Adapters;

internal sealed class DotNetBuildRunnerAdapter : IBuildRunner
{
    public async Task<Result<BuildReport, BuildExecutionError>> BuildModAsync(
        string projectFilePath,
        string configuration = "Debug",
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(projectFilePath))
        {
            return Result.Fail<BuildReport, BuildExecutionError>(
                new BuildExecutionError("Project file path cannot be empty."));
        }

        if (!File.Exists(projectFilePath))
        {
            return Result.Fail<BuildReport, BuildExecutionError>(
                new BuildExecutionError($"Project file does not exist: {projectFilePath}"));
        }

        var fullProjectPath = Path.GetFullPath(projectFilePath);
        var workingDirectory = Path.GetDirectoryName(fullProjectPath) ?? string.Empty;

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
                lock (stdoutBuilder)
                {
                    stdoutBuilder.AppendLine(e.Data);
                }
            }
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null)
            {
                lock (stderrBuilder)
                {
                    stderrBuilder.AppendLine(e.Data);
                }
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
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Ignore failure to kill process
            }

            return Result.Fail<BuildReport, BuildExecutionError>(
                new BuildExecutionError("Build execution was cancelled."));
        }
        catch (Exception ex)
        {
            return Result.Fail<BuildReport, BuildExecutionError>(
                new BuildExecutionError($"Failed to execute dotnet process: {ex.Message}"));
        }

        var rawOutput = stdoutBuilder.ToString();
        var errorOutput = stderrBuilder.ToString();
        var combinedOutput = string.IsNullOrWhiteSpace(errorOutput)
            ? rawOutput
            : $"{rawOutput}\n--- STDERR ---\n{errorOutput}";

        var diagnostics = MsBuildDiagnosticParser.ParseDiagnostics(combinedOutput);

        if (process.ExitCode != 0 && !diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
        {
            diagnostics.Add(new BuildDiagnostic(
                Code: "BUILD_FAILED",
                Message: string.IsNullOrWhiteSpace(errorOutput) ? $"dotnet build exited with code {process.ExitCode}" : errorOutput.Trim(),
                FilePath: projectFilePath,
                LineNumber: 0,
                Severity: DiagnosticSeverity.Error
            ));
        }

        var isSuccess = process.ExitCode == 0;
        IReadOnlyList<MissingReferenceHint>? missingHints = null;

        if (!isSuccess)
        {
            missingHints = BuildMissingReferenceHints(fullProjectPath, diagnostics);
        }

        var report = new BuildReport(
            IsSuccess: isSuccess,
            ExitCode: process.ExitCode,
            Diagnostics: diagnostics,
            RawOutput: combinedOutput,
            MissingReferenceHints: missingHints
        );

        return Result.Ok<BuildReport, BuildExecutionError>(report);
    }

    private static List<MissingReferenceHint>? BuildMissingReferenceHints(
        string fullProjectPath,
        IReadOnlyList<BuildDiagnostic> diagnostics)
    {
        var missingNames = MissingReferenceHintAnalyzer.ExtractMissingAssemblyNames(diagnostics);
        if (missingNames.Count == 0)
        {
            return null;
        }

        string? managedDir = null;
        try
        {
            var xml = File.ReadAllText(fullProjectPath);
            managedDir = CsprojReferenceMutator.TryExtractManagedDirectory(xml);
            if (managedDir == null)
            {
                var gameRoot = CsprojReferenceMutator.TryExtractGameRootPath(xml);
                if (!string.IsNullOrWhiteSpace(gameRoot))
                {
                    managedDir = DiskProjectReferenceEditorAdapter.FindManagedDirectory(gameRoot);
                }
            }
        }
        catch
        {
            // Best-effort hint resolution only
        }

        var hints = new List<MissingReferenceHint>(missingNames.Count);
        foreach (var asmName in missingNames)
        {
            string? resolvedDll = null;
            if (!string.IsNullOrWhiteSpace(managedDir) && Directory.Exists(managedDir))
            {
                var candidate = Path.Combine(managedDir, asmName + ".dll");
                if (File.Exists(candidate))
                {
                    resolvedDll = candidate;
                }
            }

            hints.Add(new MissingReferenceHint(
                AssemblyName: asmName,
                ResolvedDllPath: resolvedDll,
                SuggestedCommand: $"add-reference(projectFilePath=\"{fullProjectPath}\", assemblyName=\"{asmName}\")"));
        }

        return hints;
    }

    internal static List<BuildDiagnostic> ParseDiagnostics(string output) => MsBuildDiagnosticParser.ParseDiagnostics(output);
}
