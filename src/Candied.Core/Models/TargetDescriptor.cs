using System.Collections.Generic;
using Candied.Primitives;

namespace Candied.Core.Models;

public enum PlatformCapability
{
    EnvironmentSetup,
    CodeKnowledge,
    AssetPipeline,
    ModWorkspace,
    RuntimeLogs,
    InputProfileExtraction,
    LiveVisionSensor
}

public enum CodeModuleTier
{
    CoreLogic,
    ModularLogic,
    AuxiliaryOrThirdParty
}

public sealed record DiscoveredCodeModule(
    string Name,
    string FullPath,
    long SizeBytes,
    CodeModuleTier Tier,
    string Reason,
    bool IsExtracted = false
);

public sealed record ModLoaderStatus(
    bool IsInstalled,
    string? LoaderName = null,
    string? DetectedVersion = null,
    string? RootPath = null
);

public sealed record TargetDescriptor(
    TargetId Id,
    string Name,
    string TargetPath,
    string? RunnerPath,
    string PlatformId,
    string PlatformDisplayName,
    string Architecture,
    string? EngineVersion,
    string? RuntimeFramework,
    ModLoaderStatus ModLoader,
    IReadOnlyList<PlatformCapability> Capabilities,
    IReadOnlyList<DiscoveredCodeModule> CodeModules,
    IReadOnlyDictionary<string, string> Metadata,
    IReadOnlyList<string> Warnings
);
