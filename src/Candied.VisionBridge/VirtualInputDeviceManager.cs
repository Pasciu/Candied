using System;
using System.Collections;
using System.Reflection;
using Candied.VisionBridge.Protocol;
using HarmonyLib;
using UnityEngine;

namespace Candied.VisionBridge;

/// <summary>
/// Generic in-process virtual mouse and keyboard device manager for Unity Mono games.
/// Isolates the game from physical OS mouse interference by:
/// 1. Provisioning dedicated virtual InputSystem devices ("CandiedVirtualMouse", "CandiedVirtualKeyboard")
///    and disabling physical hardware Mouse devices in UnityEngine.InputSystem.
/// 2. Pinning the virtual mouse state across frames so physical OS mouse movements never overwrite it.
/// 3. Intercepting legacy UnityEngine.Input mouse queries and Application.isFocused via Harmony.
/// </summary>
public static class VirtualInputDeviceManager
{
    public const string VirtualMouseName = VirtualInputStateTracker.DefaultVirtualMouseName;
    public const string VirtualKeyboardName = VirtualInputStateTracker.DefaultVirtualKeyboardName;

    private static readonly VirtualInputStateTracker StateTracker = new();
    private static bool _harmonyPatched;

    // Cached reflection members for UnityEngine.InputSystem
    private static bool _reflectionInitialized;
    private static Type? _inputSystemType;
    private static Type? _inputDeviceType;
    private static Type? _mouseType;
    private static Type? _keyboardType;
    private static Type? _mouseStateType;
    private static Type? _inputUserType;
    private static Type? _pairingOptionsType;
    private static MethodInfo? _addDeviceMethod;
    private static MethodInfo? _disableDeviceMethod;
    private static MethodInfo? _enableDeviceMethod;
    private static MethodInfo? _makeCurrentMethod;
    private static MethodInfo? _queueMouseStateMethod;
    private static MethodInfo? _performPairingMethod;
    private static MethodInfo? _unpairDeviceMethod;
    private static PropertyInfo? _devicesProperty;
    private static PropertyInfo? _runInBackgroundProperty;
    private static PropertyInfo? _deviceNameProperty;
    private static PropertyInfo? _deviceEnabledProperty;
    private static PropertyInfo? _mouseCurrentProperty;
    private static PropertyInfo? _allUsersProperty;
    private static PropertyInfo? _userPairedDevicesProperty;
    private static FieldInfo? _posField;
    private static FieldInfo? _deltaField;
    private static FieldInfo? _btnField;
    private static FieldInfo? _clickCountField;

    private static object? _virtualMouseDevice;
    private static object? _virtualKeyboardDevice;

    public static bool IsIsolated => StateTracker.IsIsolated;
    public static Vector2 VirtualPosition => new Vector2(StateTracker.ScreenX, StateTracker.ScreenY);
    public static ushort CurrentButtonsMask => StateTracker.ButtonsMask;

    /// <summary>
    /// Installs generic Harmony postfixes on UnityEngine.Application.isFocused and UnityEngine.Input
    /// so background execution and legacy Input queries read exclusively from the virtual mouse state.
    /// </summary>
    public static void Initialize()
    {
        if (_harmonyPatched) return;
        _harmonyPatched = true;

        try
        {
            var harmony = new Harmony("com.candied.visionbridge.virtualinput");

            // 1. Patch Application.isFocused -> true when virtual input is active
            Type? appType = CapabilitiesDetector.FindType("UnityEngine.Application");
            if (appType != null)
            {
                PropertyInfo? isFocusedProp = appType.GetProperty("isFocused", BindingFlags.Public | BindingFlags.Static);
                MethodInfo? getter = isFocusedProp?.GetGetMethod();
                if (getter != null)
                {
                    MethodInfo postfix = typeof(VirtualInputDeviceManager).GetMethod(
                        nameof(ApplicationIsFocusedPostfix),
                        BindingFlags.NonPublic | BindingFlags.Static)!;
                    harmony.Patch(getter, postfix: new HarmonyMethod(postfix));
                }
            }

            // 2. Patch Legacy UnityEngine.Input mouse properties and methods if present
            Type? inputType = CapabilitiesDetector.FindType("UnityEngine.Input");
            if (inputType != null)
            {
                PropertyInfo? mousePosProp = inputType.GetProperty("mousePosition", BindingFlags.Public | BindingFlags.Static);
                MethodInfo? mousePosGetter = mousePosProp?.GetGetMethod();
                if (mousePosGetter != null)
                {
                    MethodInfo postfix = typeof(VirtualInputDeviceManager).GetMethod(
                        nameof(InputMousePositionPostfix),
                        BindingFlags.NonPublic | BindingFlags.Static)!;
                    harmony.Patch(mousePosGetter, postfix: new HarmonyMethod(postfix));
                }

                PropertyInfo? mousePresentProp = inputType.GetProperty("mousePresent", BindingFlags.Public | BindingFlags.Static);
                MethodInfo? mousePresentGetter = mousePresentProp?.GetGetMethod();
                if (mousePresentGetter != null)
                {
                    MethodInfo postfix = typeof(VirtualInputDeviceManager).GetMethod(
                        nameof(InputMousePresentPostfix),
                        BindingFlags.NonPublic | BindingFlags.Static)!;
                    harmony.Patch(mousePresentGetter, postfix: new HarmonyMethod(postfix));
                }

                MethodInfo? getMouseBtn = inputType.GetMethod("GetMouseButton", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(int) }, null);
                if (getMouseBtn != null)
                {
                    MethodInfo postfix = typeof(VirtualInputDeviceManager).GetMethod(
                        nameof(InputGetMouseButtonPostfix),
                        BindingFlags.NonPublic | BindingFlags.Static)!;
                    harmony.Patch(getMouseBtn, postfix: new HarmonyMethod(postfix));
                }

                MethodInfo? getMouseBtnDown = inputType.GetMethod("GetMouseButtonDown", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(int) }, null);
                if (getMouseBtnDown != null)
                {
                    MethodInfo postfix = typeof(VirtualInputDeviceManager).GetMethod(
                        nameof(InputGetMouseButtonDownPostfix),
                        BindingFlags.NonPublic | BindingFlags.Static)!;
                    harmony.Patch(getMouseBtnDown, postfix: new HarmonyMethod(postfix));
                }

                MethodInfo? getMouseBtnUp = inputType.GetMethod("GetMouseButtonUp", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(int) }, null);
                if (getMouseBtnUp != null)
                {
                    MethodInfo postfix = typeof(VirtualInputDeviceManager).GetMethod(
                        nameof(InputGetMouseButtonUpPostfix),
                        BindingFlags.NonPublic | BindingFlags.Static)!;
                    harmony.Patch(getMouseBtnUp, postfix: new HarmonyMethod(postfix));
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[Candied.VisionBridge] Impossibile applicare patch Harmony per VirtualInputDeviceManager: " + ex.Message);
        }
    }

    /// <summary>
    /// Updates the virtual mouse state and queues a state event onto the isolated virtual InputSystem mouse.
    /// </summary>
    public static bool SetMouseState(float screenX, float screenY, ushort buttonsMask, ushort clickCount, int currentFrame = -1)
    {
        int frame = currentFrame >= 0 ? currentFrame : SafeGetFrameCount();
        StateTracker.UpdateMouseState(screenX, screenY, buttonsMask, clickCount, frame);

        object? virtualMouse = EnsureVirtualMouseIsolated();
        if (virtualMouse == null || _queueMouseStateMethod == null || _mouseStateType == null)
        {
            return false;
        }

        QueueStateOnDevice(virtualMouse, new Vector2(StateTracker.ScreenX, StateTracker.ScreenY), StateTracker.ButtonsMask, StateTracker.ClickCount);
        return true;
    }

    /// <summary>
    /// Releases virtual input isolation and restores physical OS mouse devices and InputUser pairings
    /// so the human user can manually interact with the embedded game window.
    /// </summary>
    public static bool ReleaseIsolation()
    {
        float lastX = StateTracker.ScreenX;
        float lastY = StateTracker.ScreenY;
        StateTracker.SetIsolated(false);

        EnsureReflectionInitialized();
        if (_inputSystemType == null || _mouseType == null || _inputDeviceType == null)
        {
            return true;
        }

        try
        {
            if (_virtualMouseDevice != null)
            {
                QueueStateOnDevice(_virtualMouseDevice, new Vector2(lastX, lastY), 0, 0);
                UnpairDeviceFromAllUsers(_virtualMouseDevice);
            }

            if (_virtualKeyboardDevice != null)
            {
                UnpairDeviceFromAllUsers(_virtualKeyboardDevice);
            }

            object? firstPhysicalMouse = null;
            if (_devicesProperty != null)
            {
                object? devicesCollection = _devicesProperty.GetValue(null, null);
                if (devicesCollection is IEnumerable enumerable)
                {
                    foreach (object? dev in enumerable)
                    {
                        if (dev == null) continue;
                        string devName = (_deviceNameProperty?.GetValue(dev, null) as string) ?? string.Empty;

                        if (_mouseType.IsInstanceOfType(dev) && !string.Equals(devName, VirtualMouseName, StringComparison.OrdinalIgnoreCase))
                        {
                            bool isEnabled = _deviceEnabledProperty != null && (bool)(_deviceEnabledProperty.GetValue(dev, null) ?? false);
                            if (!isEnabled && _enableDeviceMethod != null)
                            {
                                _enableDeviceMethod.Invoke(null, new object[] { dev });
                            }

                            PairDeviceWithAllUsers(dev);
                            firstPhysicalMouse ??= dev;
                        }
                    }
                }
            }

            if (firstPhysicalMouse != null && _makeCurrentMethod != null)
            {
                _makeCurrentMethod.Invoke(firstPhysicalMouse, null);
            }

            return true;
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[Candied.VisionBridge] Errore in ReleaseIsolation: " + ex.Message);
            return false;
        }
    }

    /// <summary>
    /// Called every frame from VisionBridgePlugin.Update() to ensure physical hardware mice remain
    /// disabled and the virtual mouse stays current and pinned at the target position.
    /// </summary>
    public static void OnFrameUpdate()
    {
        if (!StateTracker.IsIsolated) return;

        try
        {
            object? virtualMouse = EnsureVirtualMouseIsolated();
            if (virtualMouse != null && _queueMouseStateMethod != null && _mouseStateType != null)
            {
                QueueStateOnDevice(virtualMouse, new Vector2(StateTracker.ScreenX, StateTracker.ScreenY), StateTracker.ButtonsMask, StateTracker.ClickCount);
            }
        }
        catch
        {
            // Ignore transient frame errors
        }
    }

    public static object? EnsureVirtualMouseIsolated()
    {
        EnsureReflectionInitialized();
        if (_inputSystemType == null || _mouseType == null || _inputDeviceType == null)
        {
            return null;
        }

        try
        {
            // Ensure InputSystem processes events even when window is in background
            if (_runInBackgroundProperty != null && _runInBackgroundProperty.CanWrite)
            {
                _runInBackgroundProperty.SetValue(null, true, null);
            }

            // 1. Create CandiedVirtualMouse & CandiedVirtualKeyboard first if not yet present
            if (_devicesProperty != null)
            {
                object? devicesCollection = _devicesProperty.GetValue(null, null);
                if (devicesCollection is IEnumerable scanEnum)
                {
                    foreach (object? dev in scanEnum)
                    {
                        if (dev == null) continue;
                        string devName = (_deviceNameProperty?.GetValue(dev, null) as string) ?? string.Empty;
                        if (_mouseType.IsInstanceOfType(dev) && string.Equals(devName, VirtualMouseName, StringComparison.OrdinalIgnoreCase))
                        {
                            _virtualMouseDevice = dev;
                        }
                        else if (_keyboardType != null && _keyboardType.IsInstanceOfType(dev) && string.Equals(devName, VirtualKeyboardName, StringComparison.OrdinalIgnoreCase))
                        {
                            _virtualKeyboardDevice = dev;
                        }
                    }
                }
            }

            if (_virtualMouseDevice == null && _addDeviceMethod != null)
            {
                _virtualMouseDevice = _addDeviceMethod.Invoke(null, new object?[] { "Mouse", VirtualMouseName, null });
            }

            if (_virtualKeyboardDevice == null && _addDeviceMethod != null && _keyboardType != null)
            {
                _virtualKeyboardDevice = _addDeviceMethod.Invoke(null, new object?[] { "Keyboard", VirtualKeyboardName, null });
            }

            // 2. Pair CandiedVirtualMouse (and CandiedVirtualKeyboard) with all active InputUsers (PlayerInput)
            //    and unpair + disable physical OS Mouse devices so physical mouse movements cannot interfere
            if (_devicesProperty != null)
            {
                object? devicesCollection = _devicesProperty.GetValue(null, null);
                if (devicesCollection is IEnumerable enumerable)
                {
                    foreach (object? dev in enumerable)
                    {
                        if (dev == null) continue;
                        string devName = (_deviceNameProperty?.GetValue(dev, null) as string) ?? string.Empty;

                        if (_mouseType.IsInstanceOfType(dev) && !string.Equals(devName, VirtualMouseName, StringComparison.OrdinalIgnoreCase))
                        {
                            UnpairDeviceFromAllUsers(dev);

                            bool isEnabled = _deviceEnabledProperty != null && (bool)(_deviceEnabledProperty.GetValue(dev, null) ?? false);
                            if (isEnabled && _disableDeviceMethod != null)
                            {
                                _disableDeviceMethod.Invoke(null, new object[] { dev, false });
                            }
                        }
                    }
                }
            }

            // 3. Ensure CandiedVirtualMouse is enabled, paired with all InputUsers, and set as Mouse.current
            if (_virtualMouseDevice != null)
            {
                bool isEnabled = _deviceEnabledProperty == null || (bool)(_deviceEnabledProperty.GetValue(_virtualMouseDevice, null) ?? true);
                if (!isEnabled && _enableDeviceMethod != null)
                {
                    _enableDeviceMethod.Invoke(null, new object[] { _virtualMouseDevice });
                }

                PairDeviceWithAllUsers(_virtualMouseDevice);
                if (_virtualKeyboardDevice != null)
                {
                    PairDeviceWithAllUsers(_virtualKeyboardDevice);
                }

                object? currentMouse = _mouseCurrentProperty?.GetValue(null, null);
                if (!ReferenceEquals(currentMouse, _virtualMouseDevice) && _makeCurrentMethod != null)
                {
                    _makeCurrentMethod.Invoke(_virtualMouseDevice, null);
                }
            }

            return _virtualMouseDevice;
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[Candied.VisionBridge] Errore in EnsureVirtualMouseIsolated: " + ex.Message);
            return _mouseCurrentProperty?.GetValue(null, null);
        }
    }

    private static void QueueStateOnDevice(object mouseDevice, Vector2 screenPos, ushort buttonsMask, ushort clickCount)
    {
        if (_mouseStateType == null || _queueMouseStateMethod == null) return;

        object state = Activator.CreateInstance(_mouseStateType)!;
        _posField?.SetValue(state, screenPos);
        _deltaField?.SetValue(state, Vector2.zero);
        _btnField?.SetValue(state, buttonsMask);
        _clickCountField?.SetValue(state, clickCount);

        _queueMouseStateMethod.Invoke(null, new object[] { mouseDevice, state, -1.0 });
    }

    private static void PairDeviceWithAllUsers(object device)
    {
        if (_inputUserType == null || _allUsersProperty == null || _performPairingMethod == null || _pairingOptionsType == null)
        {
            return;
        }

        try
        {
            object? allUsers = _allUsersProperty.GetValue(null, null);
            if (allUsers is IEnumerable usersEnum)
            {
                object noneOption = Enum.ToObject(_pairingOptionsType, 0);
                foreach (object? user in usersEnum)
                {
                    if (user == null) continue;
                    bool alreadyPaired = false;
                    if (_userPairedDevicesProperty != null)
                    {
                        object? paired = _userPairedDevicesProperty.GetValue(user, null);
                        if (paired is IEnumerable pairedEnum)
                        {
                            foreach (object? p in pairedEnum)
                            {
                                if (ReferenceEquals(p, device))
                                {
                                    alreadyPaired = true;
                                    break;
                                }
                            }
                        }
                    }

                    if (!alreadyPaired)
                    {
                        _performPairingMethod.Invoke(null, new[] { device, user, noneOption });
                    }
                }
            }
        }
        catch
        {
            // Ignore InputUser pairing errors on games not using InputUser
        }
    }

    private static void UnpairDeviceFromAllUsers(object device)
    {
        if (_inputUserType == null || _allUsersProperty == null || _unpairDeviceMethod == null)
        {
            return;
        }

        try
        {
            object? allUsers = _allUsersProperty.GetValue(null, null);
            if (allUsers is IEnumerable usersEnum)
            {
                foreach (object? user in usersEnum)
                {
                    if (user == null) continue;
                    bool isPaired = false;
                    if (_userPairedDevicesProperty != null)
                    {
                        object? paired = _userPairedDevicesProperty.GetValue(user, null);
                        if (paired is IEnumerable pairedEnum)
                        {
                            foreach (object? p in pairedEnum)
                            {
                                if (ReferenceEquals(p, device))
                                {
                                    isPaired = true;
                                    break;
                                }
                            }
                        }
                    }

                    if (isPaired)
                    {
                        _unpairDeviceMethod.Invoke(user, new[] { device });
                    }
                }
            }
        }
        catch
        {
            // Ignore InputUser unpairing errors
        }
    }

    private static void EnsureReflectionInitialized()
    {
        if (_reflectionInitialized) return;
        _reflectionInitialized = true;

        _inputSystemType = CapabilitiesDetector.FindType("UnityEngine.InputSystem.InputSystem");
        _inputDeviceType = CapabilitiesDetector.FindType("UnityEngine.InputSystem.InputDevice");
        _mouseType = CapabilitiesDetector.FindType("UnityEngine.InputSystem.Mouse");
        _keyboardType = CapabilitiesDetector.FindType("UnityEngine.InputSystem.Keyboard");
        _mouseStateType = CapabilitiesDetector.FindType("UnityEngine.InputSystem.LowLevel.MouseState");
        _inputUserType = CapabilitiesDetector.FindType("UnityEngine.InputSystem.Users.InputUser");
        _pairingOptionsType = CapabilitiesDetector.FindType("UnityEngine.InputSystem.Users.InputUserPairingOptions");

        if (_inputSystemType != null)
        {
            _addDeviceMethod = _inputSystemType.GetMethod(
                "AddDevice",
                BindingFlags.Public | BindingFlags.Static,
                null,
                new[] { typeof(string), typeof(string), typeof(string) },
                null);

            _devicesProperty = _inputSystemType.GetProperty("devices", BindingFlags.Public | BindingFlags.Static);
            _runInBackgroundProperty = _inputSystemType.GetProperty("runInBackground", BindingFlags.Public | BindingFlags.Static);

            if (_inputDeviceType != null)
            {
                _disableDeviceMethod = _inputSystemType.GetMethod(
                    "DisableDevice",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new[] { _inputDeviceType, typeof(bool) },
                    null);

                _enableDeviceMethod = _inputSystemType.GetMethod(
                    "EnableDevice",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new[] { _inputDeviceType },
                    null);
            }

            if (_mouseStateType != null)
            {
                MethodInfo[] methods = _inputSystemType.GetMethods(BindingFlags.Public | BindingFlags.Static);
                for (int i = 0; i < methods.Length; i++)
                {
                    MethodInfo m = methods[i];
                    if (m.Name == "QueueStateEvent" && m.IsGenericMethod && m.GetParameters().Length == 3)
                    {
                        _queueMouseStateMethod = m.MakeGenericMethod(new[] { _mouseStateType });
                        break;
                    }
                }
            }
        }

        if (_inputUserType != null && _inputDeviceType != null)
        {
            _allUsersProperty = _inputUserType.GetProperty("all", BindingFlags.Public | BindingFlags.Static);
            _userPairedDevicesProperty = _inputUserType.GetProperty("pairedDevices", BindingFlags.Public | BindingFlags.Instance);
            _unpairDeviceMethod = _inputUserType.GetMethod(
                "UnpairDevice",
                BindingFlags.Public | BindingFlags.Instance,
                null,
                new[] { _inputDeviceType },
                null);

            if (_pairingOptionsType != null)
            {
                _performPairingMethod = _inputUserType.GetMethod(
                    "PerformPairingWithDevice",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new[] { _inputDeviceType, _inputUserType, _pairingOptionsType },
                    null);
            }
        }

        if (_inputDeviceType != null)
        {
            _deviceNameProperty = _inputDeviceType.GetProperty("name", BindingFlags.Public | BindingFlags.Instance);
            _deviceEnabledProperty = _inputDeviceType.GetProperty("enabled", BindingFlags.Public | BindingFlags.Instance);
            _makeCurrentMethod = _inputDeviceType.GetMethod("MakeCurrent", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
        }

        if (_mouseType != null)
        {
            _mouseCurrentProperty = _mouseType.GetProperty("current", BindingFlags.Public | BindingFlags.Static);
        }

        if (_mouseStateType != null)
        {
            _posField = _mouseStateType.GetField("position", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            _deltaField = _mouseStateType.GetField("delta", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            _btnField = _mouseStateType.GetField("buttons", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            _clickCountField = _mouseStateType.GetField("clickCount", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        }
    }

    private static int SafeGetFrameCount()
    {
        try
        {
            return Time.frameCount;
        }
        catch
        {
            return 0;
        }
    }

    // Harmony Postfixes
    private static void ApplicationIsFocusedPostfix(ref bool __result)
    {
        if (StateTracker.IsIsolated)
        {
            __result = true;
        }
    }

    private static void InputMousePositionPostfix(ref Vector3 __result)
    {
        if (StateTracker.IsIsolated)
        {
            __result = new Vector3(StateTracker.ScreenX, StateTracker.ScreenY, 0f);
        }
    }

    private static void InputMousePresentPostfix(ref bool __result)
    {
        if (StateTracker.IsIsolated)
        {
            __result = true;
        }
    }

    private static void InputGetMouseButtonPostfix(int button, ref bool __result)
    {
        if (StateTracker.IsIsolated && button >= 0 && button <= 2)
        {
            __result = StateTracker.IsButtonHeld(button);
        }
    }

    private static void InputGetMouseButtonDownPostfix(int button, ref bool __result)
    {
        if (StateTracker.IsIsolated && button >= 0 && button <= 2)
        {
            __result = StateTracker.IsButtonDownOnFrame(button, SafeGetFrameCount());
        }
    }

    private static void InputGetMouseButtonUpPostfix(int button, ref bool __result)
    {
        if (StateTracker.IsIsolated && button >= 0 && button <= 2)
        {
            __result = StateTracker.IsButtonUpOnFrame(button, SafeGetFrameCount());
        }
    }
}
