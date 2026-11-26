using System;
using System.Diagnostics;
using System.Threading;
using Candied.Vision.Protocol;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Candied.VisionSensors.UnityMono;

public sealed class IdleTracker
{
    private readonly object _lock = new object();
    private int _consecutiveStableFrames;
    private int _lastElementCount = -1;
    private string _lastSceneName = string.Empty;

    public int ConsecutiveStableFrames
    {
        get
        {
            lock (_lock)
            {
                return _consecutiveStableFrames;
            }
        }
    }

    public void OnFrameUpdate()
    {
        lock (_lock)
        {
            string currentScene = SceneManager.GetActiveScene().name ?? string.Empty;
            int currentCount = CountActiveUi();

            if (currentScene == _lastSceneName && currentCount == _lastElementCount)
            {
                _consecutiveStableFrames++;
            }
            else
            {
                _consecutiveStableFrames = 0;
                _lastSceneName = currentScene;
                _lastElementCount = currentCount;
            }
        }
    }

    private static int CountActiveUi()
    {
        try
        {
            Type? selectableType = CapabilitiesDetector.FindType("UnityEngine.UI.Selectable");
            if (selectableType != null)
            {
                var objs = UnityEngine.Object.FindObjectsOfType(selectableType);
                return objs?.Length ?? 0;
            }
        }
        catch
        {
        }

        return 0;
    }

    public VisionWaitIdleResponse WaitForIdle(int targetFrames = 10, int timeoutMs = 5000)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (ConsecutiveStableFrames >= targetFrames)
            {
                return new VisionWaitIdleResponse
                {
                    IdleReached = true,
                    ElapsedFrames = ConsecutiveStableFrames,
                    ElapsedMs = sw.ElapsedMilliseconds,
                    Reason = "Reached " + targetFrames + " stable frames."
                };
            }

            Thread.Sleep(16);
        }

        return new VisionWaitIdleResponse
        {
            IdleReached = false,
            ElapsedFrames = ConsecutiveStableFrames,
            ElapsedMs = sw.ElapsedMilliseconds,
            Reason = "Timed out (" + timeoutMs + "ms) waiting for " + targetFrames + " stable frames."
        };
    }
}
