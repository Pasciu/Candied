using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using AssetsTools.NET.Extra;
using Candied.Core.Models;

namespace Candied.Decompiler.Internal.Assets;

internal static class UnityAssetContainerScanner
{
    public static List<string> CollectAssetContainers(GameEnvironment game)
    {
        var containers = new List<string>();

        string dataDir = !string.IsNullOrWhiteSpace(game.ManagedPath)
            ? Path.GetDirectoryName(game.ManagedPath.TrimEnd('/', '\\')) ?? game.GameRootPath
            : Path.Combine(game.GameRootPath, $"{game.GameName}_Data");

        if (Directory.Exists(dataDir))
        {
            // 1. Data folder root archives (*.assets)
            foreach (var file in Directory.GetFiles(dataDir, "*.assets", SearchOption.TopDirectoryOnly))
            {
                containers.Add(file);
            }

            // 2. StreamingAssets subfolder
            var streamingAssetsDir = Path.Combine(dataDir, "StreamingAssets");
            if (Directory.Exists(streamingAssetsDir))
            {
                var extensions = new[] { "*.bundle", "*.assets", "*.ab", "*.unity3d" };
                foreach (var ext in extensions)
                {
                    foreach (var file in Directory.GetFiles(streamingAssetsDir, ext, SearchOption.AllDirectories))
                    {
                        if (!containers.Contains(file))
                        {
                            containers.Add(file);
                        }
                    }
                }
            }
        }

        return containers;
    }

    public static void ProcessContainers(
        AssetsManager am,
        IEnumerable<string> containers,
        Action<AssetsFileInstance, string, string> onAssetFileLoaded,
        CancellationToken ct = default)
    {
        foreach (var containerPath in containers)
        {
            if (ct.IsCancellationRequested) break;

            var fileName = Path.GetFileName(containerPath);
            var ext = Path.GetExtension(containerPath).ToLowerInvariant();

            if (ext == ".assets")
            {
                try
                {
                    var inst = am.LoadAssetsFile(containerPath, true);
                    if (inst?.file?.Metadata != null)
                    {
                        am.LoadClassDatabaseFromPackage(inst.file.Metadata.UnityVersion);
                        onAssetFileLoaded(inst, fileName, containerPath);
                    }
                }
                catch
                {
                    // Ignore corrupted or incompatible assets file
                }
            }
            else if (ext is ".bundle" or ".ab" or ".unity3d")
            {
                BundleFileInstance? bundle = null;
                try
                {
                    bundle = am.LoadBundleFile(containerPath, true);
                    if (bundle?.file?.BlockAndDirInfo != null)
                    {
                        for (int i = 0; i < bundle.file.BlockAndDirInfo.DirectoryInfos.Count; i++)
                        {
                            if (ct.IsCancellationRequested) break;

                            var dirInfo = bundle.file.BlockAndDirInfo.DirectoryInfos[i];
                            if (dirInfo.Flags == 0x04) // Directory is an assets file
                            {
                                try
                                {
                                    var inst = am.LoadAssetsFileFromBundle(bundle, i, true);
                                    if (inst?.file?.Metadata != null)
                                    {
                                        am.LoadClassDatabaseFromPackage(inst.file.Metadata.UnityVersion);
                                        onAssetFileLoaded(inst, fileName, containerPath);
                                    }
                                }
                                catch
                                {
                                    // Skip single malformed file inside bundle
                                }
                            }
                        }
                    }
                }
                catch
                {
                    // Ignore corrupted bundle
                }
                finally
                {
                    if (bundle != null)
                    {
                        try
                        {
                            am.UnloadBundleFile(bundle);
                        }
                        catch
                        {
                            // Ignore unload errors
                        }
                    }
                }
            }
        }
    }
}
