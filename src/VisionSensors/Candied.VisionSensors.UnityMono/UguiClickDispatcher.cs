using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Candied.Vision.Protocol;
using UnityEngine;

namespace Candied.VisionSensors.UnityMono;

public static class UguiClickDispatcher
{
    public static VisionInputResponse Click(VisionInputRequest request)
    {
        if (request == null)
        {
            return new VisionInputResponse
            {
                Success = false,
                Channel = VisionConstants.ChannelPointer,
                Action = "click",
                Message = "Null input request."
            };
        }

        if (!string.IsNullOrEmpty(request.TargetElementId))
        {
            return ClickById(request.TargetElementId!);
        }

        if (request.X.HasValue && request.Y.HasValue)
        {
            return ClickByCoordinate(request.X.Value, request.Y.Value);
        }

        return new VisionInputResponse
        {
            Success = false,
            Channel = VisionConstants.ChannelPointer,
            Action = request.Action,
            Message = "Pointer input must specify either 'targetElementId' or 'x' and 'y' coordinates."
        };
    }

    private static VisionInputResponse ClickById(string targetId)
    {
        Type? selectableType = CapabilitiesDetector.FindType("UnityEngine.UI.Selectable");
        if (selectableType != null)
        {
            UnityEngine.Object[] selectables = UnityEngine.Object.FindObjectsOfType(selectableType);
            foreach (var obj in selectables)
            {
                if (obj is not Component comp || comp == null) continue;
                string id = UguiProvider.BuildHierarchyPath(comp.transform);
                if (string.Equals(id, targetId, StringComparison.OrdinalIgnoreCase))
                {
                    var go = comp.gameObject;
                    if (!go.activeInHierarchy)
                    {
                        return new VisionInputResponse
                        {
                            Success = false,
                            Channel = VisionConstants.ChannelPointer,
                            Action = "click",
                            HitElementId = id,
                            HitElementType = comp.GetType().Name,
                            Message = "Element found but inactive in hierarchy."
                        };
                    }

                    bool dispatched = DispatchClickToGameObject(go);
                    return new VisionInputResponse
                    {
                        Success = dispatched,
                        Channel = VisionConstants.ChannelPointer,
                        Action = "click",
                        HitElementId = id,
                        HitElementType = comp.GetType().Name,
                        Message = dispatched ? "Click dispatched to UI element." : "Element found but click dispatch failed."
                    };
                }
            }
        }

        try
        {
            Transform[] allTransforms = UnityEngine.Object.FindObjectsOfType<Transform>();
            foreach (var t in allTransforms)
            {
                if (t == null) continue;
                string id = UguiProvider.BuildHierarchyPath(t);
                if (string.Equals(id, targetId, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(t.name, targetId, StringComparison.OrdinalIgnoreCase))
                {
                    var go = t.gameObject;
                    if (!go.activeInHierarchy)
                    {
                        return new VisionInputResponse
                        {
                            Success = false,
                            Channel = VisionConstants.ChannelPointer,
                            Action = "click",
                            HitElementId = id,
                            HitElementType = go.name,
                            Message = "Transform found but inactive in hierarchy."
                        };
                    }

                    bool dispatched = DispatchClickToGameObject(go);
                    return new VisionInputResponse
                    {
                        Success = dispatched,
                        Channel = VisionConstants.ChannelPointer,
                        Action = "click",
                        HitElementId = id,
                        HitElementType = go.name,
                        Message = dispatched ? "Click dispatched to transform." : "Transform found but dispatch failed."
                    };
                }
            }
        }
        catch
        {
        }

        return new VisionInputResponse
        {
            Success = false,
            Channel = VisionConstants.ChannelPointer,
            Action = "click",
            HitElementId = targetId,
            Message = "No UI element found with id: " + targetId
        };
    }

    private static VisionInputResponse ClickByCoordinate(float imageX, float imageY)
    {
        int screenHeight = Screen.height;
        var screenPoint = CoordinateConverter.ImageToBottomLeftScreenPoint(imageX, imageY, screenHeight);
        float screenX = screenPoint.X;
        float screenY = screenPoint.Y;

        Type? eventSystemType = CapabilitiesDetector.FindType("UnityEngine.EventSystems.EventSystem");
        if (eventSystemType != null)
        {
            PropertyInfo? currentProp = eventSystemType.GetProperty("current", BindingFlags.Public | BindingFlags.Static);
            object? currentEventSystem = currentProp?.GetValue(null, null);
            if (currentEventSystem != null)
            {
                Type? pointerDataType = CapabilitiesDetector.FindType("UnityEngine.EventSystems.PointerEventData");
                Type? raycastResultType = CapabilitiesDetector.FindType("UnityEngine.EventSystems.RaycastResult");

                if (pointerDataType != null && raycastResultType != null)
                {
                    try
                    {
                        object pointerData = Activator.CreateInstance(pointerDataType, new object[] { currentEventSystem })!;
                        pointerDataType.GetProperty("position")?.SetValue(pointerData, new Vector2(screenX, screenY), null);

                        Type listType = typeof(List<>).MakeGenericType(raycastResultType);
                        IList raycastList = (IList)Activator.CreateInstance(listType)!;

                        MethodInfo? raycastMethod = eventSystemType.GetMethod("RaycastAll", new Type[] { pointerDataType, listType });
                        raycastMethod?.Invoke(currentEventSystem, new object[] { pointerData, raycastList });

                        if (raycastList.Count > 0)
                        {
                            object firstHit = raycastList[0]!;
                            GameObject? hitGo = raycastResultType.GetProperty("gameObject")?.GetValue(firstHit, null) as GameObject;
                            if (hitGo != null)
                            {
                                string hitId = UguiProvider.BuildHierarchyPath(hitGo.transform);
                                bool dispatched = DispatchClickToGameObject(hitGo);
                                return new VisionInputResponse
                                {
                                    Success = dispatched,
                                    Channel = VisionConstants.ChannelPointer,
                                    Action = "click",
                                    HitElementId = hitId,
                                    HitElementType = hitGo.name,
                                    HitPointX = imageX,
                                    HitPointY = imageY,
                                    Message = dispatched ? "Element hit and clicked." : "Element hit but click dispatch failed."
                                };
                            }
                        }
                    }
                    catch
                    {
                    }
                }
            }
        }

        try
        {
            Type? physics2DType = CapabilitiesDetector.FindType("UnityEngine.Physics2D");
            if (physics2DType != null)
            {
                Camera? cam = Camera.main ?? (Camera.allCameras.Length > 0 ? Camera.allCameras[0] : null);
                if (cam != null)
                {
                    Vector3 worldPoint = cam.ScreenToWorldPoint(new Vector3(screenX, screenY, cam.nearClipPlane));
                    Vector2 point2D = new Vector2(worldPoint.x, worldPoint.y);
                    MethodInfo? overlapPointMethod = physics2DType.GetMethod("OverlapPoint", new Type[] { typeof(Vector2) });
                    if (overlapPointMethod != null)
                    {
                        Component? hitCollider = overlapPointMethod.Invoke(null, new object[] { point2D }) as Component;
                        if (hitCollider != null && hitCollider.gameObject != null)
                        {
                            var hitGo = hitCollider.gameObject;
                            string hitId = UguiProvider.BuildHierarchyPath(hitGo.transform);
                            bool dispatched = DispatchClickToGameObject(hitGo);
                            return new VisionInputResponse
                            {
                                Success = dispatched,
                                Channel = VisionConstants.ChannelPointer,
                                Action = "click",
                                HitElementId = hitId,
                                HitElementType = hitGo.name,
                                HitPointX = imageX,
                                HitPointY = imageY,
                                Message = dispatched ? "Collider2D hit and clicked." : "Collider2D hit but dispatch failed."
                            };
                        }
                    }
                }
            }
        }
        catch
        {
        }

        return new VisionInputResponse
        {
            Success = false,
            Channel = VisionConstants.ChannelPointer,
            Action = "click",
            HitPointX = imageX,
            HitPointY = imageY,
            Message = "No UI element or Collider2D hit at (" + imageX + ", " + imageY + ")."
        };
    }

    private static bool DispatchClickToGameObject(GameObject go)
    {
        try
        {
            Type? execEventsType = CapabilitiesDetector.FindType("UnityEngine.EventSystems.ExecuteEvents");
            Type? eventSystemType = CapabilitiesDetector.FindType("UnityEngine.EventSystems.EventSystem");
            Type? pointerDataType = CapabilitiesDetector.FindType("UnityEngine.EventSystems.PointerEventData");

            if (execEventsType != null && eventSystemType != null && pointerDataType != null)
            {
                object? currentEventSystem = eventSystemType.GetProperty("current", BindingFlags.Public | BindingFlags.Static)?.GetValue(null, null);
                object? pointerData = Activator.CreateInstance(pointerDataType, new object?[] { currentEventSystem });
                object? handler = execEventsType.GetProperty("pointerClickHandler", BindingFlags.Public | BindingFlags.Static)?.GetValue(null, null);

                if (pointerData != null && handler != null)
                {
                    foreach (var m in execEventsType.GetMethods(BindingFlags.Public | BindingFlags.Static))
                    {
                        if (m.Name == "Execute" && m.IsGenericMethod && m.GetParameters().Length == 3)
                        {
                            Type? handlerInterfaceType = CapabilitiesDetector.FindType("UnityEngine.EventSystems.IPointerClickHandler");
                            if (handlerInterfaceType != null)
                            {
                                object? res = m.MakeGenericMethod(handlerInterfaceType).Invoke(null, new object[] { go, pointerData, handler });
                                if (res is bool executed && executed) return true;
                            }
                        }
                    }
                }
            }
        }
        catch
        {
        }

        try
        {
            foreach (var comp in go.GetComponents<Component>())
            {
                if (comp == null) continue;
                object? onClickVal = comp.GetType().GetProperty("onClick")?.GetValue(comp, null);
                MethodInfo? invokeMethod = onClickVal?.GetType().GetMethod("Invoke", Type.EmptyTypes);
                if (invokeMethod != null)
                {
                    invokeMethod.Invoke(onClickVal, null);
                    return true;
                }
            }
        }
        catch
        {
        }

        try
        {
            foreach (var comp in go.GetComponents<Component>())
            {
                if (comp == null) continue;
                Type t = comp.GetType();
                MethodInfo? clickMethod = t.GetMethod("OnClick", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, Type.EmptyTypes, null)
                    ?? t.GetMethod("Click", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, Type.EmptyTypes, null)
                    ?? t.GetMethod("OnPrimaryCursorClick", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, Type.EmptyTypes, null)
                    ?? t.GetMethod("OnButtonReleasedPointerInside", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, Type.EmptyTypes, null);
                if (clickMethod != null)
                {
                    clickMethod.Invoke(comp, null);
                    return true;
                }
            }
        }
        catch
        {
        }

        try
        {
            go.SendMessage("OnMouseDown", SendMessageOptions.DontRequireReceiver);
            go.SendMessage("OnMouseUpAsButton", SendMessageOptions.DontRequireReceiver);
            go.SendMessage("OnClick", SendMessageOptions.DontRequireReceiver);
            return true;
        }
        catch
        {
        }

        return false;
    }
}
