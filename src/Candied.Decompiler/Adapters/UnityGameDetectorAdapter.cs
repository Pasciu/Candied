using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Threading;
using System.Threading.Tasks;
using Candied.Core.Errors;
using Candied.Core.Models;
using Candied.Core.Ports;
using Candied.Decompiler.Internal;
using Candied.Primitives;

namespace Candied.Decompiler.Adapters;

internal sealed class UnityGameDetectorAdapter : IGameDetector
{
    public Task<Result<GameEnvironment, DetectionError>> DetectGameAsync(
        string gameDirectoryPath,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(gameDirectoryPath) || !Directory.Exists(gameDirectoryPath))
        {
            return Task.FromResult<Result<GameEnvironment, DetectionError>>(
                new DetectionError($"Game directory does not exist: '{gameDirectoryPath}'"));
        }

        var trimmedPath = gameDirectoryPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var dirInfo = new DirectoryInfo(trimmedPath);

        // Search for Unity *_Data directory
        var dataDirs = dirInfo.GetDirectories("*_Data");
        DirectoryInfo? managedDir = null;
        DirectoryInfo? activeDataDir = null;

        foreach (var dataDir in dataDirs)
        {
            var managedCandidates = dataDir.GetDirectories("Managed");
            if (managedCandidates.Length > 0)
            {
                managedDir = managedCandidates[0];
                activeDataDir = dataDir;
                break;
            }
        }

        if (managedDir == null || activeDataDir == null)
        {
            return Task.FromResult<Result<GameEnvironment, DetectionError>>(
                new DetectionError($"Could not locate Unity Managed assemblies in '{gameDirectoryPath}'. Ensure this is a valid Unity game root directory."));
        }

        var gameName = activeDataDir.Name[..^"_Data".Length];
        var isIl2Cpp = File.Exists(Path.Combine(trimmedPath, "GameAssembly.dll")) ||
                       Directory.Exists(Path.Combine(trimmedPath, "il2cpp_data"));

        var engineType = isIl2Cpp ? UnityEngineType.Il2Cpp : UnityEngineType.Mono;
        var architecture = DetectArchitecture(trimmedPath);
        var bepInEx = DetectBepInEx(trimmedPath);

        // Scan all dlls in Managed
        var dllFiles = managedDir.GetFiles("*.dll");
        var allDllPaths = dllFiles.Select(f => f.FullName).ToList();

        // Check decompiled cache status per assembly in .candied/games/<gameName>/decompiled/
        var decompiledCacheDir = Path.Combine(Directory.GetCurrentDirectory(), ".candied", "games", gameName, "decompiled");
        var discoveredAssemblies = UnityAssemblyFilter.ClassifyAssemblies(
            allDllPaths,
            gameName,
            fileName =>
            {
                var cleanName = fileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                    ? fileName[..^4]
                    : fileName;
                var cacheMarker = Path.Combine(decompiledCacheDir, $".{cleanName}.cache");
                return File.Exists(cacheMarker);
            });

        // Detect target framework from primary assembly
        var primaryAssembly = discoveredAssemblies.FirstOrDefault(a => a.Tier == AssemblyTier.CoreGame)
            ?? (discoveredAssemblies.Count > 0 ? discoveredAssemblies[0] : null);
        var targetFramework = primaryAssembly != null ? DetectTargetFramework(primaryAssembly.FullPath) : null;

        // Collect diagnostics & warnings
        var warnings = new List<string>();
        if (!bepInEx.IsInstalled)
        {
            warnings.Add("BepInEx is not installed in the game directory. BepInEx 5 (Mono) is recommended for modding Unity Mono games.");
        }
        else if (bepInEx.DetectedVersion != null && bepInEx.DetectedVersion.StartsWith("6.", StringComparison.OrdinalIgnoreCase))
        {
            warnings.Add("BepInEx 6 pre-release detected. Most community mods and tooling target BepInEx 5.4.x.");
        }

        if (engineType == UnityEngineType.Il2Cpp)
        {
            warnings.Add("IL2CPP engine detected. Candied currently focuses on Unity Mono games; advanced IL2CPP unhollowing is required.");
        }

        var env = new GameEnvironment(
            Id: new GameId(gameName),
            GameName: gameName,
            GameRootPath: trimmedPath,
            ManagedPath: managedDir.FullName,
            EngineType: engineType,
            Architecture: architecture,
            TargetFramework: targetFramework,
            BepInExInfo: bepInEx,
            DiscoveredAssemblies: discoveredAssemblies,
            EnvironmentWarnings: warnings
        )
        {
            UnityVersion = DetectUnityVersion(trimmedPath, managedDir.FullName)
        };

        return Task.FromResult(Result.Ok<GameEnvironment, DetectionError>(env));
    }

    private static string? DetectUnityVersion(string gameRootPath, string managedPath)
    {
        try
        {
            var playerDll = Path.Combine(gameRootPath, "UnityPlayer.dll");
            if (File.Exists(playerDll))
            {
                var fvi = FileVersionInfo.GetVersionInfo(playerDll);
                if (!string.IsNullOrWhiteSpace(fvi.ProductVersion))
                {
                    string prodVer = fvi.ProductVersion.Trim();
                    int spaceIdx = prodVer.IndexOf(' ');
                    return spaceIdx > 0 ? prodVer[..spaceIdx] : prodVer;
                }
            }

            var exeFiles = Directory.GetFiles(gameRootPath, "*.exe");
            var mainExe = exeFiles.FirstOrDefault(f => !Path.GetFileName(f).StartsWith("UnityCrashHandler", StringComparison.OrdinalIgnoreCase));
            if (mainExe != null && File.Exists(mainExe))
            {
                var fvi = FileVersionInfo.GetVersionInfo(mainExe);
                if (!string.IsNullOrWhiteSpace(fvi.ProductVersion))
                {
                    string prodVer = fvi.ProductVersion.Trim();
                    int spaceIdx = prodVer.IndexOf(' ');
                    return spaceIdx > 0 ? prodVer[..spaceIdx] : prodVer;
                }
            }

            var coreModule = Path.Combine(managedPath, "UnityEngine.CoreModule.dll");
            var candidateDll = File.Exists(coreModule) ? coreModule : Path.Combine(managedPath, "UnityEngine.dll");
            if (File.Exists(candidateDll))
            {
                var fvi = FileVersionInfo.GetVersionInfo(candidateDll);
                if (!string.IsNullOrWhiteSpace(fvi.ProductVersion))
                {
                    string prodVer = fvi.ProductVersion.Trim();
                    int spaceIdx = prodVer.IndexOf(' ');
                    return spaceIdx > 0 ? prodVer[..spaceIdx] : prodVer;
                }
            }
        }
        catch { }

        return null;
    }

    private static string DetectArchitecture(string gameRootPath)
    {
        try
        {
            var exeFiles = Directory.GetFiles(gameRootPath, "*.exe");
            var targetFile = exeFiles.FirstOrDefault(f => !Path.GetFileName(f).StartsWith("UnityCrashHandler", StringComparison.OrdinalIgnoreCase))
                ?? Path.Combine(gameRootPath, "UnityPlayer.dll");

            if (!File.Exists(targetFile))
            {
                return "Unknown";
            }

            using var stream = new FileStream(targetFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new BinaryReader(stream);

            if (stream.Length < 64)
            {
                return "Unknown";
            }

            // DOS Header signature check
            if (reader.ReadUInt16() != 0x5A4D)
            {
                return "Unknown";
            }

            stream.Seek(0x3C, SeekOrigin.Begin);
            var peOffset = reader.ReadInt32();
            if (peOffset + 6 > stream.Length)
            {
                return "Unknown";
            }

            stream.Seek(peOffset, SeekOrigin.Begin);
            var peSig = reader.ReadUInt32();
            if (peSig != 0x00004550) // "PE\0\0"
            {
                return "Unknown";
            }

            var machine = reader.ReadUInt16();
            return machine switch
            {
                0x8664 => "x64",
                0x014c => "x86",
                0xAA64 => "ARM64",
                _ => "Unknown"
            };
        }
        catch
        {
            return "Unknown";
        }
    }

    private static BepInExInstallation DetectBepInEx(string gameRootPath)
    {
        var bepInExRoot = Path.Combine(gameRootPath, "BepInEx");
        if (!Directory.Exists(bepInExRoot))
        {
            return new BepInExInstallation(false, null, null);
        }

        string? detectedVersion = null;
        var coreDll = Path.Combine(bepInExRoot, "core", "BepInEx.Core.dll");
        var preloaderDll = Path.Combine(bepInExRoot, "core", "BepInEx.Preloader.dll");

        var candidatePath = File.Exists(coreDll) ? coreDll : (File.Exists(preloaderDll) ? preloaderDll : null);
        if (candidatePath != null)
        {
            try
            {
                var fvi = FileVersionInfo.GetVersionInfo(candidatePath);
                detectedVersion = !string.IsNullOrWhiteSpace(fvi.ProductVersion)
                    ? fvi.ProductVersion
                    : fvi.FileVersion;
            }
            catch
            {
                // Ignore version read error
            }
        }

        return new BepInExInstallation(true, detectedVersion, bepInExRoot);
    }

    private static string? DetectTargetFramework(string assemblyPath)
    {
        try
        {
            if (!File.Exists(assemblyPath))
            {
                return null;
            }

            using var stream = new FileStream(assemblyPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var peReader = new PEReader(stream);
            if (!peReader.HasMetadata)
            {
                return null;
            }

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
                    var valueBytes = mdReader.GetBlobBytes(customAttr.Value);
                    if (valueBytes.Length > 3 && valueBytes[0] == 1 && valueBytes[1] == 0)
                    {
                        int strLen = valueBytes[2];
                        if (strLen > 0 && 3 + strLen <= valueBytes.Length)
                        {
                            var tf = System.Text.Encoding.UTF8.GetString(valueBytes, 3, strLen);
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
            // Ignore metadata parse error
        }

        return null;
    }
}
