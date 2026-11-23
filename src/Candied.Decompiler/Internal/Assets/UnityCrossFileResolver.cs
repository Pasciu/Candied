using System;
using AssetsTools.NET;
using AssetsTools.NET.Extra;

namespace Candied.Decompiler.Internal.Assets;

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

            // Try resolving external reference
            var ext = am.GetExtAsset(currentFile, pptrField);
            if (ext.info != null && ext.baseField != null)
            {
                return (ext.file, ext.info, ext.baseField);
            }

            // If not found, attempt loading dependencies for current file and retry
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
                // Dependency file might be unavailable or unparseable
            }

            return (null, null, null);
        }
        catch
        {
            return (null, null, null);
        }
    }
}
