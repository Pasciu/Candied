using Candied.Primitives;

namespace Candied.Core.Models;

public record ModScaffoldSpec(
    string ModName,
    PluginGuid PluginGuid,
    string Version,
    string OutputDirectory,
    GameEnvironment TargetGame,
    IReadOnlyList<string> PublicizedAssemblyNames,
    bool IncludeAssetPipeline = false
);

public record ModProjectManifest(
    string ProjectFilePath,
    string MainPluginSourcePath,
    string PostBuildDeployPath
);

public record AddReferenceResult(
    string ProjectFilePath,
    string AssemblyName,
    string DllPath,
    bool Publicized,
    bool AlreadyReferenced
);
