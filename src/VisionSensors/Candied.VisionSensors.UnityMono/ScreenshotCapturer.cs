using System;
using System.Collections;
using System.Reflection;
using UnityEngine;

namespace Candied.VisionSensors.UnityMono;

public sealed class ScreenshotResult
{
    public byte[] PngBytes { get; }
    public bool IsUniform { get; }
    public int Width { get; }
    public int Height { get; }

    public ScreenshotResult(byte[] pngBytes, bool isUniform, int width, int height)
    {
        PngBytes = pngBytes ?? new byte[0];
        IsUniform = isUniform;
        Width = width;
        Height = height;
    }
}

public static class ScreenshotCapturer
{
    public static string ActiveMethod { get; private set; } = "camera_fallback";

    public static string ActiveLimitations
    {
        get
        {
            if (ActiveMethod == "camera_fallback")
            {
                return "camera_fallback renders active depth-sorted cameras into an off-screen RenderTexture. ScreenSpaceOverlay UI canvases or post-effects rendered directly to the final backbuffer after camera rendering are not included.";
            }

            if (ActiveMethod == "screen_capture")
            {
                return "screen_capture reads the native backbuffer; may return black/uniform frames in embedded child windows (WS_CHILD) due to OS swapchain redirection.";
            }

            return "direct_frame_buffer_read";
        }
    }

    public static IEnumerator CaptureCoroutine(Action<ScreenshotResult?> onComplete)
    {
        yield return new WaitForEndOfFrame();

        ScreenshotResult? result = null;
        Texture2D? tex = null;

        try
        {
            int width = Screen.width;
            int height = Screen.height;

            if (width > 0 && height > 0)
            {
                RenderTexture? prevActive = RenderTexture.active;

                try
                {
                    RenderTexture.active = null;
                    Type? screenCaptureType = CapabilitiesDetector.FindType("UnityEngine.ScreenCapture");
                    if (screenCaptureType != null)
                    {
                        MethodInfo? capMethod = screenCaptureType.GetMethod("CaptureScreenshotAsTexture", BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
                        if (capMethod != null)
                        {
                            tex = capMethod.Invoke(null, null) as Texture2D;
                            if (tex != null)
                            {
                                ActiveMethod = "screen_capture";
                            }
                        }
                    }
                }
                catch
                {
                    tex = null;
                }

                if (tex == null)
                {
                    try
                    {
                        RenderTexture.active = null;
                        var directTex = new Texture2D(width, height, TextureFormat.RGB24, false);
                        directTex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                        directTex.Apply();
                        tex = directTex;
                        ActiveMethod = "read_pixels";
                    }
                    catch
                    {
                        tex = null;
                    }
                }

                if (tex == null || CheckUniformity(tex, width, height))
                {
                    Texture2D? camTex = CaptureViaCameras(width, height);
                    if (camTex != null && !CheckUniformity(camTex, width, height))
                    {
                        if (tex != null) UnityEngine.Object.DestroyImmediate(tex);
                        tex = camTex;
                        ActiveMethod = "camera_fallback";
                    }
                    else if (camTex != null)
                    {
                        UnityEngine.Object.DestroyImmediate(camTex);
                    }
                }

                try
                {
                    RenderTexture.active = prevActive;
                }
                catch
                {
                }

                if (tex != null)
                {
                    int actualWidth = tex.width > 0 ? tex.width : width;
                    int actualHeight = tex.height > 0 ? tex.height : height;
                    bool isUniform = CheckUniformity(tex, actualWidth, actualHeight);
                    byte[]? pngBytes = EncodeTextureToPng(tex);

                    if (pngBytes != null && pngBytes.Length > 0)
                    {
                        result = new ScreenshotResult(pngBytes, isUniform, actualWidth, actualHeight);
                    }
                }
            }
        }
        catch
        {
            result = null;
        }
        finally
        {
            if (tex != null)
            {
                UnityEngine.Object.DestroyImmediate(tex);
            }
        }

        onComplete?.Invoke(result);
    }

    private static Texture2D? CaptureViaCameras(int width, int height)
    {
        try
        {
            Camera[] cameras = Camera.allCameras;
            if (cameras == null || cameras.Length == 0) return null;

            Array.Sort(cameras, (a, b) => a.depth.CompareTo(b.depth));

            RenderTexture rt = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
            RenderTexture prevActive = RenderTexture.active;

            try
            {
                foreach (var cam in cameras)
                {
                    if (cam == null || !cam.enabled || !cam.gameObject.activeInHierarchy) continue;

                    RenderTexture prevTarget = cam.targetTexture;
                    cam.targetTexture = rt;
                    cam.Render();
                    cam.targetTexture = prevTarget;
                }

                RenderTexture.active = rt;
                Texture2D tex = new Texture2D(width, height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                tex.Apply();
                return tex;
            }
            finally
            {
                RenderTexture.active = null;
                RenderTexture.ReleaseTemporary(rt);
                try { RenderTexture.active = prevActive; } catch { }
            }
        }
        catch
        {
            return null;
        }
    }

    public static bool CheckUniformity(Texture2D tex, int width, int height)
    {
        try
        {
            int steps = 5;
            Color firstColor = tex.GetPixel(width / 2, height / 2);

            for (int xStep = 0; xStep < steps; xStep++)
            {
                int sampleX = Math.Min(width - 1, (width * xStep) / (steps - 1));
                for (int yStep = 0; yStep < steps; yStep++)
                {
                    int sampleY = Math.Min(height - 1, (height * yStep) / (steps - 1));
                    Color c = tex.GetPixel(sampleX, sampleY);

                    if (Math.Abs(c.r - firstColor.r) > 0.02f ||
                        Math.Abs(c.g - firstColor.g) > 0.02f ||
                        Math.Abs(c.b - firstColor.b) > 0.02f)
                    {
                        return false;
                    }
                }
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static byte[]? EncodeTextureToPng(Texture2D tex)
    {
        Type? imgConvType = CapabilitiesDetector.FindType("UnityEngine.ImageConversion");
        if (imgConvType != null)
        {
            MethodInfo? method = imgConvType.GetMethod("EncodeToPNG", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(Texture2D) }, null);
            if (method != null)
            {
                if (method.Invoke(null, new object[] { tex }) is byte[] bytes)
                {
                    return bytes;
                }
            }
        }

        MethodInfo? directMethod = typeof(Texture2D).GetMethod("EncodeToPNG", BindingFlags.Public | BindingFlags.Instance);
        if (directMethod != null)
        {
            if (directMethod.Invoke(tex, null) is byte[] bytes)
            {
                return bytes;
            }
        }

        return null;
    }
}
