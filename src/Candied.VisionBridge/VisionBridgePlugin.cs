using System;
using BepInEx;
using Candied.VisionBridge.Protocol;

namespace Candied.VisionBridge;

[BepInPlugin(VisionConstants.PluginGuid, VisionConstants.PluginName, VisionConstants.PluginVersion)]
public sealed class VisionBridgePlugin : BaseUnityPlugin
{
    private MainThreadDispatcher? _dispatcher;
    private VisionHttpServer? _server;
    private IdleTracker? _idleTracker;

    private void Awake()
    {
        string? portStr = Environment.GetEnvironmentVariable(VisionConstants.EnvPort);
        string? token = Environment.GetEnvironmentVariable(VisionConstants.EnvToken);

        if (string.IsNullOrEmpty(portStr) || !int.TryParse(portStr, out int port) || port <= 0 || port > 65535)
        {
            Logger.LogInfo("[Candied.VisionBridge] Variabile CANDIED_PORT non definita o non valida. Server HTTP inattivo.");
            return;
        }

        if (string.IsNullOrEmpty(token))
        {
            Logger.LogWarning("[Candied.VisionBridge] Variabile CANDIED_TOKEN mancante. Server HTTP non avviato per sicurezza.");
            return;
        }

        try
        {
            VirtualInputDeviceManager.Initialize();

            _dispatcher = new MainThreadDispatcher();
            _idleTracker = new IdleTracker();
            var uiProvider = new UguiProvider();

            _server = new VisionHttpServer(port, token, _dispatcher, this, uiProvider, _idleTracker);
            _server.Start();

            Logger.LogInfo($"[Candied.VisionBridge] Server HTTP avviato con successo su 127.0.0.1:{port} (Protocol: {VisionConstants.ProtocolVersion})");
        }
        catch (Exception ex)
        {
            Logger.LogError($"[Candied.VisionBridge] Errore durante l'avvio del server HTTP: {ex.Message}");
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
        catch { }

        _dispatcher?.Clear();
        _dispatcher = null;
    }
}
