using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Candied.Core.Capabilities;
using Candied.Core.Errors;
using Candied.Core.Models;
using Candied.Platforms.Unity.Mono.Internal;
using Candied.Primitives;

namespace Candied.Platforms.Unity.Mono.Capabilities;

internal sealed class BepInEx5EnvironmentSetupCapability : IEnvironmentSetupCapability
{
    private const string DefaultBepInExVersion = "5.4.23.2";
    private const string ManifestFileName = ".candied_manifest.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly HttpClient _httpClient;

    public BepInEx5EnvironmentSetupCapability(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
    }

    public async Task<Result<ProvisioningReport, ProvisioningError>> ProvisionAsync(
        TargetDescriptor target,
        ProvisioningSpec spec,
        CandiedStorageLayout storageLayout,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(storageLayout);

        string gameRoot = Directory.Exists(target.TargetPath)
            ? target.TargetPath
            : (Path.GetDirectoryName(target.TargetPath) ?? string.Empty);

        if (string.IsNullOrWhiteSpace(gameRoot) || !Directory.Exists(gameRoot))
        {
            return Result.Fail<ProvisioningReport, ProvisioningError>(
                new ProvisioningError($"Target game root directory does not exist: '{gameRoot}'."));
        }

        string version = !string.IsNullOrWhiteSpace(spec.TargetVersion) ? spec.TargetVersion : DefaultBepInExVersion;
        string architecture = string.Equals(target.Architecture, "x86", StringComparison.OrdinalIgnoreCase) ? "x86" : "x64";
        string primaryArchiveName = $"BepInEx_win_{architecture}_{version}.zip";
        string legacyArchiveName = $"BepInEx_{architecture}_{version}.zip";
        string cacheDir = storageLayout.GetLoaderCacheDir("bepinex-mono");
        string primaryCachedPath = Path.Combine(cacheDir, primaryArchiveName);
        string legacyCachedPath = Path.Combine(cacheDir, legacyArchiveName);
        string cachedArchivePath = File.Exists(primaryCachedPath)
            ? primaryCachedPath
            : (File.Exists(legacyCachedPath) ? legacyCachedPath : primaryCachedPath);

        try
        {
            if (!File.Exists(cachedArchivePath) || spec.ForceReinstall)
            {
                Directory.CreateDirectory(cacheDir);
                string[] candidateNames = [primaryArchiveName, legacyArchiveName];
                string tempDownloadPath = Path.Combine(cacheDir, $"{primaryArchiveName}.tmp_{Guid.NewGuid():N}");

                try
                {
                    bool downloaded = false;
                    string lastAttemptedUrl = string.Empty;
                    System.Net.HttpStatusCode lastStatus = System.Net.HttpStatusCode.NotFound;

                    foreach (string candidateName in candidateNames)
                    {
                        string downloadUrl = $"https://github.com/BepInEx/BepInEx/releases/download/v{version}/{candidateName}";
                        lastAttemptedUrl = downloadUrl;

                        using var response = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                        lastStatus = response.StatusCode;
                        if (!response.IsSuccessStatusCode)
                        {
                            continue;
                        }

                        using (var contentStream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
                        using (var fileStream = new FileStream(tempDownloadPath, FileMode.Create, FileAccess.Write, FileShare.None))
                        {
                            await contentStream.CopyToAsync(fileStream, ct).ConfigureAwait(false);
                        }

                        cachedArchivePath = Path.Combine(cacheDir, candidateName);
                        if (File.Exists(cachedArchivePath))
                        {
                            File.Delete(cachedArchivePath);
                        }

                        File.Move(tempDownloadPath, cachedArchivePath);
                        downloaded = true;
                        break;
                    }

                    if (!downloaded)
                    {
                        return Result.Fail<ProvisioningReport, ProvisioningError>(
                            new ProvisioningError($"Failed to download BepInEx package from GitHub: HTTP {lastStatus} ({lastAttemptedUrl})"));
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    if (File.Exists(tempDownloadPath))
                    {
                        try { File.Delete(tempDownloadPath); } catch { }
                    }

                    return Result.Fail<ProvisioningReport, ProvisioningError>(
                        new ProvisioningError($"Download error while fetching BepInEx: {ex.Message}"));
                }
            }

            string fullGameRoot = Path.GetFullPath(gameRoot)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

            var installedRelativePaths = new List<string>();
            using (var zipArchive = ZipFile.OpenRead(cachedArchivePath))
            {
                foreach (var entry in zipArchive.Entries)
                {
                    if (string.IsNullOrEmpty(entry.Name))
                    {
                        continue;
                    }

                    string destinationPath = Path.GetFullPath(Path.Combine(fullGameRoot, entry.FullName));
                    if (!destinationPath.StartsWith(fullGameRoot, StringComparison.OrdinalIgnoreCase))
                    {
                        return Result.Fail<ProvisioningReport, ProvisioningError>(
                            new ProvisioningError($"Security check failed: archive entry '{entry.FullName}' resolves outside target root."));
                    }

                    string? destinationDir = Path.GetDirectoryName(destinationPath);
                    if (!string.IsNullOrEmpty(destinationDir) && !Directory.Exists(destinationDir))
                    {
                        Directory.CreateDirectory(destinationDir);
                    }

                    entry.ExtractToFile(destinationPath, overwrite: true);
                    installedRelativePaths.Add(entry.FullName.Replace('\\', '/'));
                }
            }

            string configDir = Path.Combine(gameRoot, "BepInEx", "config");
            string configPath = Path.Combine(configDir, "BepInEx.cfg");
            Directory.CreateDirectory(configDir);

            if (!File.Exists(configPath) || spec.EnableConsoleLogging)
            {
                string cfgContent = UnityMonoTemplateRenderers.RenderBepInExConfig(spec.EnableConsoleLogging);
                await File.WriteAllTextAsync(configPath, cfgContent, ct).ConfigureAwait(false);
                const string relCfgPath = "BepInEx/config/BepInEx.cfg";
                if (!installedRelativePaths.Contains(relCfgPath))
                {
                    installedRelativePaths.Add(relCfgPath);
                }
            }

            bool sensorInstalled = TryDeployVisionSensor(gameRoot, target.RuntimeFramework, installedRelativePaths);

            var manifest = new ProvisioningManifest(
                TargetName: target.Name,
                PlatformId: target.PlatformId,
                ModLoaderVersion: version,
                Architecture: architecture,
                InstalledAtUtc: DateTime.UtcNow.ToString("O"),
                InstalledRelativePaths: installedRelativePaths);

            string manifestPath = Path.Combine(gameRoot, ManifestFileName);
            await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(manifest, JsonOptions), ct).ConfigureAwait(false);

            return Result.Ok<ProvisioningReport, ProvisioningError>(new ProvisioningReport(
                Success: true,
                ModLoaderName: "BepInEx",
                Version: version,
                Architecture: architecture,
                FilesInstalledCount: installedRelativePaths.Count,
                ConsoleLoggingConfigured: true,
                ManifestPath: manifestPath,
                SensorInstalled: sensorInstalled));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Result.Fail<ProvisioningReport, ProvisioningError>(
                new ProvisioningError($"Failed to provision BepInEx 5 environment: {ex.Message}"));
        }
    }

    public async Task<Result<RollbackReport, ProvisioningError>> RollbackAsync(
        TargetDescriptor target,
        CandiedStorageLayout storageLayout,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(target);

        string gameRoot = Directory.Exists(target.TargetPath)
            ? target.TargetPath
            : (Path.GetDirectoryName(target.TargetPath) ?? string.Empty);

        string manifestPath = Path.Combine(gameRoot, ManifestFileName);
        if (!File.Exists(manifestPath))
        {
            return Result.Fail<RollbackReport, ProvisioningError>(
                new ProvisioningError($"No Candied provisioning manifest ('{ManifestFileName}') found in '{gameRoot}'."));
        }

        try
        {
            string manifestJson = await File.ReadAllTextAsync(manifestPath, ct).ConfigureAwait(false);
            var manifest = JsonSerializer.Deserialize<ProvisioningManifest>(manifestJson, JsonOptions);
            if (manifest == null || manifest.InstalledRelativePaths.Count == 0)
            {
                return Result.Fail<RollbackReport, ProvisioningError>(
                    new ProvisioningError("Provisioning manifest is empty or invalid."));
            }

            string fullGameRoot = Path.GetFullPath(gameRoot)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

            var removedFiles = new List<string>();
            foreach (var relPath in manifest.InstalledRelativePaths)
            {
                if (string.IsNullOrWhiteSpace(relPath) || Path.IsPathRooted(relPath))
                {
                    continue;
                }

                string fullPath = Path.GetFullPath(Path.Combine(fullGameRoot, relPath));
                if (!fullPath.StartsWith(fullGameRoot, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (File.Exists(fullPath))
                {
                    File.Delete(fullPath);
                    removedFiles.Add(relPath);
                }
            }

            string bepInExRoot = Path.Combine(gameRoot, "BepInEx");
            if (Directory.Exists(bepInExRoot))
            {
                TryDeleteEmptyDirectory(Path.Combine(bepInExRoot, "plugins", "Candied.VisionSensor"));
                TryDeleteEmptyDirectory(Path.Combine(bepInExRoot, "patchers"));
                TryDeleteEmptyDirectory(Path.Combine(bepInExRoot, "plugins"));
                TryDeleteEmptyDirectory(Path.Combine(bepInExRoot, "config"));
                TryDeleteEmptyDirectory(Path.Combine(bepInExRoot, "core"));
                TryDeleteEmptyDirectory(bepInExRoot);
            }

            if (File.Exists(manifestPath))
            {
                File.Delete(manifestPath);
                removedFiles.Add(ManifestFileName);
            }

            return Result.Ok<RollbackReport, ProvisioningError>(new RollbackReport(true, removedFiles.Count, removedFiles));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Result.Fail<RollbackReport, ProvisioningError>(
                new ProvisioningError($"Rollback failed: {ex.Message}"));
        }
    }

    private static bool TryDeployVisionSensor(string gameRoot, string? runtimeFramework, List<string> installedRelativePaths)
    {
        try
        {
            string tfm = string.Equals(runtimeFramework, "net35", StringComparison.OrdinalIgnoreCase) ? "net35" : "net472";
            string? sensorDll = FindBuiltSensorFile(tfm, "Candied.VisionSensors.UnityMono.dll");
            string? protoDll = FindBuiltSensorFile(tfm, "Candied.Vision.Protocol.dll");

            if (sensorDll == null || protoDll == null)
            {
                return false;
            }

            string destDir = Path.Combine(gameRoot, "BepInEx", "plugins", "Candied.VisionSensor");
            Directory.CreateDirectory(destDir);

            string destSensor = Path.Combine(destDir, "Candied.VisionSensors.UnityMono.dll");
            string destProto = Path.Combine(destDir, "Candied.Vision.Protocol.dll");
            File.Copy(sensorDll, destSensor, overwrite: true);
            File.Copy(protoDll, destProto, overwrite: true);

            const string relSensor = "BepInEx/plugins/Candied.VisionSensor/Candied.VisionSensors.UnityMono.dll";
            const string relProto = "BepInEx/plugins/Candied.VisionSensor/Candied.Vision.Protocol.dll";
            if (!installedRelativePaths.Contains(relSensor)) installedRelativePaths.Add(relSensor);
            if (!installedRelativePaths.Contains(relProto)) installedRelativePaths.Add(relProto);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string? FindBuiltSensorFile(string tfm, string fileName)
    {
        string? dir = AppContext.BaseDirectory;
        for (int i = 0; i < 6 && dir != null; i++)
        {
            foreach (var cfg in new[] { "Release", "Debug" })
            {
                string candidate = Path.Combine(dir, "src", "VisionSensors", "Candied.VisionSensors.UnityMono", "bin", cfg, tfm, fileName);
                if (File.Exists(candidate)) return candidate;
            }

            dir = Directory.GetParent(dir)?.FullName;
        }

        return null;
    }

    private static void TryDeleteEmptyDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path) &&
                Directory.GetFiles(path).Length == 0 &&
                Directory.GetDirectories(path).Length == 0)
            {
                Directory.Delete(path);
            }
        }
        catch
        {
        }
    }
}
