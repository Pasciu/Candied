using System;

namespace Candied.Vision.Hosting;

public static class ViewportCalculator
{
    public const int ReferenceDesignWidth = 1920;
    public const int ReferenceDesignHeight = 1080;

    public static (int x, int y, int width, int height) CalculateLetterbox(
        int panelWidth,
        int panelHeight,
        int gameInternalWidth,
        int gameInternalHeight)
    {
        if (panelWidth <= 0 || panelHeight <= 0 || gameInternalWidth <= 0 || gameInternalHeight <= 0)
        {
            return (0, 0, Math.Max(1, panelWidth), Math.Max(1, panelHeight));
        }

        double targetAspect = (double)gameInternalWidth / gameInternalHeight;
        double panelAspect = (double)panelWidth / panelHeight;

        if (panelAspect > targetAspect)
        {
            int h = panelHeight;
            int w = (int)Math.Round(panelHeight * targetAspect);
            int x = (panelWidth - w) / 2;
            int y = 0;
            return (x, y, w, h);
        }
        else
        {
            int w = panelWidth;
            int h = (int)Math.Round(panelWidth / targetAspect);
            int x = 0;
            int y = (panelHeight - h) / 2;
            return (x, y, w, h);
        }
    }

    public static (int gameX, int gameY, bool isInBounds) PanelToGameCoordinates(
        int panelX,
        int panelY,
        int letterboxX,
        int letterboxY,
        int letterboxW,
        int letterboxH,
        int gameInternalWidth,
        int gameInternalHeight)
    {
        if (letterboxW <= 0 || letterboxH <= 0 || gameInternalWidth <= 0 || gameInternalHeight <= 0)
        {
            return (-1, -1, false);
        }

        if (panelX < letterboxX || panelX >= letterboxX + letterboxW ||
            panelY < letterboxY || panelY >= letterboxY + letterboxH)
        {
            return (-1, -1, false);
        }

        int gameX = (int)Math.Round((double)(panelX - letterboxX) * gameInternalWidth / letterboxW);
        int gameY = (int)Math.Round((double)(panelY - letterboxY) * gameInternalHeight / letterboxH);

        gameX = Math.Clamp(gameX, 0, gameInternalWidth - 1);
        gameY = Math.Clamp(gameY, 0, gameInternalHeight - 1);

        return (gameX, gameY, true);
    }

    public static (int panelX, int panelY) GameToPanelCoordinates(
        int gameX,
        int gameY,
        int letterboxX,
        int letterboxY,
        int letterboxW,
        int letterboxH,
        int gameInternalWidth,
        int gameInternalHeight)
    {
        if (gameInternalWidth <= 0 || gameInternalHeight <= 0)
        {
            return (letterboxX, letterboxY);
        }

        int panelX = letterboxX + (int)Math.Round((double)gameX * letterboxW / gameInternalWidth);
        int panelY = letterboxY + (int)Math.Round((double)gameY * letterboxH / gameInternalHeight);
        return (panelX, panelY);
    }

    public static (double dipWidth, double dipHeight, int pixelWidth, int pixelHeight) CalculateStageViewport(
        double availableDipWidth,
        double availableDipHeight,
        double dpiScaleX,
        double dpiScaleY,
        string? presetKey = "auto_16_9")
    {
        double scaleX = dpiScaleX > 0 ? dpiScaleX : 1.0;
        double scaleY = dpiScaleY > 0 ? dpiScaleY : 1.0;

        int availPxW = Math.Max(320, (int)Math.Floor(availableDipWidth * scaleX));
        int availPxH = Math.Max(180, (int)Math.Floor(availableDipHeight * scaleY));

        int maxPxW = int.MaxValue;
        int maxPxH = int.MaxValue;

        if (string.Equals(presetKey, "960x540", StringComparison.OrdinalIgnoreCase))
        {
            maxPxW = 960;
            maxPxH = 540;
        }
        else if (string.Equals(presetKey, "1280x720", StringComparison.OrdinalIgnoreCase))
        {
            maxPxW = 1280;
            maxPxH = 720;
        }
        else if (string.Equals(presetKey, "1920x1080", StringComparison.OrdinalIgnoreCase))
        {
            maxPxW = 1920;
            maxPxH = 1080;
        }

        int boundedPxW = Math.Min(availPxW, maxPxW);
        int boundedPxH = Math.Min(availPxH, maxPxH);

        var (_, _, strictPxW, strictPxH) = CalculateLetterbox(boundedPxW, boundedPxH, ReferenceDesignWidth, ReferenceDesignHeight);
        strictPxW = Math.Max(320, strictPxW);
        strictPxH = Math.Max(180, strictPxH);

        double dipW = strictPxW / scaleX;
        double dipH = strictPxH / scaleY;

        return (dipW, dipH, strictPxW, strictPxH);
    }

    public static (int refX, int refY) MapToReference1080p(int frameX, int frameY, int frameWidth, int frameHeight)
    {
        if (frameWidth <= 0 || frameHeight <= 0)
        {
            return (0, 0);
        }

        int refX = (int)Math.Round((double)frameX * ReferenceDesignWidth / frameWidth);
        int refY = (int)Math.Round((double)frameY * ReferenceDesignHeight / frameHeight);

        refX = Math.Clamp(refX, 0, ReferenceDesignWidth - 1);
        refY = Math.Clamp(refY, 0, ReferenceDesignHeight - 1);
        return (refX, refY);
    }

    public static (int frameX, int frameY, int ref1080X, int ref1080Y, bool isInBounds) ImageControlToFrameCoordinates(
        double controlX,
        double controlY,
        double controlWidth,
        double controlHeight,
        int bitmapPixelWidth,
        int bitmapPixelHeight)
    {
        if (controlWidth <= 0 || controlHeight <= 0 || bitmapPixelWidth <= 0 || bitmapPixelHeight <= 0)
        {
            return (-1, -1, -1, -1, false);
        }

        var (boxX, boxY, boxW, boxH) = CalculateLetterbox(
            (int)Math.Round(controlWidth),
            (int)Math.Round(controlHeight),
            bitmapPixelWidth,
            bitmapPixelHeight);

        var (frameX, frameY, inBounds) = PanelToGameCoordinates(
            (int)Math.Round(controlX),
            (int)Math.Round(controlY),
            boxX,
            boxY,
            boxW,
            boxH,
            bitmapPixelWidth,
            bitmapPixelHeight);

        if (!inBounds)
        {
            return (-1, -1, -1, -1, false);
        }

        var (refX, refY) = MapToReference1080p(frameX, frameY, bitmapPixelWidth, bitmapPixelHeight);
        return (frameX, frameY, refX, refY, true);
    }
}
