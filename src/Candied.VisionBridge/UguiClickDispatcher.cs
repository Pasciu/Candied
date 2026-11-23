using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Candied.VisionBridge.Protocol;
using UnityEngine;

namespace Candied.VisionBridge;

public static class UguiClickDispatcher
{
    public static VisionClickResponse Click(VisionClickRequest request)
    {
        if (request == null)
        {
            return new VisionClickResponse
            {
                Success = false,
                Message = "Richiesta di click nulla."
            };
        }

        // Mode 1: Click by ID
        if (!string.IsNullOrEmpty(request.Id))
        {
            return ClickById(request.Id!);
        }

        // Mode 2: Click by Coordinate
        if (request.X.HasValue && request.Y.HasValue)
        {
            return ClickByCoordinate(request.X.Value, request.Y.Value, request.Button);
        }

        return new VisionClickResponse
        {
            Success = false,
            Message = "La richiesta di click deve specificare 'id' oppure coordinate 'x' e 'y'."
        };
    }

    private static VisionClickResponse ClickById(string targetId)
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
                        return new VisionClickResponse
                        {
                            Success = false,
                            HitElementId = id,
                            HitElementType = comp.GetType().Name,
                            Message = "Elemento trovato ma non attivo nella gerarchia (disabilitato)."
                        };
                    }

                    // Trigger click
                    bool dispatched = DispatchClickToGameObject(go);
                    return new VisionClickResponse
                    {
                        Success = dispatched,
                        HitElementId = id,
                        HitElementType = comp.GetType().Name,
                        Message = dispatched ? "Click inviato con successo all'elemento." : "Elemento trovato ma dispatch del click fallito."
                    };
                }
            }
        }

        // Fallback: search transforms by hierarchy path or name (covers 2D Collider / custom button objects)
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
                        return new VisionClickResponse
                        {
                            Success = false,
                            HitElementId = id,
                            HitElementType = go.name,
                            Message = "Elemento trovato ma non attivo nella gerarchia (disabilitato)."
                        };
                    }

                    bool dispatched = DispatchClickToGameObject(go);
                    return new VisionClickResponse
                    {
                        Success = dispatched,
                        HitElementId = id,
                        HitElementType = go.name,
                        Message = dispatched ? "Click inviato con successo all'elemento." : "Elemento trovato ma dispatch del click fallito."
                    };
                }
            }
        }
        catch { }

        return new VisionClickResponse
        {
            Success = false,
            HitElementId = targetId,
            Message = "Nessun elemento UI trovato con id: " + targetId
        };
    }

    private static VisionClickResponse ClickByCoordinate(float imageX, float imageY, int button)
    {
        int screenWidth = Screen.width;
        int screenHeight = Screen.height;

        var screenPoint = CoordinateConverter.ImageToUnityScreenPoint(imageX, imageY, screenHeight);
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
                        // Create PointerEventData
                        object pointerData = Activator.CreateInstance(pointerDataType, new object[] { currentEventSystem });
                        PropertyInfo? posProp = pointerDataType.GetProperty("position");
                        posProp?.SetValue(pointerData, new Vector2(screenX, screenY), null);

                        // Create List<RaycastResult>
                        Type listType = typeof(List<>).MakeGenericType(raycastResultType);
                        IList raycastList = (IList)Activator.CreateInstance(listType);

                        // Call EventSystem.RaycastAll(pointerData, list)
                        MethodInfo? raycastMethod = eventSystemType.GetMethod("RaycastAll", new Type[] { pointerDataType, listType });
                        raycastMethod?.Invoke(currentEventSystem, new object[] { pointerData, raycastList });

                        if (raycastList.Count > 0)
                        {
                            object firstHit = raycastList[0];
                            PropertyInfo? goProp = raycastResultType.GetProperty("gameObject");
                            GameObject? hitGo = goProp?.GetValue(firstHit, null) as GameObject;

                            if (hitGo != null)
                            {
                                string hitId = UguiProvider.BuildHierarchyPath(hitGo.transform);
                                string hitType = hitGo.name;
                                bool dispatched = DispatchClickToGameObject(hitGo);

                                return new VisionClickResponse
                                {
                                    Success = dispatched,
                                    HitElementId = hitId,
                                    HitElementType = hitType,
                                    HitPointX = imageX,
                                    HitPointY = imageY,
                                    Message = dispatched ? "Elemento colpito e cliccato con successo." : "Elemento colpito ma invio evento fallito."
                                };
                            }
                        }
                    }
                    catch { }
                }
            }
        }

        // 2D Physics Collider fallback (for games using 2D world-space buttons)
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
                            return new VisionClickResponse
                            {
                                Success = dispatched,
                                HitElementId = hitId,
                                HitElementType = hitGo.name,
                                HitPointX = imageX,
                                HitPointY = imageY,
                                Message = dispatched ? "Elemento Collider2D colpito e cliccato con successo." : "Collider2D colpito ma invio evento fallito."
                            };
                        }
                    }
                }
            }
        }
        catch { }

        return new VisionClickResponse
        {
            Success = false,
            HitPointX = imageX,
            HitPointY = imageY,
            Message = "Nessun elemento UI o Collider2D intercettato dal Raycast alle coordinate (" + imageX + ", " + imageY + ")."
        };
    }

    private static bool DispatchClickToGameObject(GameObject go)
    {
        // 1. Try ExecuteEvents.Execute(target, pointerData, ExecuteEvents.pointerClickHandler)
        try
        {
            Type? execEventsType = CapabilitiesDetector.FindType("UnityEngine.EventSystems.ExecuteEvents");
            Type? eventSystemType = CapabilitiesDetector.FindType("UnityEngine.EventSystems.EventSystem");
            Type? pointerDataType = CapabilitiesDetector.FindType("UnityEngine.EventSystems.PointerEventData");

            if (execEventsType != null && eventSystemType != null && pointerDataType != null)
            {
                PropertyInfo? currentProp = eventSystemType.GetProperty("current", BindingFlags.Public | BindingFlags.Static);
                object? currentEventSystem = currentProp?.GetValue(null, null);

                object? pointerData = Activator.CreateInstance(pointerDataType, new object?[] { currentEventSystem });
                if (pointerData != null)
                {
                    PropertyInfo? handlerProp = execEventsType.GetProperty("pointerClickHandler", BindingFlags.Public | BindingFlags.Static);
                    object? handler = handlerProp?.GetValue(null, null);

                    if (handler != null)
                    {
                        MethodInfo[] methods = execEventsType.GetMethods(BindingFlags.Public | BindingFlags.Static);
                        foreach (var m in methods)
                        {
                            if (m.Name == "Execute" && m.IsGenericMethod && m.GetParameters().Length == 3)
                            {
                                Type? handlerInterfaceType = CapabilitiesDetector.FindType("UnityEngine.EventSystems.IPointerClickHandler");
                                if (handlerInterfaceType != null)
                                {
                                    MethodInfo generic = m.MakeGenericMethod(handlerInterfaceType);
                                    object? result = generic.Invoke(null, new object[] { go, pointerData, handler });
                                    if (result is bool executed && executed)
                                    {
                                        return true;
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
        catch { }

        // 2. Direct fallback: Button.onClick.Invoke()
        try
        {
            Component[] comps = go.GetComponents<Component>();
            foreach (var comp in comps)
            {
                if (comp == null) continue;
                PropertyInfo? onClickProp = comp.GetType().GetProperty("onClick");
                if (onClickProp != null)
                {
                    object? onClickVal = onClickProp.GetValue(comp, null);
                    MethodInfo? invokeMethod = onClickVal?.GetType().GetMethod("Invoke", Type.EmptyTypes);
                    if (invokeMethod != null)
                    {
                        invokeMethod.Invoke(onClickVal, null);
                        return true;
                    }
                }
            }
        }
        catch { }

        // 3. Generic method invocation: OnClick() or Click() with no arguments
        try
        {
            Component[] comps = go.GetComponents<Component>();
            foreach (var comp in comps)
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
        catch { }

        // 4. Standard Unity SendMessage fallback
        try
        {
            go.SendMessage("OnMouseDown", SendMessageOptions.DontRequireReceiver);
            go.SendMessage("OnMouseUpAsButton", SendMessageOptions.DontRequireReceiver);
            go.SendMessage("OnClick", SendMessageOptions.DontRequireReceiver);
            return true;
        }
        catch { }

        return false;
    }
}
