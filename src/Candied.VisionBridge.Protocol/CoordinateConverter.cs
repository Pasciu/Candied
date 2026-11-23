using System;

namespace Candied.VisionBridge.Protocol;

public static class CoordinateConverter
{
    /// <summary>
    /// Converts a Unity screen-space bounding rectangle (bottom-left origin, Y up)
    /// into standard image coordinates (top-left origin, Y down).
    /// </summary>
    public static VisionRectDto UnityToImagePixelRect(float unityX, float unityY, float unityW, float unityH, int screenHeight)
    {
        float imageX = unityX;
        float imageY = screenHeight - (unityY + unityH);
        return new VisionRectDto(imageX, imageY, unityW, unityH);
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
    /// to Unity screen coordinates (bottom-left origin, Y up) for EventSystem raycasting.
    /// </summary>
    public static VisionPointDto ImageToUnityScreenPoint(float imageX, float imageY, int screenHeight)
    {
        return new VisionPointDto(imageX, screenHeight - imageY);
    }
}
