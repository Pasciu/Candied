using System.Collections.Generic;

namespace Candied.Core.Models;

public record ProvisioningSpec(
    string? TargetVersion = null,
    bool ForceReinstall = false,
    bool EnableConsoleLogging = true
);

public record ProvisioningReport(
    bool Success,
    string ModLoaderName,
    string Version,
    string Architecture,
    int FilesInstalledCount,
    bool ConsoleLoggingConfigured,
    string ManifestPath
);

public record RollbackReport(
    bool Success,
    int FilesRemovedCount,
    IReadOnlyList<string> RemovedFiles
);

public record ProvisioningManifest(
    string GameName,
    string ModLoaderVersion,
    string Architecture,
    string InstalledAtUtc,
    IReadOnlyList<string> InstalledRelativePaths
);
