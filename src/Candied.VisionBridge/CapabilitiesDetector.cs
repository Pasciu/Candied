using System;
using System.Reflection;
using Candied.VisionBridge.Protocol;

namespace Candied.VisionBridge;

public static class CapabilitiesDetector
{
    private static VisionCapabilitiesDto? _cachedCapabilities;
    public static string ActiveInputClickMethod { get; set; } = string.Empty;

    public static VisionCapabilitiesDto Detect()
    {
        if (_cachedCapabilities != null)
        {
            _cachedCapabilities.ScreenshotMethod = ScreenshotCapturer.ActiveMethod;
            _cachedCapabilities.ScreenshotLimitations = ScreenshotCapturer.ActiveLimitations;
            _cachedCapabilities.InputClickMethod = ActiveInputClickMethod;
            _cachedCapabilities.InputClickStatus = BuildInputClickStatus(_cachedCapabilities.InputSystem, _cachedCapabilities.LegacyInput);
            return _cachedCapabilities;
        }

        bool hasInputSystem = HasType("UnityEngine.InputSystem.InputSystem") || HasType("UnityEngine.InputSystem.Mouse");
        bool hasLegacy = CheckLegacyInput();

        string method = ActiveInputClickMethod;
        if (string.IsNullOrEmpty(method))
        {
            method = hasInputSystem ? "input_system" : (hasLegacy ? "legacy" : "postmessage");
            ActiveInputClickMethod = method;
        }

        var caps = new VisionCapabilitiesDto
        {
            Ugui = HasType("UnityEngine.UI.GraphicRaycaster") && HasType("UnityEngine.UI.Selectable"),
            EventSystem = HasType("UnityEngine.EventSystems.EventSystem"),
            UiToolkit = HasType("UnityEngine.UIElements.UIDocument") || HasType("UnityEngine.UIElements.VisualElement"),
            LegacyInput = hasLegacy,
            InputSystem = hasInputSystem,
            Screenshot = true,
            ScreenshotMethod = ScreenshotCapturer.ActiveMethod,
            ScreenshotLimitations = ScreenshotCapturer.ActiveLimitations,
            InputClickMethod = method,
            InputClickStatus = BuildInputClickStatus(hasInputSystem, hasLegacy),
            UntestedMethods = "postmessage è implementato ma mai provato dal vivo"
        };

        _cachedCapabilities = caps;
        return caps;
    }

    private static string BuildInputClickStatus(bool hasInputSystem, bool hasLegacy)
    {
        string modeTag = VirtualInputDeviceManager.IsIsolated ? "[ISOLATED]" : "[PHYSICAL_ACTIVE]";
        if (hasInputSystem)
        {
            return $"input_system {modeTag}: isolato via CandiedVirtualMouse (DisableDevice su mouse fisico + pre-hover 4 frame + QueueStateEvent)";
        }
        if (hasLegacy)
        {
            return $"legacy {modeTag}: intercettazione virtuale UnityEngine.Input + EventSystem";
        }
        return $"postmessage {modeTag}: implementato ma mai provato dal vivo";
    }

    public static Type? FindType(string fullTypeName)
    {
        if (string.IsNullOrEmpty(fullTypeName)) return null;

        Type? direct = Type.GetType(fullTypeName);
        if (direct != null) return direct;

        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                Type? t = asm.GetType(fullTypeName, false, false);
                if (t != null) return t;
            }
            catch
            {
                // Ignore ReflectionTypeLoadException or assembly probe errors
            }
        }

        return null;
    }

    private static bool HasType(string fullTypeName)
    {
        return FindType(fullTypeName) != null;
    }

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
            // Ignore
        }
        return false;
    }
}
