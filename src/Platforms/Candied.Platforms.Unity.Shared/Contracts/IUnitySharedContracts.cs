using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Candied.Core.Capabilities;
using Candied.Core.Models;
using Candied.Vision.Controls;

namespace Candied.Platforms.Unity.Shared.Contracts;

public sealed record UnityInstallProbe(
    string GameRootPath,
    string DataDirectoryPath,
    string GameName,
    bool IsIl2Cpp,
    string Architecture,
    string? UnityVersion,
    ModLoaderStatus BepInExStatus
);

public interface IUnitySharedInspector
{
    UnityInstallProbe? ProbeUnityInstall(string targetPath);
}

public interface IUnityAssetPipeline : IAssetPipelineCapability
{
}

public interface IUnityRuntimeLogReader : IRuntimeLogCapability
{
}

public interface IUnityInputExtractor
{
    Task<IReadOnlyList<ControlActionEntry>> ExtractControlsOfflineAsync(
        string gameRootPath,
        string dataDirectoryPath,
        CancellationToken ct = default);
}
