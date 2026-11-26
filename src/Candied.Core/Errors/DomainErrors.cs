namespace Candied.Core.Errors;

public abstract record DomainError(string Message, string? Detail = null);

public sealed record DetectionError(string Message, string? Detail = null) : DomainError(Message, Detail);

public sealed record CodeExtractionError(string Message, string? ModuleName = null) : DomainError(Message, ModuleName);

public sealed record IndexError(string Message, string? Detail = null) : DomainError(Message, Detail);

public sealed record ScaffoldError(string Message, string? Detail = null) : DomainError(Message, Detail);

public sealed record BuildExecutionError(string Message, int ExitCode = -1) : DomainError(Message);

public sealed record LogMonitorError(string Message, string? Detail = null) : DomainError(Message, Detail);

public sealed record ProvisioningError(string Message, string? Detail = null) : DomainError(Message, Detail);

public sealed record ProcessManagementError(string Message, string? Detail = null) : DomainError(Message, Detail);

public sealed record AssetExtractionError(string Message, string? Detail = null) : DomainError(Message, Detail);

public sealed record UnsupportedCapabilityError(string PlatformId, string CapabilityName)
    : DomainError($"Platform '{PlatformId}' does not support capability '{CapabilityName}'.");
