using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using AssetsTools.NET;
using AssetsTools.NET.Extra;
using AssetsTools.NET.Texture;
using Candied.Core.Models;

namespace Candied.Platforms.Unity.Shared.Internal.Assets;

internal static class FileNameDisambiguator
{
    public static string SanitizeFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "unnamed";
        }

        var invalid = Path.GetInvalidFileNameChars();
        var chars = name.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            if (Array.IndexOf(invalid, chars[i]) >= 0)
            {
                chars[i] = '_';
            }
        }

        string sanitized = new string(chars).Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? "unnamed" : sanitized;
    }

    public static string GetDisambiguatedFilePath(
        string directory,
        string baseName,
        string extension,
        long assetKey,
        bool disambiguateCollisions = true)
    {
        string safeBaseName = SanitizeFileName(baseName);
        string cleanExt = extension.TrimStart('.');

        if (!disambiguateCollisions)
        {
            return Path.Combine(directory, $"{safeBaseName}.{cleanExt}");
        }

        string candidate = Path.Combine(directory, $"{safeBaseName}.{cleanExt}");
        if (!File.Exists(candidate))
        {
            return candidate;
        }

        string withKey = Path.Combine(directory, $"{safeBaseName}_{assetKey}.{cleanExt}");
        if (!File.Exists(withKey))
        {
            return withKey;
        }

        int counter = 1;
        while (true)
        {
            string withCounter = Path.Combine(directory, $"{safeBaseName}_{assetKey}_{counter}.{cleanExt}");
            if (!File.Exists(withCounter))
            {
                return withCounter;
            }

            counter++;
        }
    }
}

internal static class UnityTextureDecoder
{
    public static byte[] CropSprite(
        byte[] bgraData,
        int texWidth,
        int texHeight,
        int rectX,
        int rectY,
        int rectWidth,
        int rectHeight,
        bool isPackedRotated)
    {
        if (bgraData == null || bgraData.Length == 0 || texWidth <= 0 || texHeight <= 0)
        {
            throw new ArgumentException("Invalid texture data or dimensions.");
        }

        if (isPackedRotated)
        {
            int sliceW = rectHeight;
            int sliceH = rectWidth;

            byte[] rotatedSlice = new byte[sliceW * sliceH * 4];
            for (int row = 0; row < sliceH; row++)
            {
                int srcY = texHeight - (rectY + sliceH) + row;
                if (srcY < 0 || srcY >= texHeight) continue;

                int srcOffset = (srcY * texWidth + rectX) * 4;
                int dstOffset = row * sliceW * 4;

                int copyBytes = Math.Min(sliceW * 4, Math.Max(0, bgraData.Length - srcOffset));
                if (copyBytes > 0 && dstOffset + copyBytes <= rotatedSlice.Length)
                {
                    Buffer.BlockCopy(bgraData, srcOffset, rotatedSlice, dstOffset, copyBytes);
                }
            }

            return RotateBgra90Clockwise(rotatedSlice, sliceW, sliceH);
        }
        else
        {
            int rw = rectWidth;
            int rh = rectHeight;
            byte[] spriteBgra = new byte[rw * rh * 4];

            for (int row = 0; row < rh; row++)
            {
                int srcY = texHeight - (rectY + rh) + row;
                if (srcY < 0 || srcY >= texHeight) continue;

                int srcOffset = (srcY * texWidth + rectX) * 4;
                int dstOffset = row * rw * 4;

                int copyBytes = Math.Min(rw * 4, Math.Max(0, bgraData.Length - srcOffset));
                if (copyBytes > 0 && dstOffset + copyBytes <= spriteBgra.Length)
                {
                    Buffer.BlockCopy(bgraData, srcOffset, spriteBgra, dstOffset, copyBytes);
                }
            }

            return spriteBgra;
        }
    }

    public static byte[] RotateBgra90Clockwise(byte[] src, int srcW, int srcH)
    {
        int dstW = srcH;
        int dstH = srcW;
        byte[] dst = new byte[dstW * dstH * 4];

        for (int y = 0; y < srcH; y++)
        {
            for (int x = 0; x < srcW; x++)
            {
                int srcIdx = (y * srcW + x) * 4;
                int dstX = srcH - 1 - y;
                int dstY = x;
                int dstIdx = (dstY * dstW + dstX) * 4;

                if (srcIdx + 4 <= src.Length && dstIdx + 4 <= dst.Length)
                {
                    dst[dstIdx] = src[srcIdx];
                    dst[dstIdx + 1] = src[srcIdx + 1];
                    dst[dstIdx + 2] = src[srcIdx + 2];
                    dst[dstIdx + 3] = src[srcIdx + 3];
                }
            }
        }

        return dst;
    }

    public static void SaveBgraToPng(byte[] bgraData, int width, int height, string filePath)
    {
        string? dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        using var fs = File.Open(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
        TextureOperations.WriteRawImage(bgraData, width, height, fs, ImageExportType.Png, 100);
    }
}

internal static class UnityCrossFileResolver
{
    public static (AssetsFileInstance? file, AssetFileInfo? info, AssetTypeValueField? baseField) ResolveAsset(
        AssetsManager am,
        AssetsFileInstance currentFile,
        AssetTypeValueField pptrField)
    {
        if (am == null || currentFile == null || pptrField == null)
        {
            return (null, null, null);
        }

        try
        {
            int fileId = pptrField["m_FileID"].AsInt;
            long pathId = pptrField["m_PathID"].AsLong;

            if (fileId == 0)
            {
                var localInfo = currentFile.file.GetAssetInfo(pathId);
                if (localInfo != null)
                {
                    var baseField = am.GetBaseField(currentFile, localInfo);
                    return (currentFile, localInfo, baseField);
                }
            }

            var ext = am.GetExtAsset(currentFile, pptrField);
            if (ext.info != null && ext.baseField != null)
            {
                return (ext.file, ext.info, ext.baseField);
            }

            try
            {
                am.LoadDependencies(currentFile);
                ext = am.GetExtAsset(currentFile, pptrField);
                if (ext.info != null && ext.baseField != null)
                {
                    return (ext.file, ext.info, ext.baseField);
                }
            }
            catch
            {
            }

            return (null, null, null);
        }
        catch
        {
            return (null, null, null);
        }
    }
}

internal static class UnityAssetContainerScanner
{
    public static List<string> CollectAssetContainers(TargetDescriptor target)
    {
        var containers = new List<string>();
        string rootDir = Directory.Exists(target.TargetPath)
            ? target.TargetPath
            : (Path.GetDirectoryName(target.TargetPath) ?? string.Empty);

        string dataDir = target.Metadata.TryGetValue("dataDirectory", out var metaDataDir) && !string.IsNullOrWhiteSpace(metaDataDir)
            ? metaDataDir
            : Path.Combine(rootDir, $"{target.Name}_Data");

        if (Directory.Exists(dataDir))
        {
            foreach (var file in Directory.GetFiles(dataDir, "*.assets", SearchOption.TopDirectoryOnly))
            {
                containers.Add(file);
            }

            string streamingAssetsDir = Path.Combine(dataDir, "StreamingAssets");
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

            string fileName = Path.GetFileName(containerPath);
            string ext = Path.GetExtension(containerPath).ToLowerInvariant();

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
                            if (dirInfo.Flags == 0x04)
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
                                }
                            }
                        }
                    }
                }
                catch
                {
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
                        }
                    }
                }
            }
        }
    }
}

internal static class UnityMultiTypeAssetExtractor
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public static ExtractedAssetInfo? ExtractTexture2D(
        AssetsManager am,
        AssetsFileInstance inst,
        AssetFileInfo inf,
        string outputDirectory,
        bool disambiguateCollisions,
        string containerName)
    {
        try
        {
            var baseField = am.GetBaseField(inst, inf);
            string name = baseField["m_Name"].AsString;
            if (string.IsNullOrWhiteSpace(name)) return null;

            var tf = TextureFile.ReadTextureFile(baseField);
            tf.FillPictureData(inst);

            string outPath = FileNameDisambiguator.GetDisambiguatedFilePath(
                outputDirectory, name, "png", inf.PathId, disambiguateCollisions);

            string? dir = Path.GetDirectoryName(outPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            using (var fs = File.Open(outPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                tf.DecodeTextureImage(tf.pictureData, fs, ImageExportType.Png, 100);
            }

            var fi = new FileInfo(outPath);
            return new ExtractedAssetInfo(name, "Texture2D", outPath, fi.Length, tf.m_Width, tf.m_Height, inf.PathId, containerName);
        }
        catch
        {
            return null;
        }
    }

    public static ExtractedAssetInfo? ExtractSprite(
        AssetsManager am,
        AssetsFileInstance inst,
        AssetFileInfo inf,
        string outputDirectory,
        bool disambiguateCollisions,
        string containerName)
    {
        try
        {
            var baseField = am.GetBaseField(inst, inf);
            string name = baseField["m_Name"].AsString;
            if (string.IsNullOrWhiteSpace(name)) return null;

            var rect = baseField["m_Rect"];
            int rx = (int)rect["x"].AsFloat;
            int ry = (int)rect["y"].AsFloat;
            int rw = (int)rect["width"].AsFloat;
            int rh = (int)rect["height"].AsFloat;
            if (rw <= 0 || rh <= 0) return null;

            var rd = baseField["m_RD"];
            var texRef = rd["texture"];
            var (texFile, texInf, texBase) = UnityCrossFileResolver.ResolveAsset(am, inst, texRef);
            if (texFile == null || texInf == null || texBase == null) return null;

            var tf = TextureFile.ReadTextureFile(texBase);
            tf.FillPictureData(texFile);

            byte[] bgraData = tf.DecodeTextureRaw(tf.pictureData, true);
            if (bgraData == null || bgraData.Length == 0) return null;

            uint settingsRaw = rd["settingsRaw"].AsUInt;
            bool isPackedRotated = (settingsRaw & 0x4) != 0;

            byte[] spriteBgra = UnityTextureDecoder.CropSprite(
                bgraData, tf.m_Width, tf.m_Height, rx, ry, rw, rh, isPackedRotated);

            string outPath = FileNameDisambiguator.GetDisambiguatedFilePath(
                outputDirectory, name, "png", inf.PathId, disambiguateCollisions);

            UnityTextureDecoder.SaveBgraToPng(spriteBgra, rw, rh, outPath);
            var fi = new FileInfo(outPath);
            return new ExtractedAssetInfo(name, "Sprite", outPath, fi.Length, rw, rh, inf.PathId, containerName);
        }
        catch
        {
            return null;
        }
    }

    public static ExtractedAssetInfo? ExtractTextAsset(
        AssetsManager am,
        AssetsFileInstance inst,
        AssetFileInfo inf,
        string outputDirectory,
        bool disambiguateCollisions,
        string containerName)
    {
        try
        {
            var baseField = am.GetBaseField(inst, inf);
            string name = baseField["m_Name"].AsString;
            if (string.IsNullOrWhiteSpace(name)) return null;

            var scriptField = baseField["m_Script"];
            byte[] bytes = scriptField.Value?.ValueType == AssetValueType.ByteArray
                ? scriptField.AsByteArray
                : Encoding.UTF8.GetBytes(scriptField.AsString ?? string.Empty);

            string ext = GuessTextExtension(bytes, name);
            string outPath = FileNameDisambiguator.GetDisambiguatedFilePath(
                outputDirectory, name, ext, inf.PathId, disambiguateCollisions);

            string? dir = Path.GetDirectoryName(outPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllBytes(outPath, bytes);
            return new ExtractedAssetInfo(name, "TextAsset", outPath, bytes.Length, 0, 0, inf.PathId, containerName);
        }
        catch
        {
            return null;
        }
    }

    public static ExtractedAssetInfo? ExtractMonoBehaviour(
        AssetsManager am,
        AssetsFileInstance inst,
        AssetFileInfo inf,
        string outputDirectory,
        bool disambiguateCollisions,
        string containerName)
    {
        try
        {
            var baseField = am.GetBaseField(inst, inf);
            string? name = baseField["m_Name"]?.AsString;
            if (string.IsNullOrWhiteSpace(name))
            {
                name = $"MonoBehaviour_{inf.PathId}";
            }

            var rootObj = ConvertFieldToJsonObject(baseField);
            string json = JsonSerializer.Serialize(rootObj, JsonOpts);

            string outPath = FileNameDisambiguator.GetDisambiguatedFilePath(
                outputDirectory, name, "json", inf.PathId, disambiguateCollisions);

            string? dir = Path.GetDirectoryName(outPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(outPath, json, Encoding.UTF8);
            var fi = new FileInfo(outPath);
            return new ExtractedAssetInfo(name, "MonoBehaviour", outPath, fi.Length, 0, 0, inf.PathId, containerName);
        }
        catch
        {
            return null;
        }
    }

    private static string GuessTextExtension(byte[] bytes, string name)
    {
        if (Path.HasExtension(name))
        {
            string ext = Path.GetExtension(name).TrimStart('.');
            if (!string.IsNullOrWhiteSpace(ext)) return ext;
        }

        if (bytes == null || bytes.Length == 0) return "txt";

        string preview = Encoding.UTF8.GetString(bytes, 0, Math.Min(bytes.Length, 256)).TrimStart();
        if (preview.StartsWith('{') || preview.StartsWith('[')) return "json";
        if (preview.StartsWith('<')) return "xml";
        if (preview.Contains(',', StringComparison.Ordinal) && preview.Contains('\n', StringComparison.Ordinal)) return "csv";

        return "txt";
    }

    public static object? ConvertFieldToJsonObject(AssetTypeValueField field)
    {
        if (field == null) return null;
        var vt = field.Value?.ValueType ?? AssetValueType.None;

        switch (vt)
        {
            case AssetValueType.Bool: return field.AsBool;
            case AssetValueType.Int8:
            case AssetValueType.Int16:
            case AssetValueType.Int32: return field.AsInt;
            case AssetValueType.UInt8:
            case AssetValueType.UInt16:
            case AssetValueType.UInt32: return field.AsUInt;
            case AssetValueType.Int64: return field.AsLong;
            case AssetValueType.UInt64: return field.AsULong;
            case AssetValueType.Float: return field.AsFloat;
            case AssetValueType.Double: return field.AsDouble;
            case AssetValueType.String: return field.AsString;
            case AssetValueType.ByteArray: return Convert.ToBase64String(field.AsByteArray);
            case AssetValueType.Array:
                var list = new List<object?>();
                foreach (var child in field.Children)
                {
                    list.Add(ConvertFieldToJsonObject(child));
                }

                return list;
            default:
                if (field.Children.Count > 0)
                {
                    var dict = new Dictionary<string, object?>();
                    foreach (var child in field.Children)
                    {
                        dict[child.FieldName] = ConvertFieldToJsonObject(child);
                    }

                    return dict;
                }

                return null;
        }
    }
}
