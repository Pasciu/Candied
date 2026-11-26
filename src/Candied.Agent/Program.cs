using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Candied.Agent.Mcp;
using Candied.Infrastructure;
using Candied.Platforms.Unity.Mono;
using Candied.Vision;
using Candied.Vision.Protocol;
using Microsoft.Extensions.DependencyInjection;

namespace Candied.Agent;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var services = new ServiceCollection();

        // 1. Core + Shared Agnostic Infrastructure
        services.AddCandiedCoreAndInfrastructure();

        // 2. Universal 2-Tier Vision & 3-Channel Input Infrastructure
        services.AddVisionInfrastructure();

        // 3. Registered Target Platforms
        services.AddUnityMonoPlatform();

        // 4. Agent & MCP Orchestration
        services.AddTransient<AgentWorkbenchService>();
        services.AddTransient<McpServer>();

        using var serviceProvider = services.BuildServiceProvider();
        var workbench = serviceProvider.GetRequiredService<AgentWorkbenchService>();

        if (args.Length > 0)
        {
            string command = args[0].ToLowerInvariant();
            switch (command)
            {
                case "--help" or "-h" or "help":
                    PrintHelp(workbench);
                    return 0;

                case "inspect":
                    if (args.Length < 2)
                    {
                        Console.WriteLine("Usage: dotnet run --project src/Candied.Agent -- inspect <targetPath> [--runner <exe>]");
                        return 1;
                    }

                    Console.WriteLine(await workbench.InspectTargetAsync(
                        args[1],
                        GetOption(args, "--runner", "-r")).ConfigureAwait(false));
                    return 0;

                case "setup":
                    Console.WriteLine(await workbench.SetupEnvironmentAsync(
                        GetPositionalOrOption(args, 1, "--target", "-t"),
                        GetOption(args, "--runner", "-r"),
                        HasFlag(args, "--force", "-f"),
                        !HasFlag(args, "--no-console")).ConfigureAwait(false));
                    return 0;

                case "rollback":
                    Console.WriteLine(await workbench.RollbackEnvironmentAsync(
                        GetPositionalOrOption(args, 1, "--target", "-t"),
                        GetOption(args, "--runner", "-r")).ConfigureAwait(false));
                    return 0;

                case "extract-code":
                    if (args.Length < 2)
                    {
                        Console.WriteLine("Usage: dotnet run --project src/Candied.Agent -- extract-code <moduleName> [targetPath]");
                        return 1;
                    }

                    Console.WriteLine(await workbench.ExtractCodeAsync(
                        args[1],
                        GetPositionalOrOption(args, 2, "--target", "-t"),
                        GetOption(args, "--runner", "-r")).ConfigureAwait(false));
                    return 0;

                case "query":
                    if (args.Length < 2)
                    {
                        Console.WriteLine("Usage: dotnet run --project src/Candied.Agent -- query <searchTerm> [--target path] [--kind K] [--namespace NS] [--module M] [--limit N]");
                        return 1;
                    }

                    int qLimit = int.TryParse(GetOption(args, "--limit", "--max", "-m"), out int ql) ? ql : 25;
                    Console.WriteLine(await workbench.QuerySymbolsAsync(
                        args[1],
                        GetPositionalOrOption(args, 2, "--target", "-t"),
                        GetOption(args, "--runner", "-r"),
                        GetOption(args, "--kind", "-k"),
                        GetOption(args, "--namespace", "--ns"),
                        GetOption(args, "--module"),
                        qLimit).ConfigureAwait(false));
                    return 0;

                case "search-code":
                    if (args.Length < 2)
                    {
                        Console.WriteLine("Usage: dotnet run --project src/Candied.Agent -- search-code <pattern> [--target path] [--namespace NS] [--module M] [--limit N]");
                        return 1;
                    }

                    int scLimit = int.TryParse(GetOption(args, "--limit", "--max", "-m"), out int scl) ? scl : 25;
                    Console.WriteLine(await workbench.SearchCodeAsync(
                        args[1],
                        GetPositionalOrOption(args, 2, "--target", "-t"),
                        GetOption(args, "--runner", "-r"),
                        GetOption(args, "--namespace", "--ns"),
                        GetOption(args, "--module"),
                        scLimit).ConfigureAwait(false));
                    return 0;

                case "type":
                    if (args.Length < 2)
                    {
                        Console.WriteLine("Usage: dotnet run --project src/Candied.Agent -- type <fullTypeName> [--target path] [--signatures-only] [--member M]");
                        return 1;
                    }

                    Console.WriteLine(await workbench.GetTypeDetailAsync(
                        args[1],
                        GetPositionalOrOption(args, 2, "--target", "-t"),
                        GetOption(args, "--runner", "-r"),
                        HasFlag(args, "--signatures-only", "--signatures"),
                        GetOption(args, "--member", "-m")).ConfigureAwait(false));
                    return 0;

                case "notes":
                    if (args.Length < 2)
                    {
                        Console.WriteLine("Usage: dotnet run --project src/Candied.Agent -- notes <markdown> [targetPath]");
                        return 1;
                    }

                    Console.WriteLine(await workbench.SaveResearchNotesAsync(
                        args[1],
                        GetPositionalOrOption(args, 2, "--target", "-t"),
                        GetOption(args, "--runner", "-r")).ConfigureAwait(false));
                    return 0;

                case "index-assets":
                    Console.WriteLine(await workbench.IndexAssetsAsync(
                        GetPositionalOrOption(args, 1, "--target", "-t"),
                        GetOption(args, "--runner", "-r"),
                        HasFlag(args, "--force", "-f")).ConfigureAwait(false));
                    return 0;

                case "discover-assets":
                    int daMax = int.TryParse(GetOption(args, "--limit", "--max", "-m"), out int dam) ? dam : 50;
                    int daOff = int.TryParse(GetOption(args, "--offset"), out int dao) ? dao : 0;
                    Console.WriteLine(await workbench.DiscoverAssetsAsync(
                        GetOption(args, "--pattern", "-p") ?? GetPositionalOrOption(args, 1),
                        GetOption(args, "--type"),
                        daMax,
                        GetOption(args, "--target", "-t"),
                        GetOption(args, "--runner", "-r"),
                        GetOption(args, "--container", "-c"),
                        GetOption(args, "--key", "--id"),
                        daOff).ConfigureAwait(false));
                    return 0;

                case "extract-assets":
                    int eaMax = int.TryParse(GetOption(args, "--limit", "--max", "-m"), out int eam) ? eam : 50;
                    Console.WriteLine(await workbench.ExtractAssetsAsync(
                        GetOption(args, "--pattern", "-p") ?? GetPositionalOrOption(args, 1),
                        GetOption(args, "--type"),
                        eaMax,
                        GetOption(args, "--out", "-o"),
                        GetOption(args, "--target", "-t"),
                        GetOption(args, "--runner", "-r"),
                        GetOption(args, "--container", "-c"),
                        GetOption(args, "--key", "--id"),
                        !HasFlag(args, "--no-disambiguate")).ConfigureAwait(false));
                    return 0;

                case "scaffold":
                    if (args.Length < 2)
                    {
                        Console.WriteLine("Usage: dotnet run --project src/Candied.Agent -- scaffold <modName> [targetPath] [--out dir] [--with-assets]");
                        return 1;
                    }

                    Console.WriteLine(await workbench.ScaffoldModAsync(
                        args[1],
                        GetOption(args, "--id"),
                        GetOption(args, "--version") ?? "1.0.0",
                        GetOption(args, "--out", "-o"),
                        GetPositionalOrOption(args, 2, "--target", "-t"),
                        GetOption(args, "--runner", "-r"),
                        HasFlag(args, "--with-assets")).ConfigureAwait(false));
                    return 0;

                case "build":
                    if (args.Length < 2)
                    {
                        Console.WriteLine("Usage: dotnet run --project src/Candied.Agent -- build <projectFilePath> [configuration]");
                        return 1;
                    }

                    Console.WriteLine(await workbench.BuildModAsync(
                        args[1],
                        GetPositionalOrOption(args, 2, "--config", "-c") ?? "Debug").ConfigureAwait(false));
                    return 0;

                case "logs":
                    int logMax = int.TryParse(GetOption(args, "--max", "-m"), out int lm) ? lm : 100;
                    Console.WriteLine(await workbench.ReadLogsAsync(
                        GetPositionalOrOption(args, 1, "--log"),
                        logMax,
                        null,
                        GetOption(args, "--target", "-t"),
                        GetOption(args, "--runner", "-r")).ConfigureAwait(false));
                    return 0;

                case "game-launch":
                    Console.WriteLine(await workbench.LaunchInStudioAsync(
                        GetPositionalOrOption(args, 1, "--target", "-t"),
                        GetOption(args, "--runner", "-r"),
                        GetOption(args, "--args"),
                        GetOption(args, "--wait-for-log")).ConfigureAwait(false));
                    return 0;

                case "game-close":
                    Console.WriteLine(await workbench.CloseStudioTargetAsync().ConfigureAwait(false));
                    return 0;

                case "recent-targets":
                    Console.WriteLine(await workbench.ListRecentTargetsAsync().ConfigureAwait(false));
                    return 0;

                case "status":
                    Console.WriteLine(await workbench.GetTargetStatusAsync(
                        GetPositionalOrOption(args, 1, "--target", "-t"),
                        GetOption(args, "--runner", "-r")).ConfigureAwait(false));
                    return 0;

                case "capabilities":
                    int? capPort = int.TryParse(GetOption(args, "--port", "-p"), out int cp) ? cp : null;
                    Console.WriteLine(await workbench.GetCapabilitiesAsync(capPort, GetOption(args, "--token")).ConfigureAwait(false));
                    return 0;

                case "screenshot":
                    int? scPort = int.TryParse(GetOption(args, "--port", "-p"), out int scp) ? scp : null;
                    Console.WriteLine(await workbench.CaptureScreenshotAsync(
                        GetOption(args, "--out", "-o"),
                        GetOption(args, "--target", "-t"),
                        scPort,
                        GetOption(args, "--token")).ConfigureAwait(false));
                    return 0;

                case "send-input":
                    string ch = GetOption(args, "--channel", "-c") ?? VisionConstants.ChannelPointer;
                    var req = new VisionInputRequest
                    {
                        Channel = ch,
                        Action = GetOption(args, "--action", "-a") ?? (ch == VisionConstants.ChannelPointer ? "click" : "press"),
                        X = float.TryParse(GetOption(args, "--x"), out float ix) ? ix : null,
                        Y = float.TryParse(GetOption(args, "--y"), out float iy) ? iy : null,
                        Key = GetOption(args, "--key", "-k"),
                        GamepadButton = GetOption(args, "--button", "-b"),
                        HoldFrames = int.TryParse(GetOption(args, "--hold"), out int hf) ? hf : 6
                    };
                    Console.WriteLine(await workbench.SendInputAsync(req).ConfigureAwait(false));
                    return 0;

                case "get-controls":
                    Console.WriteLine(await workbench.GetControlsAsync(
                        GetPositionalOrOption(args, 1, "--target", "-t"),
                        GetOption(args, "--runner", "-r")).ConfigureAwait(false));
                    return 0;

                case "trigger-action":
                    if (args.Length < 2)
                    {
                        Console.WriteLine("Usage: dotnet run --project src/Candied.Agent -- trigger-action <actionName> [--target path]");
                        return 1;
                    }

                    Console.WriteLine(await workbench.TriggerActionAsync(
                        args[1],
                        GetOption(args, "--target", "-t"),
                        GetOption(args, "--runner", "-r")).ConfigureAwait(false));
                    return 0;

                case "wait-idle":
                    Console.WriteLine(await workbench.WaitIdleAsync().ConfigureAwait(false));
                    return 0;

                case "list-ui":
                    Console.WriteLine(await workbench.ListUiElementsAsync().ConfigureAwait(false));
                    return 0;

                case "scene-info":
                    Console.WriteLine(await workbench.GetSceneInfoAsync().ConfigureAwait(false));
                    return 0;

                case "inspect-scene":
                    Console.WriteLine(await workbench.InspectSceneAsync(
                        GetOption(args, "--name", "-n"),
                        HasFlag(args, "--include-inactive"),
                        HasFlag(args, "--visible-only"),
                        GetOption(args, "--attr")).ConfigureAwait(false));
                    return 0;

                case "inspect-runtime-assets":
                    Console.WriteLine(await workbench.InspectRuntimeAssetsAsync(
                        GetOption(args, "--name", "-n"),
                        GetOption(args, "--type", "-t")).ConfigureAwait(false));
                    return 0;

                case "mcp":
                    break;

                default:
                    if (workbench.GetPlatformTools().Any(t => string.Equals(t.Name, command, StringComparison.OrdinalIgnoreCase)))
                    {
                        var cliMap = ParseAllCliFlags(args);
                        Console.WriteLine(await workbench.ExecutePlatformToolAsync(command, cliMap).ConfigureAwait(false));
                        return 0;
                    }

                    Console.WriteLine($"Unknown command: '{command}'. Run with 'help' for usage.");
                    return 1;
            }
        }

        if (Directory.Exists(workbench.StorageLayout.WorkspaceRoot))
        {
            Directory.SetCurrentDirectory(workbench.StorageLayout.WorkspaceRoot);
        }

        var mcpServer = serviceProvider.GetRequiredService<McpServer>();
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        await mcpServer.RunAsync(Console.In, Console.Out, cts.Token).ConfigureAwait(false);
        return 0;
    }

    private static string? GetPositionalOrOption(string[] args, int index, params string[] flags)
    {
        if (flags.Length > 0)
        {
            string? opt = GetOption(args, flags);
            if (!string.IsNullOrWhiteSpace(opt))
            {
                return opt;
            }
        }

        if (index < args.Length && !args[index].StartsWith('-'))
        {
            return args[index];
        }

        return null;
    }

    private static string? GetOption(string[] args, params string[] flags)
    {
        for (int i = 1; i < args.Length; i++)
        {
            foreach (var flag in flags)
            {
                if (args[i].Equals(flag, StringComparison.OrdinalIgnoreCase))
                {
                    return i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal)
                        ? args[i + 1]
                        : null;
                }
            }
        }

        return null;
    }

    private static bool HasFlag(string[] args, params string[] flags)
    {
        for (int i = 1; i < args.Length; i++)
        {
            foreach (var flag in flags)
            {
                if (args[i].Equals(flag, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static Dictionary<string, string?> ParseAllCliFlags(string[] args)
    {
        var map = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        for (int i = 1; i < args.Length; i++)
        {
            if (args[i].StartsWith("--", StringComparison.Ordinal))
            {
                string key = args[i][2..].Replace("-", string.Empty, StringComparison.Ordinal);
                if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    map[key] = args[i + 1];
                    map[args[i][2..]] = args[i + 1];
                    i++;
                }
                else
                {
                    map[key] = "true";
                    map[args[i][2..]] = "true";
                }
            }
        }

        return map;
    }

    private static void PrintHelp(AgentWorkbenchService workbench)
    {
        Console.WriteLine("""
        ==========================================================================
          Candied Workbench v2.0 - Universal Reverse-Engineering, Vision & Modding
        ==========================================================================

        Usage:
          dotnet run --project src/Candied.Agent -- [command] [arguments]

        Core Commands:
          mcp                                    Run as Model Context Protocol (MCP) stdio server (default)
          inspect <targetPath> [--runner <exe>]  Audit target platform, capabilities, and code modules
          setup [targetPath]                     Provision mod loader and Vision sensor for target
          rollback [targetPath]                  Restore target directory using .candied_manifest.json
          extract-code <moduleName> [targetPath] Extract/decompile module and index into SQLite (FTS5 + Docs)
          query <searchTerm> [targetPath]        Query indexed symbols in SQLite
          search-code <pattern> [targetPath]     Full-text search across extracted code in SQLite
          type <fullTypeName> [targetPath]       Retrieve type definition, signatures, and source
          notes <markdown> [targetPath]          Save reverse-engineering findings to RESEARCH.md
          index-assets [targetPath]              Scan and index target assets into SQLite
          discover-assets [--pattern P]          Query target assets with pagination
          extract-assets [--pattern P]           Extract target assets offline to Zone 3
          scaffold <modName> [targetPath]        Scaffold a mod workspace in mods/<ModName>
          build <projectFilePath> [config]       Compile and deploy a mod project
          logs [logFilePath]                     Read structured runtime logs

        Vision & Input Commands (2-Tier Host + 3-Channel Controls):
          game-launch [targetPath] [--runner R]  Launch & embed game or Emulator+ROM in Candied Studio
          game-close                             Stop hosted window in Candied Studio
          recent-targets                         List recently launched targets
          status                                 Query Studio window & active Vision tier
          capabilities                           Query active Vision & Input capabilities
          screenshot [--out path]                Capture PNG frame (Tier 2 Sensor or Tier 1 Win32)
          send-input --channel C --action A ...  Send pointer, keyboard, or gamepad input
          get-controls [targetPath]              Inspect semantic control mappings (controls.json)
          trigger-action <actionName>            Trigger semantic action (Confirm, Cancel, Pause, etc.)
          wait-idle                              Wait for visual rendering idle
          list-ui                                Enumerate interactive UI elements (Tier 2)
          scene-info                             Query active scene telemetry (Tier 2)
          inspect-scene [--name filter]          Inspect cameras and scene graph nodes (Tier 2)
          inspect-runtime-assets [--name filter] Inspect runtime-loaded assets (Tier 2)
        """);

        var platformTools = workbench.GetPlatformTools();
        if (platformTools.Count > 0)
        {
            Console.WriteLine("        Platform-Specific Tools:");
            foreach (var pt in platformTools)
            {
                Console.WriteLine($"          {pt.Name,-38} [{pt.PlatformId}] {pt.Description}");
            }
        }

        Console.WriteLine("        ==========================================================================");
    }
}
