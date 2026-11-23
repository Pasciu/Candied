using System;
using System.IO;
using AssetsTools.NET.Texture;

namespace Candied.Decompiler.Internal.Assets;

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
            // When packed rotated, the slice in the atlas has transposed dimensions:
            // sliceWidth = rectHeight, sliceHeight = rectWidth
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

            // Rotate back 90 degrees clockwise to restore original upright sprite
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
                    dst[dstIdx] = src[srcIdx];         // B
                    dst[dstIdx + 1] = src[srcIdx + 1]; // G
                    dst[dstIdx + 2] = src[srcIdx + 2]; // R
                    dst[dstIdx + 3] = src[srcIdx + 3]; // A
                }
            }
        }

        return dst;
    }

    public static void SaveBgraToPng(byte[] bgraData, int width, int height, string filePath)
    {
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        using var fs = File.Open(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
        TextureOperations.WriteRawImage(bgraData, width, height, fs, ImageExportType.Png, 100);
    }
}
