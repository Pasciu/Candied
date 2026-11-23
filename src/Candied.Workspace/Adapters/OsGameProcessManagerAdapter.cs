using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Candied.Core.Errors;
using Candied.Core.Models;
using Candied.Core.Ports;
using Candied.Primitives;

namespace Candied.Workspace.Adapters;

internal sealed class OsGameProcessManagerAdapter(CandiedStorageLayout? layout = null) : IGameProcessManager
{
    private readonly CandiedStorageLayout _storageLayout = layout ?? CandiedWorkspaceStorage.GetLayout();

    public Task<Result<GameProcessInfo?, ProcessManagementError>> GetRunningGameAsync(
        GameEnvironment game,
        CancellationToken ct = default)
    {
        try
        {
            var process = FindGameProcess(game);
            if (process == null)
            {
                return Task.FromResult(Result.Ok<GameProcessInfo?, ProcessManagementError>(null));
            }

            var hasBridge = CheckBridgeHeartbeat(game);
            var info = new GameProcessInfo(
                Id: new ProcessId(process.Id),
                ProcessName: process.ProcessName,
                MainWindowTitle: process.MainWindowTitle,
                IsResponding: process.Responding,
                HasBridgeAttached: hasBridge,
                StartTime: SafeGetStartTime(process)
            );

            return Task.FromResult(Result.Ok<GameProcessInfo?, ProcessManagementError>(info));
        }
        catch (Exception ex)
        {
            return Task.FromResult(Result.Fail<GameProcessInfo?, ProcessManagementError>(
                new ProcessManagementError($"Failed to check running game process: {ex.Message}", ex.ToString())));
        }
    }

    public async Task<Result<GameProcessInfo, ProcessManagementError>> LaunchGameAsync(
        GameEnvironment game,
        GameLaunchOptions options,
        CancellationToken ct = default)
    {
        try
        {
            // 1. Attach-first strategy
            var existingResult = await GetRunningGameAsync(game, ct).ConfigureAwait(false);
            if (existingResult.IsSuccess && existingResult.Value != null)
            {
                return Result.Ok<GameProcessInfo, ProcessManagementError>(existingResult.Value);
            }

            // 2. Find executable in GameRootPath
            var exePath = FindExecutable(game.GameRootPath, game.GameName);
            if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
            {
                return Result.Fail<GameProcessInfo, ProcessManagementError>(
                    new ProcessManagementError($"Game executable could not be found in '{game.GameRootPath}'."));
            }

            // 3. Prepare launch arguments
            var args = string.Empty;
            if (options.Windowed)
            {
                args = $"-screen-fullscreen 0 -screen-width {options.WindowWidth} -screen-height {options.WindowHeight} -popupwindow";
            }

            if (options.CustomArguments != null && options.CustomArguments.Count > 0)
            {
                var extra = string.Join(" ", options.CustomArguments);
                args = string.IsNullOrEmpty(args) ? extra : $"{args} {extra}";
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = args,
                WorkingDirectory = game.GameRootPath,
                UseShellExecute = true
            };

            var process = Process.Start(startInfo);
            if (process == null)
            {
                return Result.Fail<GameProcessInfo, ProcessManagementError>(
                    new ProcessManagementError($"Process.Start returned null for '{exePath}'."));
            }

            try
            {
                process.WaitForInputIdle(1000);
            }
            catch
            {
                // WaitForInputIdle may throw on platforms without standard message pump
            }

            try
            {
                process.Refresh();
            }
            catch { }

            var info = new GameProcessInfo(
                Id: new ProcessId(process.Id),
                ProcessName: process.ProcessName,
                MainWindowTitle: process.MainWindowTitle,
                IsResponding: true,
                HasBridgeAttached: false,
                StartTime: DateTime.UtcNow
            );

            return Result.Ok<GameProcessInfo, ProcessManagementError>(info);
        }
        catch (Exception ex)
        {
            return Result.Fail<GameProcessInfo, ProcessManagementError>(
                new ProcessManagementError($"Failed to launch game executable: {ex.Message}", ex.ToString()));
        }
    }

    public Task<Result<Candied.Primitives.Unit, ProcessManagementError>> StopGameAsync(
        GameEnvironment game,
        CancellationToken ct = default)
    {
        try
        {
            var processes = FindAllGameProcesses(game);
            if (processes.Count == 0)
            {
                return Task.FromResult(Result.Ok<Candied.Primitives.Unit, ProcessManagementError>(new Candied.Primitives.Unit()));
            }

            foreach (var process in processes)
            {
                try
                {
                    if (process.HasExited) continue;

                    var closedGracefully = false;
                    try
                    {
                        if (process.MainWindowHandle != IntPtr.Zero)
                        {
                            closedGracefully = process.CloseMainWindow();
                        }
                    }
                    catch { }

                    if (closedGracefully)
                    {
                        if (!process.WaitForExit(2000))
                        {
                            process.Kill(entireProcessTree: true);
                        }
                    }
                    else
                    {
                        process.Kill(entireProcessTree: true);
                    }
                }
                catch
                {
                    try
                    {
                        if (!process.HasExited)
                        {
                            process.Kill(entireProcessTree: true);
                        }
                    }
                    catch { }
                }
            }

            return Task.FromResult(Result.Ok<Candied.Primitives.Unit, ProcessManagementError>(new Candied.Primitives.Unit()));
        }
        catch (Exception ex)
        {
            return Task.FromResult(Result.Fail<Candied.Primitives.Unit, ProcessManagementError>(
                new ProcessManagementError($"Failed to stop game process: {ex.Message}", ex.ToString())));
        }
    }

    private static List<Process> FindAllGameProcesses(GameEnvironment game)
    {
        var targetExeName = Path.GetFileNameWithoutExtension(FindExecutable(game.GameRootPath, game.GameName) ?? string.Empty);
        var targetGameName = game.GameName.Replace(" ", string.Empty);
        var matched = new List<Process>();

        foreach (var p in Process.GetProcesses())
        {
            try
            {
                if (p.HasExited) continue;

                if (!string.IsNullOrEmpty(targetExeName) && string.Equals(p.ProcessName, targetExeName, StringComparison.OrdinalIgnoreCase))
                {
                    matched.Add(p);
                    continue;
                }

                if (string.Equals(p.ProcessName, game.GameName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(p.ProcessName.Replace(" ", string.Empty), targetGameName, StringComparison.OrdinalIgnoreCase))
                {
                    matched.Add(p);
                    continue;
                }

                // Check MainModule if accessible
                try
                {
                    var modulePath = p.MainModule?.FileName;
                    if (!string.IsNullOrEmpty(modulePath) && modulePath.StartsWith(game.GameRootPath, StringComparison.OrdinalIgnoreCase))
                    {
                        matched.Add(p);
                    }
                }
                catch
                {
                    // Access denied on system processes is expected
                }
            }
            catch
            {
                // Process could have exited during enumeration
            }
        }

        return matched;
    }

    private static Process? FindGameProcess(GameEnvironment game) =>
        FindAllGameProcesses(game).FirstOrDefault();

    private static string? FindExecutable(string rootDir, string gameName)
    {
        if (!Directory.Exists(rootDir)) return null;

        var exeFiles = Directory.GetFiles(rootDir, "*.exe", SearchOption.TopDirectoryOnly)
            .Where(f => !Path.GetFileName(f).StartsWith("UnityCrashHandler", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (exeFiles.Count == 0) return null;

        var cleanGameName = gameName.Replace(" ", string.Empty);
        return exeFiles.FirstOrDefault(f => Path.GetFileNameWithoutExtension(f).Replace(" ", string.Empty).Equals(cleanGameName, StringComparison.OrdinalIgnoreCase))
            ?? exeFiles.FirstOrDefault();
    }

    private bool CheckBridgeHeartbeat(GameEnvironment game)
    {
        var heartbeatFile = Path.Combine(_storageLayout.GetIpcDir(game.GameName), "heartbeat.json");

        if (!File.Exists(heartbeatFile)) return false;

        var writeTime = File.GetLastWriteTimeUtc(heartbeatFile);
        return (DateTime.UtcNow - writeTime).TotalSeconds < 60;
    }

    private static DateTime SafeGetStartTime(Process process)
    {
        try
        {
            return process.StartTime;
        }
        catch
        {
            return DateTime.UtcNow;
        }
    }
}
