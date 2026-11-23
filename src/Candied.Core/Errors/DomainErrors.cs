namespace Candied.Core.Errors;

public abstract record DomainError(string Message, string? Detail = null);

public record DetectionError(string Message) : DomainError(Message);

public record DecompilerError(string Message, string? AssemblyName = null) : DomainError(Message, AssemblyName);

public record IndexError(string Message) : DomainError(Message);

public record ScaffoldError(string Message) : DomainError(Message);

public record BuildExecutionError(string Message, int ExitCode = -1) : DomainError(Message);

public record LogMonitorError(string Message) : DomainError(Message);

public record ProvisioningError(string Message, string? Detail = null) : DomainError(Message, Detail);

public record ProcessManagementError(string Message, string? Detail = null) : DomainError(Message, Detail);

public record AssetExtractionError(string Message, string? Detail = null) : DomainError(Message, Detail);
