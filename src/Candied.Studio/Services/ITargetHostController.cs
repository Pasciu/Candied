using System;
using System.Threading;
using System.Threading.Tasks;
using Candied.Primitives;
using Candied.Vision.Models;
using Candied.Vision.Protocol;

namespace Candied.Studio.Services;

public interface ITargetHostController : IDisposable
{
    WindowLaunchSpec? ActiveSpec { get; }
    string? CurrentTargetPath { get; }
    string? CurrentRunnerPath { get; }
    string? CurrentTargetName { get; }
    bool IsRunning { get; }
    int? ActiveTargetProcessId { get; }
    IntPtr EmbeddedTargetHwnd { get; }
    string EmbeddingMode { get; }
    int? ActiveSensorPort { get; }
    string? ActiveSensorToken { get; }
    string? ActiveSensorBaseUrl { get; }
    int TargetWidth { get; }
    int TargetHeight { get; }
    int LastPanelWidth { get; }
    int LastPanelHeight { get; }

    event Action<string>? StatusChanged;
    event Action<string>? LogReceived;
    event Action<int>? ProcessExited;

    Result<WindowLaunchSpec, string> SelectTarget(
        string targetPath,
        string? runnerPath = null,
        string? extraArguments = null);

    void SelectLaunchSpec(WindowLaunchSpec spec);

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
    void Resize(int containerPixelWidth, int containerPixelHeight, int targetWidth, int targetHeight);

    byte[] CaptureExternalScreenshot();

    VisionInputResponse DispatchBaseWindowInput(VisionInputRequest request);
}
