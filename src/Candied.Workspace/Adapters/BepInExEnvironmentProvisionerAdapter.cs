using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Candied.Core.Errors;
using Candied.Core.Models;
using Candied.Core.Ports;
using Candied.Primitives;
using Candied.Workspace.Rendering;

namespace Candied.Workspace.Adapters;

/// <summary>
/// Infrastructure adapter for provisioning and configuring BepInEx in the target game directory.
/// Employs local archive caching in .candied/cache/bepinex/ to eliminate redundant downloads,
/// pre-configures logging, tracks installed files in a manifest, and supports clean rollback.
/// </summary>
internal sealed class BepInExEnvironmentProvisionerAdapter : IEnvironmentProvisioner
{
    private const string DefaultBepInExVersion = "5.4.23.2";
    private const string ManifestFileName = ".candied_manifest.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly HttpClient _httpClient;

    public BepInExEnvironmentProvisionerAdapter(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
    }

    public async Task<Result<ProvisioningReport, ProvisioningError>> ProvisionEnvironmentAsync(
        GameEnvironment gameEnv,
        ProvisioningSpec spec,
        CandiedStorageLayout storageLayout,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(gameEnv);
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(storageLayout);

        if (string.IsNullOrWhiteSpace(gameEnv.GameRootPath) || !Directory.Exists(gameEnv.GameRootPath))
        {
            return Result.Fail<ProvisioningReport, ProvisioningError>(
                new ProvisioningError($"Game root path does not exist: '{gameEnv.GameRootPath}'"));
        }

        var version = !string.IsNullOrWhiteSpace(spec.TargetVersion) ? spec.TargetVersion : DefaultBepInExVersion;
        var architecture = string.Equals(gameEnv.Architecture, "x86", StringComparison.OrdinalIgnoreCase) ? "x86" : "x64";
        var archiveFileName = $"BepInEx_{architecture}_{version}.zip";
        var cacheDir = storageLayout.BepInExCacheDir;
        var cachedArchivePath = Path.Combine(cacheDir, archiveFileName);

        try
        {
            // 1. Resolve or Download archive to local cache
            if (!File.Exists(cachedArchivePath) || spec.ForceReinstall)
            {
                Directory.CreateDirectory(cacheDir);
                var downloadUrl = $"https://github.com/BepInEx/BepInEx/releases/download/v{version}/{archiveFileName}";
                var tempDownloadPath = Path.Combine(cacheDir, $"{archiveFileName}.tmp_{Guid.NewGuid():N}");

                try
                {
                    using (var response = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
                    {
                        if (!response.IsSuccessStatusCode)
                        {
                            return Result.Fail<ProvisioningReport, ProvisioningError>(
                                new ProvisioningError($"Failed to download BepInEx package from GitHub: HTTP {response.StatusCode} ({downloadUrl})"));
                        }

                        using var contentStream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                        using var fileStream = new FileStream(tempDownloadPath, FileMode.Create, FileAccess.Write, FileShare.None);
                        await contentStream.CopyToAsync(fileStream, ct).ConfigureAwait(false);
                    }

                    if (File.Exists(cachedArchivePath))
                    {
                        File.Delete(cachedArchivePath);
                    }

                    File.Move(tempDownloadPath, cachedArchivePath);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    if (File.Exists(tempDownloadPath))
                    {
                        try { File.Delete(tempDownloadPath); } catch { /* ignore temp cleanup error */ }
                    }

                    return Result.Fail<ProvisioningReport, ProvisioningError>(
                        new ProvisioningError($"Download error while fetching BepInEx: {ex.Message}"));
                }
            }

            // 2. Extract archive into game root
            var installedRelativePaths = new List<string>();
            using (var zipArchive = ZipFile.OpenRead(cachedArchivePath))
            {
                foreach (var entry in zipArchive.Entries)
                {
                    if (string.IsNullOrEmpty(entry.Name))
                    {
                        // Directory entry
                        continue;
                    }

                    var destinationPath = Path.GetFullPath(Path.Combine(gameEnv.GameRootPath, entry.FullName));
                    var destinationDir = Path.GetDirectoryName(destinationPath);
                    if (!string.IsNullOrEmpty(destinationDir) && !Directory.Exists(destinationDir))
                    {
                        Directory.CreateDirectory(destinationDir);
                    }

                    entry.ExtractToFile(destinationPath, overwrite: true);
                    installedRelativePaths.Add(entry.FullName.Replace('\\', '/'));
                }
            }

            // 3. Pre-configure BepInEx logging
            var configDir = Path.Combine(gameEnv.GameRootPath, "BepInEx", "config");
            var configPath = Path.Combine(configDir, "BepInEx.cfg");
            Directory.CreateDirectory(configDir);

            if (!File.Exists(configPath) || spec.EnableConsoleLogging)
            {
                var cfgContent = BepInExConfigTemplateRenderer.RenderDefaultConfig(spec.EnableConsoleLogging);
                await File.WriteAllTextAsync(configPath, cfgContent, ct).ConfigureAwait(false);
                var relCfgPath = "BepInEx/config/BepInEx.cfg";
                if (!installedRelativePaths.Contains(relCfgPath))
                {
                    installedRelativePaths.Add(relCfgPath);
                }
            }

            // 4. Write tracking manifest
            var manifest = new ProvisioningManifest(
                GameName: gameEnv.GameName,
                ModLoaderVersion: version,
                Architecture: architecture,
                InstalledAtUtc: DateTime.UtcNow.ToString("O"),
                InstalledRelativePaths: installedRelativePaths
            );

            var manifestPath = Path.Combine(gameEnv.GameRootPath, ManifestFileName);
            var manifestJson = JsonSerializer.Serialize(manifest, JsonOptions);
            await File.WriteAllTextAsync(manifestPath, manifestJson, ct).ConfigureAwait(false);

            var report = new ProvisioningReport(
                Success: true,
                ModLoaderName: "BepInEx",
                Version: version,
                Architecture: architecture,
                FilesInstalledCount: installedRelativePaths.Count,
                ConsoleLoggingConfigured: true,
                ManifestPath: manifestPath
            );

            return Result.Ok<ProvisioningReport, ProvisioningError>(report);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Result.Fail<ProvisioningReport, ProvisioningError>(
                new ProvisioningError($"Failed to provision BepInEx environment: {ex.Message}"));
        }
    }

    public async Task<Result<RollbackReport, ProvisioningError>> RollbackEnvironmentAsync(
        GameEnvironment gameEnv,
        CandiedStorageLayout storageLayout,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(gameEnv);

        var manifestPath = Path.Combine(gameEnv.GameRootPath, ManifestFileName);
        if (!File.Exists(manifestPath))
        {
            return Result.Fail<RollbackReport, ProvisioningError>(
                new ProvisioningError($"No Candied provisioning manifest ('{ManifestFileName}') found in game directory: '{gameEnv.GameRootPath}'"));
        }

        try
        {
            var manifestJson = await File.ReadAllTextAsync(manifestPath, ct).ConfigureAwait(false);
            var manifest = JsonSerializer.Deserialize<ProvisioningManifest>(manifestJson, JsonOptions);

            if (manifest == null || manifest.InstalledRelativePaths.Count == 0)
            {
                return Result.Fail<RollbackReport, ProvisioningError>(
                    new ProvisioningError("Provisioning manifest is empty or invalid."));
            }

            var removedFiles = new List<string>();

            // Remove tracked files
            foreach (var relPath in manifest.InstalledRelativePaths)
            {
                var fullPath = Path.Combine(gameEnv.GameRootPath, relPath);
                if (File.Exists(fullPath))
                {
                    File.Delete(fullPath);
                    removedFiles.Add(relPath);
                }
            }

            // Clean up empty directories in BepInEx tree
            var bepInExRoot = Path.Combine(gameEnv.GameRootPath, "BepInEx");
            if (Directory.Exists(bepInExRoot))
            {
                TryDeleteEmptyDirectory(Path.Combine(bepInExRoot, "patchers"));
                TryDeleteEmptyDirectory(Path.Combine(bepInExRoot, "plugins"));
                TryDeleteEmptyDirectory(Path.Combine(bepInExRoot, "config"));
                TryDeleteEmptyDirectory(Path.Combine(bepInExRoot, "core"));
                TryDeleteEmptyDirectory(bepInExRoot);
            }

            // Remove the manifest itself
            if (File.Exists(manifestPath))
            {
                File.Delete(manifestPath);
                removedFiles.Add(ManifestFileName);
            }

            return Result.Ok<RollbackReport, ProvisioningError>(
                new RollbackReport(true, removedFiles.Count, removedFiles));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Result.Fail<RollbackReport, ProvisioningError>(
                new ProvisioningError($"Rollback failed: {ex.Message}"));
        }
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
            // Non-critical directory deletion failure ignored
        }
    }
}
