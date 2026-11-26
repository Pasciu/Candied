using System;
using System.Collections.Generic;
using Candied.Primitives;

namespace Candied.Core.Models;

public sealed record ModScaffoldSpec(
    string ModName,
    ModId ModId,
    string Version,
    string OutputDirectory,
    TargetDescriptor Target,
    bool IncludeAssetPipeline = false,
    IReadOnlyDictionary<string, string>? PlatformOptions = null
);

public sealed record ModProjectManifest(
    string ProjectFilePath,
    string MainSourcePath,
    string PostBuildDeployPath
);

public sealed record CodeExtractionOutput(
    string ModuleName,
    string OutputDirectory,
    int SourceFileCount
);

public sealed record CodeExtractionProgress(
    int Percentage,
    string StatusMessage
);

public enum DiagnosticSeverity
{
    Info,
    Warning,
    Error
}

public sealed record BuildDiagnostic(
    string Code,
    string Message,
    string FilePath,
    int LineNumber,
    DiagnosticSeverity Severity
);

public sealed record BuildResolutionHint(
    string MissingItem,
    string? ResolvedPath,
    string SuggestedCommand
);

public sealed record BuildReport(
    bool IsSuccess,
    int ExitCode,
    IReadOnlyList<BuildDiagnostic> Diagnostics,
    string RawOutput,
    IReadOnlyList<BuildResolutionHint>? ResolutionHints = null
);

public enum LogLevel
{
    Debug,
    Info,
    Warning,
    Error,
    Fatal
}

public sealed record LogEntry(
    DateTimeOffset Timestamp,
    LogLevel Level,
    string Source,
    string Message,
    string? StackTrace
);

public sealed record ProvisioningSpec(
    string? TargetVersion = null,
    bool ForceReinstall = false,
    bool EnableConsoleLogging = true
);

public sealed record ProvisioningReport(
    bool Success,
    string ModLoaderName,
    string Version,
    string Architecture,
    int FilesInstalledCount,
    bool ConsoleLoggingConfigured,
    string ManifestPath,
    bool SensorInstalled = false
);

public sealed record RollbackReport(
    bool Success,
    int FilesRemovedCount,
    IReadOnlyList<string> RemovedFiles
);

public sealed record ProvisioningManifest(
    string TargetName,
    string PlatformId,
    string ModLoaderVersion,
    string Architecture,
    string InstalledAtUtc,
    IReadOnlyList<string> InstalledRelativePaths
);

public sealed record CodeModuleOverview(
    string ModuleName,
    string RelativePath,
    long SizeBytes,
    bool IsCoreLogic
);

public sealed record NamespaceOverview(
    string NamespaceName,
    IReadOnlyList<string> KeyTypes,
    int TotalTypesCount
);

public sealed record TargetArchitectureOverview(
    string TargetName,
    string PlatformDisplayName,
    string EngineVersion,
    string RuntimeFramework,
    IReadOnlyList<CodeModuleOverview> Modules,
    IReadOnlyList<string> KeyManagers,
    IReadOnlyList<NamespaceOverview> Namespaces,
    int TotalTypesIndexed,
    int TotalMethodsIndexed
);

public sealed record TargetProcessInfo(
    ProcessId Id,
    string ProcessName,
    string MainWindowTitle,
    bool IsResponding,
    DateTime StartTime
);

public sealed record TargetLaunchOptions(
    bool Windowed = true,
    int WindowWidth = 1280,
    int WindowHeight = 720,
    IReadOnlyList<string>? CustomArguments = null
);

public sealed record PlatformToolParameter(
    string Name,
    string Type,
    string Description,
    bool Required = false
);

public sealed record PlatformToolDefinition(
    string Name,
    string Description,
    string PlatformId,
    IReadOnlyList<PlatformToolParameter> Parameters
);

public sealed record PlatformToolResult(
    bool Success,
    string PayloadJson,
    string? ErrorMessage = null
);

public sealed record PlatformModdingGuide(
    string PlatformId,
    string Title,
    string MarkdownContent
);
