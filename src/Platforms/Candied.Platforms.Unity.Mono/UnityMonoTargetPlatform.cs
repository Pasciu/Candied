using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Candied.Core.Capabilities;
using Candied.Core.Errors;
using Candied.Core.Models;
using Candied.Platforms.Unity.Mono.Capabilities;
using Candied.Platforms.Unity.Mono.Internal;
using Candied.Platforms.Unity.Mono.Tools;
using Candied.Platforms.Unity.Shared.Contracts;
using Candied.Primitives;
using Candied.Vision.Ports;

namespace Candied.Platforms.Unity.Mono;

internal sealed class UnityMonoTargetPlatform : ITargetPlatform
{
    private readonly IUnitySharedInspector _sharedInspector;
    private readonly IUnityAssetPipeline _assetPipeline;
    private readonly IUnityRuntimeLogReader _logReader;
    private readonly IUnityInputExtractor _inputExtractor;
    private readonly IGameControlProfileStore? _controlProfileStore;
    private readonly BepInEx5EnvironmentSetupCapability _environmentSetup;
    private readonly ILSpyCodeKnowledgeCapability _codeKnowledge;
    private readonly BepInEx5ModWorkspaceCapability _modWorkspace;

    public UnityMonoTargetPlatform(
        IUnitySharedInspector sharedInspector,
        IUnityAssetPipeline assetPipeline,
        IUnityRuntimeLogReader logReader,
        IUnityInputExtractor inputExtractor,
        BepInEx5EnvironmentSetupCapability environmentSetup,
        ILSpyCodeKnowledgeCapability codeKnowledge,
        BepInEx5ModWorkspaceCapability modWorkspace,
        IGameControlProfileStore? controlProfileStore = null)
    {
        _sharedInspector = sharedInspector;
        _assetPipeline = assetPipeline;
        _logReader = logReader;
        _inputExtractor = inputExtractor;
        _environmentSetup = environmentSetup;
        _codeKnowledge = codeKnowledge;
        _modWorkspace = modWorkspace;
        _controlProfileStore = controlProfileStore;
    }

    public string PlatformId => UnityMonoPlatformToolProvider.PlatformIdentifier;
    public string DisplayName => "Unity (Mono Runtime)";
    public int Priority => 100;

    public IEnvironmentSetupCapability? EnvironmentSetup => _environmentSetup;
    public ICodeKnowledgeCapability? CodeKnowledge => _codeKnowledge;
    public IAssetPipelineCapability? AssetPipeline => _assetPipeline;
    public IModWorkspaceCapability? ModWorkspace => _modWorkspace;
    public IRuntimeLogCapability? RuntimeLogs => _logReader;

    public bool CanHandle(string targetPath, string? runnerPath = null)
    {
        if (!string.IsNullOrWhiteSpace(runnerPath))
        {
            return false;
        }

        var probe = _sharedInspector.ProbeUnityInstall(targetPath);
        if (probe == null || probe.IsIl2Cpp)
        {
            return false;
        }

        return Directory.Exists(Path.Combine(probe.DataDirectoryPath, "Managed"));
    }

    public async Task<Result<TargetDescriptor, DetectionError>> InspectAsync(
        string targetPath,
        string? runnerPath,
        CandiedStorageLayout storageLayout,
        CancellationToken ct = default)
    {
        var probe = _sharedInspector.ProbeUnityInstall(targetPath);
        if (probe == null)
        {
            return Result.Fail<TargetDescriptor, DetectionError>(
                new DetectionError($"Target '{targetPath}' is not a valid Unity game installation."));
        }

        if (probe.IsIl2Cpp)
        {
            return Result.Fail<TargetDescriptor, DetectionError>(
                new DetectionError($"Target '{probe.GameName}' uses the Unity IL2CPP runtime; 'unity.mono' only handles Unity Mono targets."));
        }

        string managedPath = Path.Combine(probe.DataDirectoryPath, "Managed");
        if (!Directory.Exists(managedPath))
        {
            return Result.Fail<TargetDescriptor, DetectionError>(
                new DetectionError($"Unity Managed directory not found at '{managedPath}'."));
        }

        var dllPaths = Directory.GetFiles(managedPath, "*.dll", SearchOption.TopDirectoryOnly);
        var discoveredModules = UnityAssemblyFilter.ClassifyModules(
            dllPaths,
            probe.GameName,
            fileName =>
            {
                string cleanName = fileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                    ? fileName[..^4]
                    : fileName;
                string extractedDir = storageLayout.GetExtractedCodeDir(probe.GameName, cleanName);
                string cacheMarker = Path.Combine(extractedDir, $".{cleanName}.cache");
                return File.Exists(cacheMarker);
            });

        var primaryModule = discoveredModules.FirstOrDefault(m => m.Tier == CodeModuleTier.CoreLogic)
            ?? (discoveredModules.Count > 0 ? discoveredModules[0] : null);
        string? runtimeFramework = primaryModule != null ? DetectTargetFramework(primaryModule.FullPath) : null;

        var warnings = new List<string>();
        if (!probe.BepInExStatus.IsInstalled)
        {
            warnings.Add("BepInEx 5 is not installed. Run 'setup' to provision BepInEx and the Vision sensor.");
        }
        else if (probe.BepInExStatus.DetectedVersion != null &&
                 probe.BepInExStatus.DetectedVersion.StartsWith("6.", StringComparison.OrdinalIgnoreCase))
        {
            warnings.Add("BepInEx 6 detected on a Mono target; BepInEx 5.4.x is recommended for Unity Mono.");
        }

        string sensorDllPath = Path.Combine(probe.GameRootPath, "BepInEx", "plugins", "Candied.VisionSensor", "Candied.VisionSensors.UnityMono.dll");
        bool hasSensor = File.Exists(sensorDllPath);

        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["dataDirectory"] = probe.DataDirectoryPath,
            ["managedPath"] = managedPath,
            ["hasVisionSensor"] = hasSensor ? "true" : "false",
            ["windowClassHint"] = "UnityWndClass"
        };

        // Best-effort automatic offline input binding extraction into controls.json
        if (_controlProfileStore != null)
        {
            var autoBindings = await _inputExtractor.ExtractControlsOfflineAsync(probe.GameRootPath, probe.DataDirectoryPath, ct).ConfigureAwait(false);
            if (autoBindings.Count > 0)
            {
                string profilePath = storageLayout.GetControlProfilePath(probe.GameName);
                await _controlProfileStore.MergeAndSaveActionsAsync(
                    profilePath,
                    probe.GameName,
                    probe.GameName,
                    autoBindings,
                    overwriteHigherPrioritySources: false,
                    ct).ConfigureAwait(false);
            }
        }

        var capabilities = new List<PlatformCapability>
        {
            PlatformCapability.EnvironmentSetup,
            PlatformCapability.CodeKnowledge,
            PlatformCapability.AssetPipeline,
            PlatformCapability.ModWorkspace,
            PlatformCapability.RuntimeLogs,
            PlatformCapability.InputProfileExtraction,
            PlatformCapability.LiveVisionSensor
        };

        return Result.Ok<TargetDescriptor, DetectionError>(new TargetDescriptor(
            Id: new TargetId(probe.GameName),
            Name: probe.GameName,
            TargetPath: probe.GameRootPath,
            RunnerPath: null,
            PlatformId: PlatformId,
            PlatformDisplayName: DisplayName,
            Architecture: probe.Architecture,
            EngineVersion: probe.UnityVersion,
            RuntimeFramework: runtimeFramework,
            ModLoader: probe.BepInExStatus,
            Capabilities: capabilities,
            CodeModules: discoveredModules,
            Metadata: metadata,
            Warnings: warnings));
    }

    private static string? DetectTargetFramework(string assemblyPath)
    {
        try
        {
            if (!File.Exists(assemblyPath)) return null;

            using var stream = new FileStream(assemblyPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var peReader = new PEReader(stream);
            if (!peReader.HasMetadata) return null;

            var mdReader = peReader.GetMetadataReader();
            foreach (var handle in mdReader.CustomAttributes)
            {
                var customAttr = mdReader.GetCustomAttribute(handle);
                StringHandle nameHandle = default;

                if (customAttr.Constructor.Kind == HandleKind.MemberReference)
                {
                    var memberRef = mdReader.GetMemberReference((MemberReferenceHandle)customAttr.Constructor);
                    if (memberRef.Parent.Kind == HandleKind.TypeReference)
                    {
                        var typeRef = mdReader.GetTypeReference((TypeReferenceHandle)memberRef.Parent);
                        nameHandle = typeRef.Name;
                    }
                }

                if (!nameHandle.IsNil && mdReader.GetString(nameHandle) == "TargetFrameworkAttribute")
                {
                    byte[] valueBytes = mdReader.GetBlobBytes(customAttr.Value);
                    if (valueBytes.Length > 3 && valueBytes[0] == 1 && valueBytes[1] == 0)
                    {
                        int strLen = valueBytes[2];
                        if (strLen > 0 && 3 + strLen <= valueBytes.Length)
                        {
                            string tf = Encoding.UTF8.GetString(valueBytes, 3, strLen);
                            if (tf.Contains(".NETFramework,Version=v4", StringComparison.OrdinalIgnoreCase)) return "net472";
                            if (tf.Contains(".NETFramework,Version=v3.5", StringComparison.OrdinalIgnoreCase)) return "net35";
                            if (tf.Contains(".NETStandard,Version=v2.1", StringComparison.OrdinalIgnoreCase)) return "netstandard2.1";
                            if (tf.Contains(".NETStandard,Version=v2.0", StringComparison.OrdinalIgnoreCase)) return "netstandard2.0";
                            return tf;
                        }
                    }
                }
            }
        }
        catch
        {
        }

        return null;
    }
}
