using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Candied.Core.Models;
using Candied.Platforms.Unity.Shared.Contracts;

namespace Candied.Platforms.Unity.Shared.Internal;

internal sealed class UnitySharedInspector : IUnitySharedInspector
{
    public UnityInstallProbe? ProbeUnityInstall(string targetPath)
    {
        if (string.IsNullOrWhiteSpace(targetPath))
        {
            return null;
        }

        string rootPath = File.Exists(targetPath)
            ? (Path.GetDirectoryName(Path.GetFullPath(targetPath)) ?? string.Empty)
            : Path.GetFullPath(targetPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

        if (string.IsNullOrEmpty(rootPath) || !Directory.Exists(rootPath))
        {
            return null;
        }

        var dirInfo = new DirectoryInfo(rootPath);
        var dataDirs = dirInfo.GetDirectories("*_Data");
        if (dataDirs.Length == 0)
        {
            return null;
        }

        var activeDataDir = dataDirs.FirstOrDefault(d =>
            Directory.Exists(Path.Combine(d.FullName, "Managed")) ||
            Directory.Exists(Path.Combine(d.FullName, "il2cpp_data")) ||
            File.Exists(Path.Combine(d.FullName, "globalgamemanagers")) ||
            File.Exists(Path.Combine(d.FullName, "data.unity3d"))) ?? dataDirs[0];

        string gameName = activeDataDir.Name[..^"_Data".Length];
        bool isIl2Cpp = File.Exists(Path.Combine(rootPath, "GameAssembly.dll")) ||
                        Directory.Exists(Path.Combine(rootPath, "il2cpp_data")) ||
                        Directory.Exists(Path.Combine(activeDataDir.FullName, "il2cpp_data"));

        string arch = DetectArchitecture(rootPath);
        string? managedPath = Path.Combine(activeDataDir.FullName, "Managed");
        string? unityVersion = DetectUnityVersion(rootPath, managedPath);
        var bepInEx = DetectBepInEx(rootPath);

        return new UnityInstallProbe(
            GameRootPath: rootPath,
            DataDirectoryPath: activeDataDir.FullName,
            GameName: gameName,
            IsIl2Cpp: isIl2Cpp,
            Architecture: arch,
            UnityVersion: unityVersion,
            BepInExStatus: bepInEx);
    }

    private static string? DetectUnityVersion(string gameRootPath, string? managedPath)
    {
        try
        {
            string playerDll = Path.Combine(gameRootPath, "UnityPlayer.dll");
            if (File.Exists(playerDll))
            {
                var fvi = FileVersionInfo.GetVersionInfo(playerDll);
                if (!string.IsNullOrWhiteSpace(fvi.ProductVersion))
                {
                    string prodVer = fvi.ProductVersion.Trim();
                    int spaceIdx = prodVer.IndexOf(' ', StringComparison.Ordinal);
                    return spaceIdx > 0 ? prodVer[..spaceIdx] : prodVer;
                }
            }

            var exeFiles = Directory.GetFiles(gameRootPath, "*.exe");
            string? mainExe = exeFiles.FirstOrDefault(f => !Path.GetFileName(f).StartsWith("UnityCrashHandler", StringComparison.OrdinalIgnoreCase));
            if (mainExe != null && File.Exists(mainExe))
            {
                var fvi = FileVersionInfo.GetVersionInfo(mainExe);
                if (!string.IsNullOrWhiteSpace(fvi.ProductVersion))
                {
                    string prodVer = fvi.ProductVersion.Trim();
                    int spaceIdx = prodVer.IndexOf(' ', StringComparison.Ordinal);
                    return spaceIdx > 0 ? prodVer[..spaceIdx] : prodVer;
                }
            }

            if (!string.IsNullOrEmpty(managedPath) && Directory.Exists(managedPath))
            {
                string coreModule = Path.Combine(managedPath, "UnityEngine.CoreModule.dll");
                string candidateDll = File.Exists(coreModule) ? coreModule : Path.Combine(managedPath, "UnityEngine.dll");
                if (File.Exists(candidateDll))
                {
                    var fvi = FileVersionInfo.GetVersionInfo(candidateDll);
                    if (!string.IsNullOrWhiteSpace(fvi.ProductVersion))
                    {
                        string prodVer = fvi.ProductVersion.Trim();
                        int spaceIdx = prodVer.IndexOf(' ', StringComparison.Ordinal);
                        return spaceIdx > 0 ? prodVer[..spaceIdx] : prodVer;
                    }
                }
            }
        }
        catch
        {
        }

        return null;
    }

    private static string DetectArchitecture(string gameRootPath)
    {
        try
        {
            var exeFiles = Directory.GetFiles(gameRootPath, "*.exe");
            string targetFile = exeFiles.FirstOrDefault(f => !Path.GetFileName(f).StartsWith("UnityCrashHandler", StringComparison.OrdinalIgnoreCase))
                ?? Path.Combine(gameRootPath, "UnityPlayer.dll");

            if (!File.Exists(targetFile))
            {
                return "Unknown";
            }

            using var stream = new FileStream(targetFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new BinaryReader(stream);

            if (stream.Length < 64 || reader.ReadUInt16() != 0x5A4D)
            {
                return "Unknown";
            }

            stream.Seek(0x3C, SeekOrigin.Begin);
            int peOffset = reader.ReadInt32();
            if (peOffset + 6 > stream.Length)
            {
                return "Unknown";
            }

            stream.Seek(peOffset, SeekOrigin.Begin);
            if (reader.ReadUInt32() != 0x00004550)
            {
                return "Unknown";
            }

            ushort machine = reader.ReadUInt16();
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

    private static ModLoaderStatus DetectBepInEx(string gameRootPath)
    {
        string bepInExRoot = Path.Combine(gameRootPath, "BepInEx");
        if (!Directory.Exists(bepInExRoot))
        {
            return new ModLoaderStatus(false, "BepInEx", null, null);
        }

        string? detectedVersion = null;
        string coreDll = Path.Combine(bepInExRoot, "core", "BepInEx.Core.dll");
        string preloaderDll = Path.Combine(bepInExRoot, "core", "BepInEx.Preloader.dll");

        string? candidatePath = File.Exists(coreDll) ? coreDll : (File.Exists(preloaderDll) ? preloaderDll : null);
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
            }
        }

        return new ModLoaderStatus(true, "BepInEx", detectedVersion, bepInExRoot);
    }
}
