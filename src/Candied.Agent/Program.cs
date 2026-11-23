using Microsoft.Extensions.DependencyInjection;
using Candied.Agent.Mcp;
using Candied.Core.Ports;
using Candied.Core.UseCases;
using Candied.Decompiler;
using Candied.Workspace;

namespace Candied.Agent;

public static class Program
{
    private static readonly string[] DefaultPublicizedAssemblies = ["Assembly-CSharp"];

    public static async Task<int> Main(string[] args)
    {
        var services = new ServiceCollection();

        // Wire infrastructure adapters into Core ports
        services.AddDecompilerInfrastructure();
        services.AddWorkspaceInfrastructure();

        // Register Core use cases
        services.AddTransient<InspectGameEnvironmentUseCase>();
        services.AddTransient<SetupGameEnvironmentUseCase>();
        services.AddTransient<RollbackGameEnvironmentUseCase>();
        services.AddTransient<DecompileAssemblyUseCase>();
        services.AddTransient<SearchGameSymbolsUseCase>();
        services.AddTransient<SearchGameCodeUseCase>();
        services.AddTransient<SaveResearchNotesUseCase>();
        services.AddTransient<ScaffoldModUseCase>();
        services.AddTransient<AddProjectReferenceUseCase>();
        services.AddTransient<BuildModUseCase>();
        services.AddTransient<ReadLogsUseCase>();
        services.AddTransient<GetTypeDetailUseCase>();
        services.AddTransient<GetGameProcessStatusUseCase>();
        services.AddTransient<LaunchGameUseCase>();
        services.AddTransient<StopGameUseCase>();
        services.AddTransient<DiscoverAssetsUseCase>();
        services.AddTransient<ExtractAssetsUseCase>();
        services.AddTransient<IndexAssetsUseCase>();

        // Register Agent services and MCP server
        services.AddTransient<AgentWorkbenchService>();
        services.AddTransient<McpServer>();

        using var serviceProvider = services.BuildServiceProvider();
        var workbench = serviceProvider.GetRequiredService<AgentWorkbenchService>();

        if (args.Length > 0)
        {
            var command = args[0].ToLowerInvariant();

            switch (command)
            {
                case "--help" or "-h" or "help":
                    PrintHelp();
                    return 0;

                case "inspect":
                    if (args.Length < 2)
                    {
                        Console.WriteLine("Usage: dotnet run --project src/Candied.Agent -- inspect <gameDir>");
                        return 1;
                    }
                    var inspectResponse = await workbench.InspectGameEnvironmentAsync(args[1]).ConfigureAwait(false);
                    Console.WriteLine(inspectResponse);
                    return 0;

                case "setup":
                    if (args.Length < 2)
                    {
                        Console.WriteLine("Usage: dotnet run --project src/Candied.Agent -- setup <gameDir>");
                        return 1;
                    }
                    var setupResponse = await workbench.SetupGameEnvironmentAsync(args[1]).ConfigureAwait(false);
                    Console.WriteLine(setupResponse);
                    return 0;

                case "rollback":
                    if (args.Length < 2)
                    {
                        Console.WriteLine("Usage: dotnet run --project src/Candied.Agent -- rollback <gameDir>");
                        return 1;
                    }
                    var rollbackResponse = await workbench.RollbackGameEnvironmentAsync(args[1]).ConfigureAwait(false);
                    Console.WriteLine(rollbackResponse);
                    return 0;

                case "decompile":
                    if (args.Length < 2)
                    {
                        Console.WriteLine("Usage: dotnet run --project src/Candied.Agent -- decompile <assemblyName> [gameDir]");
                        return 1;
                    }
                    var asmName = args[1];
                    var gDir = args.Length > 2 ? args[2] : null;
                    var decompileResponse = await workbench.DecompileAssemblyAsync(gDir, asmName).ConfigureAwait(false);
                    Console.WriteLine(decompileResponse);
                    return 0;

                case "query":
                case "search_symbols":
                    if (args.Length < 2)
                    {
                        Console.WriteLine("Usage: dotnet run --project src/Candied.Agent -- query <searchTerm> [gameDir] [--kind K] [--namespace NS] [--limit N]");
                        return 1;
                    }
                    var queryTerm = args[1];
                    var queryGameDir = GetOption(args, "--dir", "--game-dir", "-d")
                        ?? (args.Length > 2 && !args[2].StartsWith("--", StringComparison.Ordinal) ? args[2] : null);
                    var queryKind = GetOption(args, "--kind", "-k");
                    var queryNs = GetOption(args, "--namespace", "--ns");
                    var queryLimit = int.TryParse(GetOption(args, "--limit", "--max", "-m"), out var ql) ? ql : 25;
                    var queryResponse = await workbench.SearchGameSymbolsOnlyAsync(queryTerm, queryGameDir, queryKind, queryNs, queryLimit).ConfigureAwait(false);
                    Console.WriteLine(queryResponse);
                    return 0;

                case "search-code":
                case "search_code":
                    if (args.Length < 2)
                    {
                        Console.WriteLine("Usage: dotnet run --project src/Candied.Agent -- search-code <pattern> [--dir gameDir] [--namespace NS] [--assembly ASM] [--limit N]");
                        return 1;
                    }
                    var codePattern = args[1];
                    var codeGameDir = GetOption(args, "--dir", "--game-dir", "-d")
                        ?? (args.Length > 2 && !args[2].StartsWith("--", StringComparison.Ordinal) ? args[2] : null);
                    var codeNs = GetOption(args, "--namespace", "--ns");
                    var codeAsm = GetOption(args, "--assembly", "-a");
                    var codeLimit = int.TryParse(GetOption(args, "--limit", "--max", "-m"), out var cl) ? cl : 25;
                    var codeResponse = await workbench.SearchGameCodeAsync(codePattern, codeGameDir, codeNs, codeAsm, codeLimit).ConfigureAwait(false);
                    Console.WriteLine(codeResponse);
                    return 0;

                case "type":
                case "detail":
                    if (args.Length < 2)
                    {
                        Console.WriteLine("Usage: dotnet run --project src/Candied.Agent -- type <fullTypeName> [gameDir] [--signatures-only] [--member M]");
                        return 1;
                    }
                    var typeName = args[1];
                    var typeGameDir = GetOption(args, "--dir", "--game-dir", "-d")
                        ?? (args.Length > 2 && !args[2].StartsWith("--", StringComparison.Ordinal) ? args[2] : null);
                    var typeSigOnly = HasFlag(args, "--signatures-only", "--signatures");
                    var typeMemberFilter = GetOption(args, "--member", "--member-filter", "-m");
                    var typeResponse = await workbench.GetTypeDetailAsync(typeName, typeGameDir, typeSigOnly, typeMemberFilter).ConfigureAwait(false);
                    Console.WriteLine(typeResponse);
                    return 0;

                case "notes":
                case "research":
                    if (args.Length < 2)
                    {
                        Console.WriteLine("Usage: dotnet run --project src/Candied.Agent -- notes <notes> [gameDir]");
                        return 1;
                    }
                    var notesContent = args[1];
                    var notesGameDir = args.Length > 2 ? args[2] : null;
                    var notesResponse = await workbench.SaveResearchNotesAsync(notesContent, notesGameDir).ConfigureAwait(false);
                    Console.WriteLine(notesResponse);
                    return 0;

                case "scaffold":
                    if (args.Length < 3)
                    {
                        Console.WriteLine("Usage: dotnet run --project src/Candied.Agent -- scaffold <modName> <gameDir> [outputDir] [--with-assets]");
                        return 1;
                    }
                    var modName = args[1];
                    var gameDir = args[2];
                    var outDir = args.Length > 3 && !args[3].StartsWith("--", StringComparison.Ordinal)
                        ? args[3]
                        : string.Empty;
                    var withAssets = args.Any(a => a.Equals("--with-assets", StringComparison.OrdinalIgnoreCase));
                    var guid = $"com.author.{modName.ToLowerInvariant()}";
                    var scaffoldResponse = await workbench.ScaffoldModByPathAsync(
                        modName, guid, "1.0.0", outDir, gameDir, DefaultPublicizedAssemblies, withAssets).ConfigureAwait(false);
                    Console.WriteLine(scaffoldResponse);
                    return 0;

                case "add-reference":
                case "add_reference":
                    if (args.Length < 3)
                    {
                        Console.WriteLine("Usage: dotnet run --project src/Candied.Agent -- add-reference <csprojPath> <assemblyName> [--publicize] [--dir gameDir]");
                        return 1;
                    }
                    var refProjPath = args[1];
                    var refAsmName = args[2];
                    var refPublicize = HasFlag(args, "--publicize", "-p");
                    var refGameDir = GetOption(args, "--dir", "--game-dir", "-d");
                    var refResp = await workbench.AddProjectReferenceAsync(refProjPath, refAsmName, refPublicize, refGameDir).ConfigureAwait(false);
                    Console.WriteLine(refResp);
                    return 0;

                case "build":
                    if (args.Length < 2)
                    {
                        Console.WriteLine("Usage: dotnet run --project src/Candied.Agent -- build <projectFilePath> [configuration]");
                        return 1;
                    }
                    var projectPath = args[1];
                    var config = args.Length > 2 ? args[2] : "Debug";
                    var buildResponse = await workbench.BuildModAsync(projectPath, config).ConfigureAwait(false);
                    Console.WriteLine(buildResponse);
                    return 0;

                case "logs":
                    var logPath = args.Length > 1 && !args[1].StartsWith("--", StringComparison.Ordinal) ? args[1] : null;
                    var maxLines = int.TryParse(GetOption(args, "--max", "-m"), out var ml)
                        ? ml
                        : (args.Length > 2 && int.TryParse(args[2], out var m) ? m : 50);
                    var logsResponse = await workbench.ReadLogsAsync(logPath, maxLines).ConfigureAwait(false);
                    Console.WriteLine(logsResponse);
                    return 0;

                case "status":
                case "game-status":
                    var statusGameDir = args.Length > 1 ? args[1] : null;
                    var statusResponse = await workbench.GetGameProcessStatusAsync(statusGameDir).ConfigureAwait(false);
                    Console.WriteLine(statusResponse);
                    return 0;

                case "launch":
                    if (args.Length < 2)
                    {
                        Console.WriteLine("Usage: dotnet run --project src/Candied.Agent -- launch <gameDir> [--fullscreen] [--width W] [--height H]");
                        return 1;
                    }
                    var launchGameDir = args[1];
                    var isFullscreen = args.Any(a => a.Equals("--fullscreen", StringComparison.OrdinalIgnoreCase));
                    var launchWidth = 1280;
                    var launchHeight = 720;
                    for (var i = 2; i < args.Length - 1; i++)
                    {
                        if (args[i].Equals("--width", StringComparison.OrdinalIgnoreCase) && int.TryParse(args[i + 1], out var w))
                        {
                            launchWidth = w;
                        }
                        else if (args[i].Equals("--height", StringComparison.OrdinalIgnoreCase) && int.TryParse(args[i + 1], out var h))
                        {
                            launchHeight = h;
                        }
                    }
                    var launchResponse = await workbench.LaunchGameAsync(launchGameDir, !isFullscreen, launchWidth, launchHeight).ConfigureAwait(false);
                    Console.WriteLine(launchResponse);
                    return 0;

                case "stop":
                    var stopGameDir = args.Length > 1 ? args[1] : null;
                    var stopResponse = await workbench.StopGameAsync(stopGameDir).ConfigureAwait(false);
                    Console.WriteLine(stopResponse);
                    return 0;

                case "index-assets":
                    var idxGameDir = GetOption(args, "--dir", "--game-dir", "-d")
                        ?? (args.Length > 1 && !args[1].StartsWith("--", StringComparison.Ordinal) ? args[1] : null);
                    var idxResponse = await workbench.IndexGameAssetsAsync(idxGameDir).ConfigureAwait(false);
                    Console.WriteLine(idxResponse);
                    return 0;

                case "discover-assets":
                case "find-assets":
                    bool discPositional = args.Length > 1 && !args[1].StartsWith('-');
                    var discPattern = GetOption(args, "--pattern", "-p")
                        ?? (discPositional ? args[1] : null);
                    var discType = GetOption(args, "--type", "-t")
                        ?? (discPositional && args.Length > 2 && !args[2].StartsWith('-') && !int.TryParse(args[2], out _) ? args[2] : null);
                    var discMaxStr = GetOption(args, "--max", "--limit", "-m");
                    var discMax = int.TryParse(discMaxStr, out var parsedDiscMax)
                        ? parsedDiscMax
                        : (discPositional && args.Length > 3 && int.TryParse(args[3], out var dm) ? dm : 50);
                    var discOffset = int.TryParse(GetOption(args, "--offset"), out var parsedDiscOffset) ? parsedDiscOffset : 0;
                    var discGameDir = GetOption(args, "--dir", "--game-dir", "-d")
                        ?? (discPositional && args.Length > 4 && !args[4].StartsWith('-') ? args[4] : null);
                    var discContainer = GetOption(args, "--container", "-c");
                    long? discPathId = long.TryParse(GetOption(args, "--path-id", "--id"), out var dpid) ? dpid : null;

                    var discResponse = await workbench.DiscoverGameAssetsAsync(
                        discPattern, discType, discMax, discGameDir, discContainer, discPathId, discOffset).ConfigureAwait(false);
                    Console.WriteLine(discResponse);
                    return 0;

                case "extract-assets":
                    bool extPositional = args.Length > 1 && !args[1].StartsWith('-');
                    var extPattern = GetOption(args, "--pattern", "-p")
                        ?? (extPositional ? args[1] : null);
                    var extType = GetOption(args, "--type", "-t")
                        ?? (extPositional && args.Length > 2 && !args[2].StartsWith('-') && !int.TryParse(args[2], out _) ? args[2] : null);
                    var extMaxStr = GetOption(args, "--max", "--limit", "-m");
                    var extMax = int.TryParse(extMaxStr, out var parsedExtMax)
                        ? parsedExtMax
                        : (extPositional && args.Length > 3 && int.TryParse(args[3], out var em) ? em : 50);
                    var extGameDir = GetOption(args, "--dir", "--game-dir", "-d")
                        ?? (extPositional && args.Length > 4 && !args[4].StartsWith('-') ? args[4] : null);
                    var extOutDir = GetOption(args, "--out", "--output", "-o")
                        ?? (extPositional && args.Length > 5 && !args[5].StartsWith('-') ? args[5] : null);
                    var extContainer = GetOption(args, "--container", "-c");
                    long? extPathId = long.TryParse(GetOption(args, "--path-id", "--id"), out var epid) ? epid : null;
                    bool disambiguate = !HasFlag(args, "--no-disambiguate");

                    var extResponse = await workbench.ExtractGameAssetsAsync(
                        extPattern, extType, extMax, extOutDir, extGameDir, extContainer, extPathId, disambiguate).ConfigureAwait(false);
                    Console.WriteLine(extResponse);
                    return 0;

                case "capabilities":
                case "game-capabilities":
                    var capPort = int.TryParse(GetOption(args, "--port", "-p"), out var cp) ? cp : (int?)null;
                    var capToken = GetOption(args, "--token", "-t");
                    var capResp = await workbench.GetGameCapabilitiesAsync(capPort, capToken).ConfigureAwait(false);
                    Console.WriteLine(capResp);
                    return 0;

                case "screenshot":
                case "game-screenshot":
                    var scPort = int.TryParse(GetOption(args, "--port", "-p"), out var scp) ? scp : (int?)null;
                    var scToken = GetOption(args, "--token", "-t");
                    var scOut = GetOption(args, "--out", "-o");
                    var scResp = await workbench.CaptureGameScreenshotAsync(scPort, scToken, scOut).ConfigureAwait(false);
                    Console.WriteLine(scResp);
                    return 0;

                case "click":
                case "game-click":
                    if (args.Length < 3 || !float.TryParse(args[1], out var cx) || !float.TryParse(args[2], out var cy))
                    {
                        Console.WriteLine("Usage: dotnet run --project src/Candied.Agent -- click <x> <y> [--button 0] [--hold 6] [--port P] [--token T]");
                        return 1;
                    }
                    var clkBtn = int.TryParse(GetOption(args, "--button", "-b"), out var cb) ? cb : 0;
                    var clkHold = int.TryParse(GetOption(args, "--hold", "-h"), out var ch) ? ch : 6;
                    var clkPort = int.TryParse(GetOption(args, "--port", "-p"), out var clkp) ? clkp : (int?)null;
                    var clkToken = GetOption(args, "--token", "-t");
                    var clkResp = await workbench.ClickGameAtAsync(cx, cy, clkBtn, clkHold, null, clkPort, clkToken).ConfigureAwait(false);
                    Console.WriteLine(clkResp);
                    return 0;

                case "wait-idle":
                case "game-wait-idle":
                    var wiMax = int.TryParse(GetOption(args, "--max", "-m"), out var wim) ? wim : 10;
                    var wiTimeout = int.TryParse(GetOption(args, "--timeout", "-t"), out var wit) ? wit : 3000;
                    var wiPort = int.TryParse(GetOption(args, "--port", "-p"), out var wip) ? wip : (int?)null;
                    var wiToken = GetOption(args, "--token");
                    var wiResp = await workbench.WaitGameIdleAsync(wiMax, wiTimeout, wiPort, wiToken).ConfigureAwait(false);
                    Console.WriteLine(wiResp);
                    return 0;

                case "scene-info":
                case "game-scene-info":
                    var siPort = int.TryParse(GetOption(args, "--port", "-p"), out var sip) ? sip : (int?)null;
                    var siToken = GetOption(args, "--token", "-t");
                    var siResp = await workbench.GetGameSceneInfoAsync(siPort, siToken).ConfigureAwait(false);
                    Console.WriteLine(siResp);
                    return 0;

                case "inspect-scene-objects":
                case "game-inspect-scene-objects":
                    var isoNameFilter = GetOption(args, "--name", "-n");
                    var isoPort = int.TryParse(GetOption(args, "--port", "-p"), out var isop) ? isop : (int?)null;
                    var isoToken = GetOption(args, "--token", "-t");
                    var isoIncludeInactive = HasFlag(args, "--include-inactive", "--inactive");
                    var isoVisibleOnly = HasFlag(args, "--visible-only", "--visible");
                    var isoSortingLayer = GetOption(args, "--sorting-layer", "--layer");
                    var isoMaxResults = int.TryParse(GetOption(args, "--max", "--limit", "-m"), out var isom) ? isom : 100;
                    var isoResp = await workbench.InspectSceneObjectsAsync(
                        isoNameFilter, isoPort, isoToken, isoIncludeInactive, isoVisibleOnly, isoSortingLayer, isoMaxResults).ConfigureAwait(false);
                    Console.WriteLine(isoResp);
                    return 0;

                case "inspect-runtime-assets":
                case "runtime-assets":
                    var iraFilter = GetOption(args, "--name", "--pattern", "-n");
                    var iraType = GetOption(args, "--type", "-t");
                    var iraMax = int.TryParse(GetOption(args, "--max", "--limit", "-m"), out var iram) ? iram : 50;
                    var iraPort = int.TryParse(GetOption(args, "--port", "-p"), out var irap) ? irap : (int?)null;
                    var iraToken = GetOption(args, "--token");
                    var iraResp = await workbench.InspectRuntimeAssetsAsync(iraFilter, iraType, iraMax, iraPort, iraToken).ConfigureAwait(false);
                    Console.WriteLine(iraResp);
                    return 0;

                case "list-ui":
                case "game-list-ui":
                    var uiPort = int.TryParse(GetOption(args, "--port", "-p"), out var uip) ? uip : (int?)null;
                    var uiToken = GetOption(args, "--token", "-t");
                    var uiResp = await workbench.ListGameUiElementsAsync(uiPort, uiToken).ConfigureAwait(false);
                    Console.WriteLine(uiResp);
                    return 0;

                case "list-games":
                case "game-list-games":
                    var listGamesResp = await workbench.ListRegisteredGamesAsync().ConfigureAwait(false);
                    Console.WriteLine(listGamesResp);
                    return 0;

                case "register-game":
                    if (args.Length < 3)
                    {
                        Console.WriteLine("Usage: dotnet run --project src/Candied.Agent -- register-game <name> <executablePath>");
                        return 1;
                    }
                    var regName = args[1];
                    var regPath = args[2];
                    var regResp = await workbench.RegisterGameAsync(regName, regPath).ConfigureAwait(false);
                    Console.WriteLine(regResp);
                    return 0;

                case "unregister-game":
                    if (args.Length < 2)
                    {
                        Console.WriteLine("Usage: dotnet run --project src/Candied.Agent -- unregister-game <name>");
                        return 1;
                    }
                    var unregName = args[1];
                    var unregResp = await workbench.UnregisterGameAsync(unregName).ConfigureAwait(false);
                    Console.WriteLine(unregResp);
                    return 0;

                case "install-bridge":
                    if (args.Length < 2)
                    {
                        Console.WriteLine("Usage: dotnet run --project src/Candied.Agent -- install-bridge <name>");
                        return 1;
                    }
                    var instName = args[1];
                    var instResp = await workbench.InstallBridgeAsync(instName).ConfigureAwait(false);
                    Console.WriteLine(instResp);
                    return 0;

                case "game-launch":
                    if (args.Length < 2)
                    {
                        Console.WriteLine("Usage: dotnet run --project src/Candied.Agent -- game-launch <name> [--wait-for-log <text>]");
                        return 1;
                    }
                    var glName = args[1];
                    var glWaitForLog = GetOption(args, "--wait-for-log", "--log");
                    var glResp = await workbench.LaunchGameByNameAsync(glName, glWaitForLog).ConfigureAwait(false);
                    Console.WriteLine(glResp);
                    return 0;

                case "game-close":
                    var gcResp = await workbench.CloseGameAsync().ConfigureAwait(false);
                    Console.WriteLine(gcResp);
                    return 0;

                case "mcp":
                    // Explicit MCP mode
                    break;

                default:
                    Console.WriteLine($"Unknown command: '{command}'. Use 'help' to see available commands.");
                    return 1;
            }
        }

        // Default or 'mcp': Run as Model Context Protocol (MCP) stdio server
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

    private static void PrintHelp()
    {
        Console.WriteLine("""
==========================================================================
  Candied Workbench - Modding & Agentic Orchestration Tool for Unity
==========================================================================

Usage:
  dotnet run --project src/Candied.Agent -- [command] [arguments]

Commands:
  mcp                                    Run as Model Context Protocol (MCP) stdio server (default)
  inspect <gameDir>                      Audit game environment without decompilation (BepInEx, .NET, assemblies)
  setup <gameDir>                        Automatically provision BepInEx & configure logging with file manifest
  rollback <gameDir>                     Safely restore game directory to virgin state using .candied_manifest.json
  decompile <assemblyName> [gameDir]     Targeted single-assembly decompilation with caching & SQLite indexing
  query <searchTerm> [gameDir]           Fast symbol query against SQLite index without decompilation
  type <fullTypeName> [gameDir]          Retrieve decompiled class detail (base class, fields, methods)
  notes <notes> [gameDir]                Save agent research findings to RESEARCH.md
  status [gameDir]                       Check running game process and IPC companion bridge status
  launch <gameDir> [options]             Launch game in windowed mode (-screen-fullscreen 0 -popupwindow)
  stop [gameDir]                         Gracefully stop running game process
  index-assets [options]                 Scan & index all game assets into SQLite index (<5ms future queries)
  discover-assets [options]              Discover Sprites, Textures, TextAssets, MonoBehaviours offline
  extract-assets [options]               Extract assets offline directly to PNG/JSON/TXT files in Zone 3
  scaffold <modName> <gameDir> [outDir]  Scaffold a ready-to-compile BepInEx 5 plugin with publicizer
  build <csprojPath> [config]            Build a mod project and display structured diagnostics
  logs <logFilePath> [maxLines]          Read and parse runtime BepInEx LogOutput.log
  list-games                             List registered games in the allowlist
  register-game <name> <exePath>         Register a Unity Mono game to the allowlist (rejects IL2CPP)
  unregister-game <name>                 Remove a game from the allowlist
  install-bridge <name>                  Install Candied.VisionBridge into a registered game
  game-launch <name>                     Launch a registered game via Candied Studio
  game-close                             Close active game running in Candied Studio
  help, --help, -h                       Display this help message

Options for asset commands:
  --dir, -d <path>                       Target game installation directory
  --pattern, -p <glob/substring>         Name search pattern or substring
  --type, -t <Sprite|Texture2D|TextAsset|MonoBehaviour|All> Filter asset type
  --limit, --max, -m <n>                 Maximum assets to process
  --container, -c <name>                 Filter by container archive name
  --path-id, --id <id>                   Filter by exact Unity PathID
  --out, -o <path>                       Output directory for extracted assets
  --no-disambiguate                      Do not append PathID when filename collisions occur

Examples:
  dotnet run --project src/Candied.Agent -- inspect "C:\Games\GameName"
  dotnet run --project src/Candied.Agent -- index-assets --dir "C:\Games\GameName"
  dotnet run --project src/Candied.Agent -- discover-assets --pattern "Avatar" --type "Sprite"
  dotnet run --project src/Candied.Agent -- extract-assets --pattern "Jesse" --type "Sprite"
  dotnet run --project src/Candied.Agent -- extract-assets --type "TextAsset" --out "./assets/dialogue"
==========================================================================
""");
    }
}
