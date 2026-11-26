using System;

namespace Candied.Vision.Protocol;

public static class CoordinateConverter
{
    /// <summary>
    /// Converts a bottom-left origin screen-space bounding rectangle (Y up)
    /// into standard image coordinates (top-left origin, Y down).
    /// </summary>
    public static VisionRectDto BottomLeftToImagePixelRect(float screenX, float screenY, float width, float height, int screenHeight)
    {
        float imageX = screenX;
        float imageY = screenHeight - (screenY + height);
        return new VisionRectDto(imageX, imageY, width, height);
    }

    /// <summary>
    /// Computes normalized coordinates (0.0 to 1.0) in top-left origin space.
    /// </summary>
    public static VisionRectDto ToNormalizedRect(VisionRectDto pixelRect, int screenWidth, int screenHeight)
    {
        if (pixelRect == null)
        {
            throw new ArgumentNullException(nameof(pixelRect));
        }

        if (screenWidth <= 0 || screenHeight <= 0)
        {
            return new VisionRectDto(0, 0, 0, 0);
        }

        return new VisionRectDto(
            pixelRect.X / screenWidth,
            pixelRect.Y / screenHeight,
            pixelRect.Width / screenWidth,
            pixelRect.Height / screenHeight);
    }

    /// <summary>
    /// Converts an image-space point (top-left origin, Y down)
    /// to bottom-left origin screen coordinates (Y up).
    /// </summary>
    public static VisionPointDto ImageToBottomLeftScreenPoint(float imageX, float imageY, int screenHeight)
    {
        return new VisionPointDto(imageX, screenHeight - imageY);
    }
}
