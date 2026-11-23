using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using AssetsTools.NET;
using AssetsTools.NET.Extra;
using AssetsTools.NET.Texture;
using Candied.Core.Models;

namespace Candied.Decompiler.Internal.Assets;

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
            var name = baseField["m_Name"].AsString;
            if (string.IsNullOrWhiteSpace(name)) return null;

            var tf = TextureFile.ReadTextureFile(baseField);
            tf.FillPictureData(inst);

            var outPath = FileNameDisambiguator.GetDisambiguatedFilePath(
                outputDirectory, name, "png", inf.PathId, disambiguateCollisions);

            var dir = Path.GetDirectoryName(outPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            using (var fs = File.Open(outPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                tf.DecodeTextureImage(tf.pictureData, fs, ImageExportType.Png, 100);
            }

            var fi = new FileInfo(outPath);
            return new ExtractedAssetInfo(
                Name: name,
                AssetType: "Texture2D",
                OutputFilePath: outPath,
                FileSizeBytes: fi.Length,
                Width: tf.m_Width,
                Height: tf.m_Height,
                PathId: inf.PathId,
                SourceContainer: containerName);
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
            var name = baseField["m_Name"].AsString;
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

            var outPath = FileNameDisambiguator.GetDisambiguatedFilePath(
                outputDirectory, name, "png", inf.PathId, disambiguateCollisions);

            UnityTextureDecoder.SaveBgraToPng(spriteBgra, rw, rh, outPath);

            var fi = new FileInfo(outPath);
            return new ExtractedAssetInfo(
                Name: name,
                AssetType: "Sprite",
                OutputFilePath: outPath,
                FileSizeBytes: fi.Length,
                Width: rw,
                Height: rh,
                PathId: inf.PathId,
                SourceContainer: containerName);
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
            var name = baseField["m_Name"].AsString;
            if (string.IsNullOrWhiteSpace(name)) return null;

            var scriptField = baseField["m_Script"];
            byte[] bytes;
            if (scriptField.Value?.ValueType == AssetValueType.ByteArray)
            {
                bytes = scriptField.AsByteArray;
            }
            else
            {
                var text = scriptField.AsString ?? string.Empty;
                bytes = Encoding.UTF8.GetBytes(text);
            }

            var ext = GuessTextExtension(bytes, name);
            var outPath = FileNameDisambiguator.GetDisambiguatedFilePath(
                outputDirectory, name, ext, inf.PathId, disambiguateCollisions);

            var dir = Path.GetDirectoryName(outPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllBytes(outPath, bytes);

            return new ExtractedAssetInfo(
                Name: name,
                AssetType: "TextAsset",
                OutputFilePath: outPath,
                FileSizeBytes: bytes.Length,
                Width: 0,
                Height: 0,
                PathId: inf.PathId,
                SourceContainer: containerName);
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
            var name = baseField["m_Name"]?.AsString;
            if (string.IsNullOrWhiteSpace(name))
            {
                name = $"MonoBehaviour_{inf.PathId}";
            }

            var rootObj = ConvertFieldToJsonObject(baseField);
            var json = JsonSerializer.Serialize(rootObj, JsonOpts);

            var outPath = FileNameDisambiguator.GetDisambiguatedFilePath(
                outputDirectory, name, "json", inf.PathId, disambiguateCollisions);

            var dir = Path.GetDirectoryName(outPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(outPath, json, Encoding.UTF8);
            var fi = new FileInfo(outPath);

            return new ExtractedAssetInfo(
                Name: name,
                AssetType: "MonoBehaviour",
                OutputFilePath: outPath,
                FileSizeBytes: fi.Length,
                Width: 0,
                Height: 0,
                PathId: inf.PathId,
                SourceContainer: containerName);
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
            var ext = Path.GetExtension(name).TrimStart('.');
            if (!string.IsNullOrWhiteSpace(ext)) return ext;
        }

        if (bytes == null || bytes.Length == 0) return "txt";

        var preview = Encoding.UTF8.GetString(bytes, 0, Math.Min(bytes.Length, 256)).TrimStart();
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
            case AssetValueType.Bool:
                return field.AsBool;
            case AssetValueType.Int8:
            case AssetValueType.Int16:
            case AssetValueType.Int32:
                return field.AsInt;
            case AssetValueType.UInt8:
            case AssetValueType.UInt16:
            case AssetValueType.UInt32:
                return field.AsUInt;
            case AssetValueType.Int64:
                return field.AsLong;
            case AssetValueType.UInt64:
                return field.AsULong;
            case AssetValueType.Float:
                return field.AsFloat;
            case AssetValueType.Double:
                return field.AsDouble;
            case AssetValueType.String:
                return field.AsString;
            case AssetValueType.ByteArray:
                return Convert.ToBase64String(field.AsByteArray);
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
