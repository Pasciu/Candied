using System;
using System.Drawing;
using System.IO;

namespace Candied.Tests.Helpers;

public record PixelDiffResult(
    int TotalPixelsInRegion,
    int ChangedPixels,
    double DiffPercentage,
    bool MeetsCriterion,
    string Summary);

public static class PixelDiffEvaluator
{
    public static PixelDiffResult Evaluate(
        byte[] prePngBytes,
        byte[] postPngBytes,
        Rectangle region,
        double requiredPercentage = 15.0,
        int colorDistanceThreshold = 30)
    {
        if (prePngBytes == null || prePngBytes.Length == 0)
            throw new ArgumentException("Pre PNG bytes cannot be null or empty", nameof(prePngBytes));
        if (postPngBytes == null || postPngBytes.Length == 0)
            throw new ArgumentException("Post PNG bytes cannot be null or empty", nameof(postPngBytes));

        using var msPre = new MemoryStream(prePngBytes);
        using var msPost = new MemoryStream(postPngBytes);
        using var bmpPre = new Bitmap(msPre);
        using var bmpPost = new Bitmap(msPost);

        int minW = Math.Min(bmpPre.Width, bmpPost.Width);
        int minH = Math.Min(bmpPre.Height, bmpPost.Height);

        int startX = Math.Clamp(region.X, 0, minW - 1);
        int startY = Math.Clamp(region.Y, 0, minH - 1);
        int endX = Math.Clamp(region.Right, 0, minW);
        int endY = Math.Clamp(region.Bottom, 0, minH);

        int totalPixels = 0;
        int changedPixels = 0;

        for (int y = startY; y < endY; y++)
        {
            for (int x = startX; x < endX; x++)
            {
                totalPixels++;
                Color c1 = bmpPre.GetPixel(x, y);
                Color c2 = bmpPost.GetPixel(x, y);

                int dist = Math.Abs(c1.R - c2.R) + Math.Abs(c1.G - c2.G) + Math.Abs(c1.B - c2.B);
                if (dist > colorDistanceThreshold)
                {
                    changedPixels++;
                }
            }
        }

        double diffPercentage = totalPixels > 0 ? (double)changedPixels / totalPixels * 100.0 : 0.0;
        bool meets = diffPercentage >= requiredPercentage;

        string summary = $"Region ({region.X},{region.Y},{region.Width}x{region.Height}): " +
                         $"{changedPixels}/{totalPixels} pixels changed ({diffPercentage:F2}%). " +
                         $"Threshold: {requiredPercentage:F2}%. Result: {(meets ? "PASSED" : "FAILED")}";

        return new PixelDiffResult(totalPixels, changedPixels, diffPercentage, meets, summary);
    }
}
