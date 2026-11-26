using System;
using System.Collections.Generic;
using BepInEx;
using Candied.Vision.Protocol;

namespace Candied.VisionSensors.UnityMono;

public interface IUiProvider
{
    IList<VisionUiElementDto> GetInteractiveElements();
}

[BepInPlugin(PluginGuid, PluginName, VisionConstants.SensorVersion)]
public sealed class UnityMonoVisionSensorPlugin : BaseUnityPlugin
{
    public const string PluginGuid = "com.candied.visionsensor.unitymono";
    public const string PluginName = "Candied.VisionSensors.UnityMono";

    private MainThreadDispatcher? _dispatcher;
    private VisionHttpServer? _server;
    private IdleTracker? _idleTracker;

    private void Awake()
    {
        string? portStr = Environment.GetEnvironmentVariable(VisionConstants.EnvPort);
        string? token = Environment.GetEnvironmentVariable(VisionConstants.EnvToken);

        if (string.IsNullOrEmpty(portStr) || !int.TryParse(portStr, out int port) || port <= 0 || port > 65535)
        {
            Logger.LogInfo("[Candied.VisionSensor] CANDIED_PORT not set or invalid. HTTP sensor inactive.");
            return;
        }

        if (string.IsNullOrEmpty(token))
        {
            Logger.LogWarning("[Candied.VisionSensor] CANDIED_TOKEN missing. HTTP sensor aborted for security.");
            return;
        }

        try
        {
            VirtualInputDeviceManager.Initialize();

            _dispatcher = new MainThreadDispatcher();
            _idleTracker = new IdleTracker();
            var uiProvider = new UguiProvider();

            _server = new VisionHttpServer(port, token!, _dispatcher, this, uiProvider, _idleTracker);
            _server.Start();

            Logger.LogInfo("[Candied.VisionSensor] HTTP sensor listening on 127.0.0.1:" + port + " (Protocol: " + VisionConstants.ProtocolVersion + ")");
        }
        catch (Exception ex)
        {
            Logger.LogError("[Candied.VisionSensor] Failed to start HTTP sensor: " + ex.Message);
        }
    }

    private void Update()
    {
        VirtualInputDeviceManager.OnFrameUpdate();
        _dispatcher?.ProcessQueue();
        _idleTracker?.OnFrameUpdate();
    }

    private void OnDestroy()
    {
        try
        {
            _server?.Stop();
            _server?.Dispose();
            _server = null;
        }
        catch
        {
        }

        _dispatcher?.Clear();
        _dispatcher = null;
    }
}
