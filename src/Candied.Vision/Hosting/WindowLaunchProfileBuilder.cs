using System;
using System.IO;
using System.Linq;
using Candied.Primitives;
using Candied.Vision.Models;

namespace Candied.Vision.Hosting;

/// <summary>
/// Builds a generic WindowLaunchSpec from any PC executable, game directory, or Emulator + ROM pair
/// without requiring any decompiler or modding Core dependencies.
/// </summary>
public static class WindowLaunchProfileBuilder
{
    public static Result<WindowLaunchSpec, string> Build(
        string targetPath,
        string? runnerPath = null,
        string? extraArguments = null,
        int targetWidth = 1280,
        int targetHeight = 720)
    {
        if (string.IsNullOrWhiteSpace(targetPath))
        {
            return Result.Fail<WindowLaunchSpec, string>("Target path cannot be empty.");
        }

        string normalizedTarget = Path.GetFullPath(targetPath.Trim());

        // Case 1: Explicit Runner / Emulator + ROM or game script
        if (!string.IsNullOrWhiteSpace(runnerPath))
        {
            string normalizedRunner = Path.GetFullPath(runnerPath.Trim());
            if (!File.Exists(normalizedRunner))
            {
                return Result.Fail<WindowLaunchSpec, string>($"Runner/emulator executable not found: '{normalizedRunner}'.");
            }

            if (!File.Exists(normalizedTarget) && !Directory.Exists(normalizedTarget))
            {
                return Result.Fail<WindowLaunchSpec, string>($"Target ROM/path not found: '{normalizedTarget}'.");
            }

            string romName = Path.GetFileNameWithoutExtension(normalizedTarget);
            if (string.IsNullOrWhiteSpace(romName))
            {
                romName = new DirectoryInfo(normalizedTarget).Name;
            }

            string combinedArgs = string.IsNullOrWhiteSpace(extraArguments)
                ? $"\"{normalizedTarget}\""
                : $"{extraArguments} \"{normalizedTarget}\"";

            return Result.Ok<WindowLaunchSpec, string>(new WindowLaunchSpec(
                TargetName: romName,
                TargetPath: normalizedTarget,
                RunnerPath: normalizedRunner,
                ExtraArguments: combinedArgs,
                WorkingDirectory: Path.GetDirectoryName(normalizedRunner),
                WindowClassHint: null,
                SupportsParentHwndFlag: false,
                LogFilePath: null,
                TargetWidth: targetWidth,
                TargetHeight: targetHeight));
        }

        // Case 2: Direct PC game executable or game directory
        string? exePath = null;
        string? rootDir = null;

        if (File.Exists(normalizedTarget))
        {
            exePath = normalizedTarget;
            rootDir = Path.GetDirectoryName(normalizedTarget);
        }
        else if (Directory.Exists(normalizedTarget))
        {
            rootDir = normalizedTarget;
            exePath = FindPrimaryExecutableInDirectory(normalizedTarget);
            if (exePath == null)
            {
                return Result.Fail<WindowLaunchSpec, string>(
                    $"No launchable executable found in '{normalizedTarget}'. If this is a ROM or script, specify runnerPath.");
            }
        }
        else
        {
            return Result.Fail<WindowLaunchSpec, string>($"Target path does not exist: '{normalizedTarget}'.");
        }

        string targetName = Path.GetFileNameWithoutExtension(exePath);
        bool isUnityWindowHost = rootDir != null &&
            (File.Exists(Path.Combine(rootDir, "UnityPlayer.dll")) ||
             Directory.GetDirectories(rootDir, "*_Data").Length > 0);

        string? logPath = null;
        if (rootDir != null)
        {
            string bepInExLog = Path.Combine(rootDir, "BepInEx", "LogOutput.log");
            if (File.Exists(bepInExLog) || Directory.Exists(Path.Combine(rootDir, "BepInEx")))
            {
                logPath = bepInExLog;
            }
        }

        if (isUnityWindowHost && rootDir != null)
        {
            var dataDir = Directory.GetDirectories(rootDir, "*_Data").FirstOrDefault();
            if (dataDir != null)
            {
                string dirName = Path.GetFileName(dataDir);
                if (dirName.EndsWith("_Data", StringComparison.OrdinalIgnoreCase) && dirName.Length > 5)
                {
                    targetName = dirName[..^5];
                }
            }
        }

        return Result.Ok<WindowLaunchSpec, string>(new WindowLaunchSpec(
            TargetName: targetName,
            TargetPath: exePath,
            RunnerPath: null,
            ExtraArguments: extraArguments,
            WorkingDirectory: rootDir,
            WindowClassHint: isUnityWindowHost ? "UnityWndClass" : null,
            SupportsParentHwndFlag: isUnityWindowHost,
            LogFilePath: logPath,
            TargetWidth: targetWidth,
            TargetHeight: targetHeight));
    }

    public static string BuildEffectiveArguments(WindowLaunchSpec spec, IntPtr containerHwnd, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(spec);

        if (!string.IsNullOrWhiteSpace(spec.RunnerPath))
        {
            return spec.ExtraArguments ?? $"\"{spec.TargetPath}\"";
        }

        if (spec.SupportsParentHwndFlag && containerHwnd != IntPtr.Zero)
        {
            int w = Math.Max(320, width);
            int h = Math.Max(240, height);
            string unityArgs = $"-parentHWND {containerHwnd.ToInt64()} -popupwindow -screen-fullscreen 0 -screen-width {w} -screen-height {h}";
            return string.IsNullOrWhiteSpace(spec.ExtraArguments)
                ? unityArgs
                : $"{unityArgs} {spec.ExtraArguments}";
        }

        return spec.ExtraArguments ?? string.Empty;
    }

    private static string? FindPrimaryExecutableInDirectory(string directoryPath)
    {
        var exeFiles = Directory.GetFiles(directoryPath, "*.exe", SearchOption.TopDirectoryOnly)
            .Where(f =>
            {
                string name = Path.GetFileName(f);
                return !name.StartsWith("UnityCrashHandler", StringComparison.OrdinalIgnoreCase) &&
                       !name.StartsWith("unins", StringComparison.OrdinalIgnoreCase) &&
                       !name.Contains("CrashReport", StringComparison.OrdinalIgnoreCase);
            })
            .ToList();

        if (exeFiles.Count == 0)
        {
            return null;
        }

        var dataDirs = Directory.GetDirectories(directoryPath, "*_Data");
        if (dataDirs.Length > 0)
        {
            string expectedExeName = Path.GetFileName(dataDirs[0])[..^5] + ".exe";
            var matched = exeFiles.FirstOrDefault(f =>
                string.Equals(Path.GetFileName(f), expectedExeName, StringComparison.OrdinalIgnoreCase));
            if (matched != null)
            {
                return matched;
            }
        }

        return exeFiles[0];
    }
}
