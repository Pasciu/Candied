using System;
using System.Collections.Generic;
using System.Reflection;
using Candied.Vision.Protocol;

namespace Candied.VisionSensors.UnityMono;

public static class CapabilitiesDetector
{
    public static string ActivePointerMethod { get; set; } = string.Empty;

    public static bool HasInputSystem => HasType("UnityEngine.InputSystem.InputSystem") || HasType("UnityEngine.InputSystem.Mouse");
    public static bool HasLegacyInput => CheckLegacyInput();

    public static VisionCapabilitiesDto Detect()
    {
        bool hasInputSystem = HasInputSystem;
        bool hasLegacy = HasLegacyInput;
        bool hasUgui = HasType("UnityEngine.UI.GraphicRaycaster") && HasType("UnityEngine.UI.Selectable");
        bool hasUiToolkit = HasType("UnityEngine.UIElements.UIDocument") || HasType("UnityEngine.UIElements.VisualElement");

        string pointerMethod = ActivePointerMethod;
        if (string.IsNullOrEmpty(pointerMethod))
        {
            pointerMethod = hasInputSystem ? "input_system" : (hasLegacy ? "legacy" : "postmessage");
            ActivePointerMethod = pointerMethod;
        }

        var caps = new List<string>
        {
            VisionConstants.CapScreenshot,
            VisionConstants.CapSceneGraph,
            VisionConstants.CapRuntimeAssets,
            VisionConstants.CapWaitIdle,
            VisionConstants.CapPointer,
            VisionConstants.CapKeyboard,
            VisionConstants.CapGamepad
        };

        if (hasUgui || hasUiToolkit)
        {
            caps.Add(VisionConstants.CapUiElements);
        }

        if (hasInputSystem || hasLegacy)
        {
            caps.Add(VisionConstants.CapPointerIsolated);
        }

        var metadata = new Dictionary<string, string>
        {
            ["ugui"] = hasUgui ? "true" : "false",
            ["uiToolkit"] = hasUiToolkit ? "true" : "false",
            ["inputSystem"] = hasInputSystem ? "true" : "false",
            ["legacyInput"] = hasLegacy ? "true" : "false",
            ["isolationMode"] = VirtualInputDeviceManager.IsIsolated ? "isolated" : "physical_active"
        };

        return new VisionCapabilitiesDto
        {
            Capabilities = caps,
            ScreenshotMethod = ScreenshotCapturer.ActiveMethod,
            ScreenshotLimitations = ScreenshotCapturer.ActiveLimitations,
            PointerMethod = pointerMethod,
            Metadata = metadata
        };
    }

    public static Type? FindType(string fullTypeName)
    {
        if (string.IsNullOrEmpty(fullTypeName))
        {
            return null;
        }

        Type? direct = Type.GetType(fullTypeName);
        if (direct != null)
        {
            return direct;
        }

        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                Type? t = asm.GetType(fullTypeName, false, false);
                if (t != null)
                {
                    return t;
                }
            }
            catch
            {
            }
        }

        return null;
    }

    private static bool HasType(string fullTypeName) => FindType(fullTypeName) != null;

    private static bool CheckLegacyInput()
    {
        try
        {
            Type? inputType = FindType("UnityEngine.Input");
            if (inputType != null)
            {
                PropertyInfo? mouseProp = inputType.GetProperty("mousePosition", BindingFlags.Public | BindingFlags.Static);
                return mouseProp != null;
            }
        }
        catch
        {
        }

        return false;
    }
}
