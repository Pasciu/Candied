using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Candied.Core.Capabilities;
using Candied.Core.Errors;
using Candied.Core.Models;
using Candied.Primitives;

namespace Candied.Infrastructure.Internal;

/// <summary>
/// Built-in universal fallback platform for generic PC executables, scripts, and Emulator + ROM targets.
/// Allows inspecting, reading runtime logs, saving research notes, and launching inside Candied.Studio's Base Window Vision mode.
/// </summary>
internal sealed class GenericTargetPlatform : ITargetPlatform
{
    private readonly PlainTextRuntimeLogCapability _logCapability = new();

    public string PlatformId => "generic.target";
    public string DisplayName => "Generic PC / Emulator Target";
    public int Priority => -1000;

    public IEnvironmentSetupCapability? EnvironmentSetup => null;
    public ICodeKnowledgeCapability? CodeKnowledge => null;
    public IAssetPipelineCapability? AssetPipeline => null;
    public IModWorkspaceCapability? ModWorkspace => null;
    public IRuntimeLogCapability? RuntimeLogs => _logCapability;

    public bool CanHandle(string targetPath, string? runnerPath = null)
    {
        if (string.IsNullOrWhiteSpace(targetPath))
        {
            return false;
        }

        return File.Exists(targetPath) || Directory.Exists(targetPath);
    }

    public Task<Result<TargetDescriptor, DetectionError>> InspectAsync(
        string targetPath,
        string? runnerPath,
        CandiedStorageLayout storageLayout,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(targetPath))
        {
            return Task.FromResult(Result.Fail<TargetDescriptor, DetectionError>(
                new DetectionError("Target path cannot be empty.")));
        }

        string fullTarget = Path.GetFullPath(targetPath.Trim());
        if (!File.Exists(fullTarget) && !Directory.Exists(fullTarget))
        {
            return Task.FromResult(Result.Fail<TargetDescriptor, DetectionError>(
                new DetectionError($"Target path does not exist: '{fullTarget}'.")));
        }

        string? fullRunner = null;
        if (!string.IsNullOrWhiteSpace(runnerPath))
        {
            fullRunner = Path.GetFullPath(runnerPath.Trim());
            if (!File.Exists(fullRunner))
            {
                return Task.FromResult(Result.Fail<TargetDescriptor, DetectionError>(
                    new DetectionError($"Runner/emulator executable does not exist: '{fullRunner}'.")));
            }
        }

        string name = File.Exists(fullTarget)
            ? Path.GetFileNameWithoutExtension(fullTarget)
            : new DirectoryInfo(fullTarget).Name;

        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["targetKind"] = !string.IsNullOrEmpty(fullRunner) ? "emulated" : (File.Exists(fullTarget) ? "file" : "directory")
        };

        if (!string.IsNullOrEmpty(fullRunner))
        {
            metadata["runnerExecutable"] = fullRunner;
        }

        var descriptor = new TargetDescriptor(
            Id: new TargetId(name),
            Name: name,
            TargetPath: fullTarget,
            RunnerPath: fullRunner,
            PlatformId: PlatformId,
            PlatformDisplayName: !string.IsNullOrEmpty(fullRunner) ? "Emulated Target (Generic)" : DisplayName,
            Architecture: "Auto",
            EngineVersion: null,
            RuntimeFramework: null,
            ModLoader: new ModLoaderStatus(false, null, null, null),
            Capabilities: [PlatformCapability.RuntimeLogs],
            CodeModules: Array.Empty<DiscoveredCodeModule>(),
            Metadata: metadata,
            Warnings:
            [
                "No specialized modding platform matched this target. Base Window Vision & Control in Candied.Studio is fully available."
            ]);

        return Task.FromResult(Result.Ok<TargetDescriptor, DetectionError>(descriptor));
    }

    private sealed class PlainTextRuntimeLogCapability : IRuntimeLogCapability
    {
        public string? ResolveDefaultLogPath(TargetDescriptor target)
        {
            string root = Directory.Exists(target.TargetPath)
                ? target.TargetPath
                : (Path.GetDirectoryName(target.TargetPath) ?? string.Empty);

            if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
            {
                return null;
            }

            return Directory.GetFiles(root, "*.log", SearchOption.TopDirectoryOnly).FirstOrDefault();
        }

        public async Task<Result<IReadOnlyList<LogEntry>, LogMonitorError>> ReadRecentLogsAsync(
            string logFilePath,
            int maxLines = 100,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(logFilePath) || !File.Exists(logFilePath))
            {
                return Result.Fail<IReadOnlyList<LogEntry>, LogMonitorError>(
                    new LogMonitorError($"Log file not found: '{logFilePath}'."));
            }

            try
            {
                using var fs = new FileStream(logFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(fs);
                string text = await reader.ReadToEndAsync(ct).ConfigureAwait(false);
                var lines = text.Split(["\r\n", "\r", "\n"], StringSplitOptions.RemoveEmptyEntries);
                int effectiveMax = Math.Max(1, maxLines);

                var entries = lines
                    .TakeLast(effectiveMax)
                    .Select(line => new LogEntry(
                        Timestamp: DateTimeOffset.UtcNow,
                        Level: line.Contains("error", StringComparison.OrdinalIgnoreCase)
                            ? LogLevel.Error
                            : (line.Contains("warn", StringComparison.OrdinalIgnoreCase) ? LogLevel.Warning : LogLevel.Info),
                        Source: "Log",
                        Message: line,
                        StackTrace: null))
                    .ToList();

                return Result.Ok<IReadOnlyList<LogEntry>, LogMonitorError>(entries);
            }
            catch (Exception ex)
            {
                return Result.Fail<IReadOnlyList<LogEntry>, LogMonitorError>(
                    new LogMonitorError($"Failed to read log file: {ex.Message}"));
            }
        }
    }
}
