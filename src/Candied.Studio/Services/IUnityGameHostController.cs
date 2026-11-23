using System;
using System.Threading;
using System.Threading.Tasks;
using Candied.Core.Models;
using Candied.Primitives;

namespace Candied.Studio.Services;

public interface IUnityGameHostController : IDisposable
{
    string? CurrentGamePath { get; }
    string? CurrentGameName { get; }
    bool IsRunning { get; }
    int? ActiveGameProcessId { get; }
    IntPtr EmbeddedGameHwnd { get; }
    string EmbeddingMode { get; }
    int? ActiveVisionBridgePort { get; }
    string? ActiveVisionBridgeToken { get; }
    string? ActiveVisionBridgeBaseUrl { get; }
    int TargetGameWidth { get; }
    int TargetGameHeight { get; }
    int LastPanelWidth { get; }
    int LastPanelHeight { get; }

    event Action<string>? StatusChanged;
    event Action<string>? LogReceived;
    event Action<int>? ProcessExited;

    Task<Result<GameEnvironment, string>> ValidateAndSelectGameAsync(
        string executablePath,
        CancellationToken ct = default);

    Task<bool> LaunchAsync(
        IntPtr containerHwnd,
        int containerPixelWidth,
        int containerPixelHeight,
        CancellationToken ct = default);

    Task StopAsync();

    Task<bool> RestartAsync(
        IntPtr containerHwnd,
        int containerPixelWidth,
        int containerPixelHeight,
        CancellationToken ct = default);

    void Resize(int containerPixelWidth, int containerPixelHeight);
    void Resize(int containerPixelWidth, int containerPixelHeight, int gameWidth, int gameHeight);

    byte[] CaptureExternalScreenshot();

    bool SendInputClick(int clientX, int clientY);
}
