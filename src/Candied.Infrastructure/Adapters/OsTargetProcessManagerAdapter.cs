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

namespace Candied.Infrastructure.Adapters;

internal sealed class OsTargetProcessManagerAdapter : ITargetProcessManager
{
    public Task<Result<TargetProcessInfo?, ProcessManagementError>> GetRunningTargetAsync(
        TargetDescriptor target,
        CancellationToken ct = default)
    {
        try
        {
            var proc = FindTargetProcesses(target).FirstOrDefault();
            if (proc == null)
            {
                return Task.FromResult(Result.Ok<TargetProcessInfo?, ProcessManagementError>(null));
            }

            return Task.FromResult(Result.Ok<TargetProcessInfo?, ProcessManagementError>(new TargetProcessInfo(
                Id: new ProcessId(proc.Id),
                ProcessName: proc.ProcessName,
                MainWindowTitle: proc.MainWindowTitle,
                IsResponding: proc.Responding,
                StartTime: SafeGetStartTime(proc))));
        }
        catch (Exception ex)
        {
            return Task.FromResult(Result.Fail<TargetProcessInfo?, ProcessManagementError>(
                new ProcessManagementError($"Failed to query target process: {ex.Message}", ex.ToString())));
        }
    }

    public async Task<Result<TargetProcessInfo, ProcessManagementError>> LaunchTargetAsync(
        TargetDescriptor target,
        TargetLaunchOptions options,
        CancellationToken ct = default)
    {
        try
        {
            var existing = await GetRunningTargetAsync(target, ct).ConfigureAwait(false);
            if (existing.IsSuccess && existing.Value != null)
            {
                return Result.Ok<TargetProcessInfo, ProcessManagementError>(existing.Value);
            }

            string? exePath;
            string arguments = string.Empty;
            string workingDir;

            if (!string.IsNullOrWhiteSpace(target.RunnerPath))
            {
                exePath = target.RunnerPath;
                workingDir = Path.GetDirectoryName(target.RunnerPath) ?? Directory.GetCurrentDirectory();
                arguments = $"\"{target.TargetPath}\"";
            }
            else if (File.Exists(target.TargetPath))
            {
                exePath = target.TargetPath;
                workingDir = Path.GetDirectoryName(target.TargetPath) ?? Directory.GetCurrentDirectory();
            }
            else
            {
                exePath = FindExecutableInDirectory(target.TargetPath, target.Name);
                workingDir = target.TargetPath;
            }

            if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
            {
                return Result.Fail<TargetProcessInfo, ProcessManagementError>(
                    new ProcessManagementError($"Launchable executable could not be found for '{target.TargetPath}'."));
            }

            if (options.CustomArguments != null && options.CustomArguments.Count > 0)
            {
                string extra = string.Join(" ", options.CustomArguments);
                arguments = string.IsNullOrEmpty(arguments) ? extra : $"{arguments} {extra}";
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = arguments,
                WorkingDirectory = workingDir,
                UseShellExecute = true
            };

            var process = Process.Start(startInfo);
            if (process == null)
            {
                return Result.Fail<TargetProcessInfo, ProcessManagementError>(
                    new ProcessManagementError($"Process.Start returned null for '{exePath}'."));
            }

            try
            {
                process.WaitForInputIdle(1000);
                process.Refresh();
            }
            catch
            {
            }

            return Result.Ok<TargetProcessInfo, ProcessManagementError>(new TargetProcessInfo(
                Id: new ProcessId(process.Id),
                ProcessName: process.ProcessName,
                MainWindowTitle: process.MainWindowTitle,
                IsResponding: true,
                StartTime: DateTime.UtcNow));
        }
        catch (Exception ex)
        {
            return Result.Fail<TargetProcessInfo, ProcessManagementError>(
                new ProcessManagementError($"Failed to launch target: {ex.Message}", ex.ToString()));
        }
    }

    public Task<Result<Unit, ProcessManagementError>> StopTargetAsync(
        TargetDescriptor target,
        CancellationToken ct = default)
    {
        try
        {
            var processes = FindTargetProcesses(target);
            foreach (var process in processes)
            {
                try
                {
                    if (process.HasExited) continue;

                    bool closedGracefully = false;
                    try
                    {
                        if (process.MainWindowHandle != IntPtr.Zero)
                        {
                            closedGracefully = process.CloseMainWindow();
                        }
                    }
                    catch
                    {
                    }

                    if (!closedGracefully || !process.WaitForExit(2000))
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
                    catch
                    {
                    }
                }
            }

            return Task.FromResult(Result.Ok<Unit, ProcessManagementError>(new Unit()));
        }
        catch (Exception ex)
        {
            return Task.FromResult(Result.Fail<Unit, ProcessManagementError>(
                new ProcessManagementError($"Failed to stop target process: {ex.Message}", ex.ToString())));
        }
    }

    private static List<Process> FindTargetProcesses(TargetDescriptor target)
    {
        string? exePath = !string.IsNullOrWhiteSpace(target.RunnerPath)
            ? target.RunnerPath
            : (File.Exists(target.TargetPath) ? target.TargetPath : FindExecutableInDirectory(target.TargetPath, target.Name));

        string targetProcessName = Path.GetFileNameWithoutExtension(exePath ?? string.Empty);
        string compactTargetName = target.Name.Replace(" ", string.Empty, StringComparison.Ordinal);
        string? rootDir = Directory.Exists(target.TargetPath)
            ? target.TargetPath
            : Path.GetDirectoryName(target.TargetPath);

        var matched = new List<Process>();
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                if (p.HasExited) continue;

                if (!string.IsNullOrEmpty(targetProcessName) &&
                    string.Equals(p.ProcessName, targetProcessName, StringComparison.OrdinalIgnoreCase))
                {
                    matched.Add(p);
                    continue;
                }

                if (string.IsNullOrWhiteSpace(target.RunnerPath) &&
                    (string.Equals(p.ProcessName, target.Name, StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(p.ProcessName.Replace(" ", string.Empty, StringComparison.Ordinal), compactTargetName, StringComparison.OrdinalIgnoreCase)))
                {
                    matched.Add(p);
                    continue;
                }

                if (!string.IsNullOrEmpty(rootDir))
                {
                    try
                    {
                        string? modulePath = p.MainModule?.FileName;
                        if (!string.IsNullOrEmpty(modulePath) && modulePath.StartsWith(rootDir, StringComparison.OrdinalIgnoreCase))
                        {
                            matched.Add(p);
                        }
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }
        }

        return matched;
    }

    private static string? FindExecutableInDirectory(string rootDir, string targetName)
    {
        if (!Directory.Exists(rootDir))
        {
            return null;
        }

        var exeFiles = Directory.GetFiles(rootDir, "*.exe", SearchOption.TopDirectoryOnly)
            .Where(f => !Path.GetFileName(f).StartsWith("UnityCrashHandler", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (exeFiles.Count == 0)
        {
            return null;
        }

        string cleanName = targetName.Replace(" ", string.Empty, StringComparison.Ordinal);
        return exeFiles.FirstOrDefault(f =>
            Path.GetFileNameWithoutExtension(f).Replace(" ", string.Empty, StringComparison.Ordinal)
                .Equals(cleanName, StringComparison.OrdinalIgnoreCase))
            ?? exeFiles[0];
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
