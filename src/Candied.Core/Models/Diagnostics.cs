namespace Candied.Core.Models;

public enum DiagnosticSeverity
{
    Info,
    Warning,
    Error
}

public record BuildDiagnostic(
    string Code,
    string Message,
    string FilePath,
    int LineNumber,
    DiagnosticSeverity Severity
);

public record MissingReferenceHint(
    string AssemblyName,
    string? ResolvedDllPath,
    string SuggestedCommand
);

public record BuildReport(
    bool IsSuccess,
    int ExitCode,
    IReadOnlyList<BuildDiagnostic> Diagnostics,
    string RawOutput,
    IReadOnlyList<MissingReferenceHint>? MissingReferenceHints = null
);

public enum LogLevel
{
    Debug,
    Info,
    Warning,
    Error,
    Fatal
}

public record LogEntry(
    DateTimeOffset Timestamp,
    LogLevel Level,
    string Source,
    string Message,
    string? StackTrace
);
