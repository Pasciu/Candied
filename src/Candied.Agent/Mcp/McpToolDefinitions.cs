using System.Collections.Generic;
using Candied.Core.Models;

namespace Candied.Agent.Mcp;

internal static class McpToolDefinitions
{
    private static readonly string[] ReqTargetPath = ["targetPath"];
    private static readonly string[] ReqModuleName = ["moduleName"];
    private static readonly string[] ReqSearchTerm = ["searchTerm"];
    private static readonly string[] ReqPattern = ["pattern"];
    private static readonly string[] ReqFullTypeName = ["fullTypeName"];
    private static readonly string[] ReqNotes = ["notes"];
    private static readonly string[] ReqModName = ["modName"];
    private static readonly string[] ReqProjectFilePath = ["projectFilePath"];
    private static readonly string[] ReqActions = ["actions"];
    private static readonly string[] ReqActionAndChannel = ["actionName", "channel"];
    private static readonly string[] ReqActionName = ["actionName"];

    private static readonly string[] SymbolKindEnum = ["all", "types", "members", "class", "struct", "interface", "enum", "method", "field", "property", "event"];
    private static readonly string[] BuildConfigEnum = ["Debug", "Release"];
    private static readonly string[] LogLevelEnum = ["Debug", "Info", "Warning", "Error", "Fatal"];
    private static readonly string[] InputChannelWireEnum = ["pointer", "keyboard", "gamepad"];
    private static readonly string[] InputActionWireEnum = ["click", "move", "down", "up", "press", "stick", "isolate", "release_isolation"];
    private static readonly string[] StickEnum = ["left", "right"];
    private static readonly string[] ControlChannelEnum = ["Mouse", "Keyboard", "Gamepad"];

    private static readonly object[] CoreTools =
    [
        new
        {
            name = "inspect",
            description = "[Step 1] Non-destructively audits any PC game installation or emulated ROM + Runner. Detects target platform, architecture, runtime/engine version, mod loader status, active capabilities, and discovered code modules.",
            inputSchema = new
            {
                type = "object",
                properties = new
                {
                    targetPath = new { type = "string", description = "Path to the target game directory, PC executable, or ROM/ISO file." },
                    runnerPath = new { type = "string", description = "Optional path to an emulator or script runner executable (e.g. pcsx2-qt.exe, dolphin.exe)." }
                },
                required = ReqTargetPath
            }
        },
        new
        {
            name = "setup",
            description = "[Step 1.5] Provisions the appropriate mod loader and in-game Vision sensor for the detected target platform.",
            inputSchema = new
            {
                type = "object",
                properties = new
                {
                    targetPath = new { type = "string", description = "Optional target path. Uses active session if omitted." },
                    runnerPath = new { type = "string", description = "Optional runner/emulator executable path." },
                    forceReinstall = new { type = "boolean", description = "Force re-provisioning even if already installed." },
                    enableConsoleLogging = new { type = "boolean", description = "Configure runtime disk/console logging. Defaults to true." }
                }
            }
        },
        new
        {
            name = "rollback",
            description = "Restores the target directory to its clean state using .candied_manifest.json.",
            inputSchema = new
            {
                type = "object",
                properties = new
                {
                    targetPath = new { type = "string", description = "Optional target path. Uses active session if omitted." },
                    runnerPath = new { type = "string", description = "Optional runner path." }
                }
            }
        },
        new
        {
            name = "extract-code",
            description = "[Step 2] Extracts, decompiles, or dumps a single specified code module and indexes all symbols and source into SQLite (with FTS5 and Knowledge Hub docs).",
            inputSchema = new
            {
                type = "object",
                properties = new
                {
                    moduleName = new { type = "string", description = "Name of the code module to extract (e.g. 'Assembly-CSharp', '<Game>.Core', or binary/script module)." },
                    targetPath = new { type = "string", description = "Optional target path. Uses active session if omitted." },
                    runnerPath = new { type = "string", description = "Optional runner path." }
                },
                required = ReqModuleName
            }
        },
        new
        {
            name = "query",
            description = "[Step 3] Queries indexed symbols (types, classes, structs, enums, methods, fields, properties) from SQLite.",
            inputSchema = new
            {
                type = "object",
                properties = new
                {
                    searchTerm = new { type = "string", description = "Symbol name substring to query." },
                    kind = new { type = "string", @enum = SymbolKindEnum, description = "Optional symbol kind filter. Defaults to 'all'." },
                    namespacePrefix = new { type = "string", description = "Optional namespace/scope prefix filter." },
                    moduleFilter = new { type = "string", description = "Optional code module name filter." },
                    limit = new { type = "integer", description = "Maximum results (1-200). Defaults to 25." },
                    targetPath = new { type = "string", description = "Optional target path or SQLite .db path." }
                },
                required = ReqSearchTerm
            }
        },
        new
        {
            name = "search-code",
            description = "[Step 3.2] Full-text search (FTS5) across extracted code modules to find call sites, constants, and implementation details.",
            inputSchema = new
            {
                type = "object",
                properties = new
                {
                    pattern = new { type = "string", description = "Code substring or token to search for." },
                    namespacePrefix = new { type = "string", description = "Optional namespace prefix filter." },
                    moduleFilter = new { type = "string", description = "Optional module name filter." },
                    maxResults = new { type = "integer", description = "Maximum matching types (1-200). Defaults to 25." },
                    targetPath = new { type = "string", description = "Optional target path." }
                },
                required = ReqPattern
            }
        },
        new
        {
            name = "type",
            description = "[Step 3.5] Retrieves extracted type definition details (inheritance, fields, methods, source file path, and optional source code).",
            inputSchema = new
            {
                type = "object",
                properties = new
                {
                    fullTypeName = new { type = "string", description = "Fully qualified type name." },
                    signaturesOnly = new { type = "boolean", description = "Omit raw source body and return signatures only." },
                    memberFilter = new { type = "string", description = "Substring filter for members and source blocks." },
                    targetPath = new { type = "string", description = "Optional target path." }
                },
                required = ReqFullTypeName
            }
        },
        new
        {
            name = "notes",
            description = "Saves structured reverse-engineering notes to RESEARCH.md in Zone 3.",
            inputSchema = new
            {
                type = "object",
                properties = new
                {
                    notes = new { type = "string", description = "Markdown research notes." },
                    targetPath = new { type = "string", description = "Optional target path." }
                },
                required = ReqNotes
            }
        },
        new
        {
            name = "index-assets",
            description = "[Asset Pipeline] Scans and indexes target game assets into SQLite in Zone 3 for instant offline queries.",
            inputSchema = new
            {
                type = "object",
                properties = new
                {
                    targetPath = new { type = "string", description = "Optional target path." },
                    forceReindex = new { type = "boolean", description = "Clear and rebuild asset index." }
                }
            }
        },
        new
        {
            name = "discover-assets",
            description = "[Asset Pipeline] Queries indexed or on-disk target assets with pagination and filtering.",
            inputSchema = new
            {
                type = "object",
                properties = new
                {
                    searchPattern = new { type = "string", description = "Optional asset name substring." },
                    assetType = new { type = "string", description = "Optional asset type filter (e.g. Sprite, Texture2D, TextAsset, All)." },
                    maxResults = new { type = "integer", description = "Page size (1-500). Defaults to 50." },
                    offset = new { type = "integer", description = "Pagination offset. Defaults to 0." },
                    containerName = new { type = "string", description = "Optional archive/container filter." },
                    assetKey = new { type = "string", description = "Optional exact asset key/ID filter." },
                    targetPath = new { type = "string", description = "Optional target path." }
                }
            }
        },
        new
        {
            name = "extract-assets",
            description = "[Asset Pipeline] Extracts target assets offline into Zone 3 (.candied/games/<Target>/assets/).",
            inputSchema = new
            {
                type = "object",
                properties = new
                {
                    searchPattern = new { type = "string", description = "Optional asset name substring." },
                    assetType = new { type = "string", description = "Optional asset type filter." },
                    maxResults = new { type = "integer", description = "Maximum assets to extract. Defaults to 50." },
                    outputDirectory = new { type = "string", description = "Optional custom output directory." },
                    containerName = new { type = "string", description = "Optional container filter." },
                    assetKey = new { type = "string", description = "Optional exact asset key/ID." },
                    disambiguateCollisions = new { type = "boolean", description = "Disambiguate duplicate asset filenames. Defaults to true." },
                    targetPath = new { type = "string", description = "Optional target path." }
                }
            }
        },
        new
        {
            name = "scaffold",
            description = "[Step 4] Scaffolds a ready-to-build mod workspace in Zone 2 (mods/<ModName>) using the active platform's ModWorkspace capability.",
            inputSchema = new
            {
                type = "object",
                properties = new
                {
                    modName = new { type = "string", description = "PascalCase mod name (e.g. FastCraftMod)." },
                    modId = new { type = "string", description = "Optional reverse-domain mod identifier (e.g. com.author.fastcraft)." },
                    version = new { type = "string", description = "Semantic version. Defaults to '1.0.0'." },
                    outputDirectory = new { type = "string", description = "Optional output directory. Defaults to mods/<ModName>." },
                    targetPath = new { type = "string", description = "Optional target path. Uses active session if omitted." },
                    runnerPath = new { type = "string", description = "Optional runner path." },
                    includeAssetPipeline = new { type = "boolean", description = "Include loose asset loading scaffold. Defaults to false." },
                    platformOptions = new
                    {
                        type = "object",
                        additionalProperties = new { type = "string" },
                        description = "Optional platform-specific key/value options (e.g. {\"publicizedModules\": \"Assembly-CSharp\"})."
                    }
                },
                required = ReqModName
            }
        },
        new
        {
            name = "build",
            description = "[Step 5] Compiles and deploys a mod project, returning structured diagnostics and resolution hints.",
            inputSchema = new
            {
                type = "object",
                properties = new
                {
                    projectFilePath = new { type = "string", description = "Path to the mod project or manifest file." },
                    configuration = new { type = "string", @enum = BuildConfigEnum, description = "Build configuration. Defaults to 'Debug'." }
                },
                required = ReqProjectFilePath
            }
        },
        new
        {
            name = "logs",
            description = "[Step 6] Reads structured runtime logs from the target's mod loader or log file.",
            inputSchema = new
            {
                type = "object",
                properties = new
                {
                    logFilePath = new { type = "string", description = "Optional explicit log file path." },
                    targetPath = new { type = "string", description = "Optional target path." },
                    maxLines = new { type = "integer", description = "Maximum recent lines to tail. Defaults to 200." },
                    minLevel = new { type = "string", @enum = LogLevelEnum, description = "Optional minimum log severity." }
                }
            }
        },
        new
        {
            name = "game-launch",
            description = "[Live Vision Host] Directly launches and embeds any PC game or Emulator + ROM inside Candied Studio (Tier 1 Win32 BaseWindow immediately active; auto-upgrades to Tier 2 SensorBridge if an in-game sensor connects).",
            inputSchema = new
            {
                type = "object",
                properties = new
                {
                    targetPath = new { type = "string", description = "Path to the PC game directory/executable or ROM/ISO file. Uses active session if omitted." },
                    runnerPath = new { type = "string", description = "Optional path to an emulator or runner executable." },
                    extraArguments = new { type = "string", description = "Optional extra CLI arguments." },
                    waitForLog = new { type = "string", description = "Optional log substring to wait for during startup." },
                    stabilityTimeoutSeconds = new { type = "integer", description = "Maximum seconds to wait for frame stability (2-120). Defaults to 30." }
                }
            }
        },
        new
        {
            name = "game-close",
            description = "[Live Vision Host] Stops the currently hosted game or emulator window in Candied Studio.",
            inputSchema = new
            {
                type = "object",
                properties = new { }
            }
        },
        new
        {
            name = "recent-targets",
            description = "Lists recently launched games and ROMs in Candied Studio.",
            inputSchema = new
            {
                type = "object",
                properties = new { }
            }
        },
        new
        {
            name = "status",
            description = "[Live Vision] Returns current Studio window host status, active Vision tier (BaseWindow vs SensorBridge), and target process state.",
            inputSchema = new
            {
                type = "object",
                properties = new
                {
                    targetPath = new { type = "string", description = "Optional target path." }
                }
            }
        },
        new
        {
            name = "capabilities",
            description = "[Live Vision] Queries active Vision & Input capabilities (works in both Tier 2 SensorBridge and Tier 1 BaseWindow).",
            inputSchema = new
            {
                type = "object",
                properties = new
                {
                    port = new { type = "integer", description = "Optional explicit sensor port." },
                    token = new { type = "string", description = "Optional explicit sensor token." }
                }
            }
        },
        new
        {
            name = "screenshot",
            description = "[Live Vision] Captures a PNG frame of the running target (via Tier 2 SensorBridge or Tier 1 Win32 BaseWindow) and saves it to Zone 3.",
            inputSchema = new
            {
                type = "object",
                properties = new
                {
                    savePath = new { type = "string", description = "Optional custom output PNG path." },
                    targetPath = new { type = "string", description = "Optional target path." },
                    port = new { type = "integer", description = "Optional explicit sensor port." },
                    token = new { type = "string", description = "Optional explicit sensor token." }
                }
            }
        },
        new
        {
            name = "send-input",
            description = "[Live Interaction - 3-Channel] Sends input across Pointer (click/move/isolate), Keyboard (press/down/up), or Gamepad (press/down/up/stick). Pointer clicks automatically return an annotated screenshot showing the hit point.",
            inputSchema = new
            {
                type = "object",
                properties = new
                {
                    channel = new { type = "string", @enum = InputChannelWireEnum, description = "Input channel ('pointer', 'keyboard', or 'gamepad'). Defaults to 'pointer'." },
                    action = new { type = "string", @enum = InputActionWireEnum, description = "Input action. Defaults to 'click' for pointer or 'press' for keyboard/gamepad." },
                    x = new { type = "number", description = "Pointer X coordinate in screenshot space (origin top-left)." },
                    y = new { type = "number", description = "Pointer Y coordinate in screenshot space (origin top-left)." },
                    button = new { type = "integer", description = "Mouse button (0=left, 1=right, 2=middle)." },
                    key = new { type = "string", description = "Keyboard key name (e.g. 'Enter', 'Escape', 'Space', 'W', 'UpArrow', 'F1')." },
                    gamepadButton = new { type = "string", description = "Gamepad button (e.g. 'South', 'East', 'West', 'North', 'Start', 'Select', 'DPadUp', 'LeftShoulder')." },
                    stick = new { type = "string", @enum = StickEnum, description = "Analog stick selector." },
                    stickX = new { type = "number", description = "Analog stick horizontal axis (-1.0 to 1.0)." },
                    stickY = new { type = "number", description = "Analog stick vertical axis (-1.0 to 1.0)." },
                    holdFrames = new { type = "integer", description = "Number of frames to hold the input. Defaults to 6." },
                    method = new { type = "string", description = "Optional sensor-specific injection method override." },
                    port = new { type = "integer", description = "Optional explicit sensor port." },
                    token = new { type = "string", description = "Optional explicit sensor token." }
                }
            }
        },
        new
        {
            name = "get-controls",
            description = "[Control Mapping] Reads the active GameControlProfile (controls.json) and semantic action bindings for the target.",
            inputSchema = new
            {
                type = "object",
                properties = new
                {
                    targetPath = new { type = "string", description = "Optional target path." }
                }
            }
        },
        new
        {
            name = "set-controls",
            description = "[Control Mapping] Registers or updates semantic game control bindings (e.g. 'Inventory' -> Key 'I' / Gamepad 'North') in controls.json.",
            inputSchema = new
            {
                type = "object",
                properties = new
                {
                    actions = new
                    {
                        type = "array",
                        description = "List of semantic actions to save.",
                        items = new
                        {
                            type = "object",
                            properties = new
                            {
                                actionName = new { type = "string", description = "Semantic identifier (e.g. 'Confirm', 'Jump', 'Inventory')." },
                                displayName = new { type = "string", description = "Human-readable label." },
                                channel = new { type = "string", @enum = ControlChannelEnum, description = "Input channel." },
                                key = new { type = "string", description = "Keyboard key if channel is Keyboard." },
                                gamepadButton = new { type = "string", description = "Gamepad button if channel is Gamepad." },
                                mouseButton = new { type = "integer", description = "Mouse button if channel is Mouse." },
                                normalizedX = new { type = "number", description = "Optional normalized X (0..1) for fixed UI screen tap." },
                                normalizedY = new { type = "number", description = "Optional normalized Y (0..1) for fixed UI screen tap." },
                                holdFrames = new { type = "integer", description = "Hold duration in frames. Defaults to 6." }
                            },
                            required = ReqActionAndChannel
                        }
                    },
                    targetPath = new { type = "string", description = "Optional target path." }
                },
                required = ReqActions
            }
        },
        new
        {
            name = "trigger-action",
            description = "[Control Mapping] Triggers a semantic game action (e.g. 'Confirm', 'Cancel', 'Pause', or custom actions from controls.json) using the optimal active input channel.",
            inputSchema = new
            {
                type = "object",
                properties = new
                {
                    actionName = new { type = "string", description = "Semantic action name to trigger." },
                    targetPath = new { type = "string", description = "Optional target path." },
                    port = new { type = "integer", description = "Optional explicit sensor port." },
                    token = new { type = "string", description = "Optional explicit sensor token." }
                },
                required = ReqActionName
            }
        },
        new
        {
            name = "wait-idle",
            description = "[Live Interaction] Waits for visual rendering stabilization (via Tier 2 SensorBridge or Tier 1 frame-diff).",
            inputSchema = new
            {
                type = "object",
                properties = new
                {
                    maxFrames = new { type = "integer", description = "Max frames to wait. Defaults to 10." },
                    timeoutMs = new { type = "integer", description = "Timeout in ms. Defaults to 3000." },
                    port = new { type = "integer", description = "Optional explicit sensor port." },
                    token = new { type = "string", description = "Optional explicit sensor token." }
                }
            }
        },
        new
        {
            name = "list-ui",
            description = "[Live Perception - Tier 2] Lists interactive UI elements exposed by the connected in-game Vision sensor.",
            inputSchema = new
            {
                type = "object",
                properties = new
                {
                    port = new { type = "integer", description = "Optional explicit sensor port." },
                    token = new { type = "string", description = "Optional explicit sensor token." }
                }
            }
        },
        new
        {
            name = "scene-info",
            description = "[Live Perception - Tier 2] Retrieves active scene metadata from the connected in-game Vision sensor.",
            inputSchema = new
            {
                type = "object",
                properties = new
                {
                    port = new { type = "integer", description = "Optional explicit sensor port." },
                    token = new { type = "string", description = "Optional explicit sensor token." }
                }
            }
        },
        new
        {
            name = "inspect-scene",
            description = "[Live Perception - Tier 2] Inspects active cameras and generic scene graph nodes (hierarchy, bounds, components, engine attributes) via the connected Vision sensor.",
            inputSchema = new
            {
                type = "object",
                properties = new
                {
                    nameFilter = new { type = "string", description = "Optional node/camera name substring filter." },
                    includeInactive = new { type = "boolean", description = "Include inactive scene nodes. Defaults to false." },
                    visibleOnly = new { type = "boolean", description = "Return only visible nodes. Defaults to false." },
                    attributeFilter = new { type = "string", description = "Optional attribute substring filter (e.g. sorting layer or shader)." },
                    maxResults = new { type = "integer", description = "Maximum nodes to return (1-1000). Defaults to 100." },
                    port = new { type = "integer", description = "Optional explicit sensor port." },
                    token = new { type = "string", description = "Optional explicit sensor token." }
                }
            }
        },
        new
        {
            name = "inspect-runtime-assets",
            description = "[Live Perception - Tier 2] Inspects runtime-loaded asset containers and in-memory assets via the connected Vision sensor.",
            inputSchema = new
            {
                type = "object",
                properties = new
                {
                    nameFilter = new { type = "string", description = "Optional asset name substring filter." },
                    assetType = new { type = "string", description = "Optional asset type filter. Defaults to 'All'." },
                    maxResults = new { type = "integer", description = "Maximum loaded assets to return (1-500). Defaults to 50." },
                    port = new { type = "integer", description = "Optional explicit sensor port." },
                    token = new { type = "string", description = "Optional explicit sensor token." }
                }
            }
        }
    ];

    public static object GetAllTools(IReadOnlyList<PlatformToolDefinition>? platformTools = null)
    {
        var tools = new List<object>(CoreTools);

        if (platformTools != null)
        {
            foreach (var pt in platformTools)
            {
                var props = new Dictionary<string, object>();
                var reqs = new List<string>();

                foreach (var param in pt.Parameters)
                {
                    props[param.Name] = new
                    {
                        type = param.Type,
                        description = param.Description
                    };
                    if (param.Required)
                    {
                        reqs.Add(param.Name);
                    }
                }

                tools.Add(new
                {
                    name = pt.Name,
                    description = pt.Description,
                    inputSchema = new
                    {
                        type = "object",
                        properties = props,
                        required = reqs.ToArray()
                    }
                });
            }
        }

        return new { tools };
    }
}
