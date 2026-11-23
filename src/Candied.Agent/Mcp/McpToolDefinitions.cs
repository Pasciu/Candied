namespace Candied.Agent.Mcp;

internal static class McpToolDefinitions
{
    public static object GetAllTools()
    {
        return new
        {
            tools = new object[]
            {
                new
                {
                    name = "inspect",
                    description = "[Step 1 of Modding Pipeline] Non-destructively audits a Unity game directory without decompilation. Detects Unity engine (Mono vs IL2CPP), architecture (x64/x86), BepInEx installation & version, target .NET framework, and returns a catalog of discovered assemblies classified by relevance tier (CoreGame, GameModular, AuxiliaryOrThirdParty). Always call this first.",
                    inputSchema = new
                    {
                        type = "object",
                        properties = new
                        {
                            gameDirectory = new
                            {
                                type = "string",
                                description = "Absolute path to the Unity game root directory (containing the executable and *_Data folder)."
                            }
                        },
                        required = new[] { "gameDirectory" }
                    }
                },
                new
                {
                    name = "setup",
                    description = "[Step 1.5 of Modding Pipeline] Provisions and configures BepInEx mod loader for the target Unity game based on detected architecture (x64/x86). Call this if inspect indicates BepInEx is missing or misconfigured.",
                    inputSchema = new
                    {
                        type = "object",
                        properties = new
                        {
                            gameDirectory = new
                            {
                                type = "string",
                                description = "Optional path to the game root directory. If omitted, uses the active game session."
                            },
                            forceReinstall = new
                            {
                                type = "boolean",
                                description = "Whether to force re-download and re-extract BepInEx even if already present. Defaults to false."
                            },
                            enableConsoleLogging = new
                            {
                                type = "boolean",
                                description = "Whether to pre-configure BepInEx to output logs to console and disk. Defaults to true."
                            }
                        }
                    }
                },
                new
                {
                    name = "rollback",
                    description = "Safely restores the game directory to its original state by removing all files and directories tracked in .candied_manifest.json.",
                    inputSchema = new
                    {
                        type = "object",
                        properties = new
                        {
                            gameDirectory = new
                            {
                                type = "string",
                                description = "Optional path to the game root directory. If omitted, uses the active game session."
                            }
                        }
                    }
                },
                new
                {
                    name = "decompile",
                    description = "[Step 2 of Modding Pipeline] Decompiles a single specified game assembly (.dll) using ILSpy with intelligent file caching, and incrementally indexes its symbols into SQLite without blocking on unneeded assemblies. Call this on the primary CoreGame assembly discovered in Step 1.",
                    inputSchema = new
                    {
                        type = "object",
                        properties = new
                        {
                            assemblyName = new
                            {
                                type = "string",
                                description = "Name of the target assembly to decompile (e.g. 'GameAssembly.Core' or 'Assembly-CSharp')."
                            },
                            gameDirectory = new
                            {
                                type = "string",
                                description = "Optional path to the game root directory. If omitted, uses the active game session."
                            }
                        },
                        required = new[] { "assemblyName" }
                    }
                },
                new
                {
                    name = "query",
                    description = "[Step 3 of Modding Pipeline] Searches symbols (classes, structs, interfaces, enums, methods, fields, properties) matching a search term in the SQLite index populated by decompiled assemblies.",
                    inputSchema = new
                    {
                        type = "object",
                        properties = new
                        {
                            searchTerm = new
                            {
                                type = "string",
                                description = "Class, method, or field name substring to query (e.g. 'PlayerController', 'Speed')."
                            },
                            kind = new
                            {
                                type = "string",
                                @enum = new[] { "all", "types", "members", "class", "struct", "interface", "enum", "method", "field", "property", "event" },
                                description = "Optional symbol kind filter. Defaults to 'all'."
                            },
                            namespacePrefix = new
                            {
                                type = "string",
                                description = "Optional namespace prefix filter (e.g. 'GameLogic.UI')."
                            },
                            limit = new
                            {
                                type = "integer",
                                description = "Maximum number of results to return (1-200). Defaults to 25."
                            },
                            gameDirectory = new
                            {
                                type = "string",
                                description = "Optional path to the game root directory or database path. If omitted, uses the active game session."
                            }
                        },
                        required = new[] { "searchTerm" }
                    }
                },
                new
                {
                    name = "search-code",
                    description = "[Step 3.2 of Modding Pipeline] Full-text search across decompiled C# source code in SQLite (FTS5) to locate call sites, constants, strings, and implementation details without manually grepping Zone 3 files.",
                    inputSchema = new
                    {
                        type = "object",
                        properties = new
                        {
                            pattern = new
                            {
                                type = "string",
                                description = "Case-insensitive code substring to search for inside decompiled type sources (e.g. 'PlayAnimation(', 'SortingLayer')."
                            },
                            namespacePrefix = new
                            {
                                type = "string",
                                description = "Optional namespace prefix filter."
                            },
                            assemblyName = new
                            {
                                type = "string",
                                description = "Optional assembly name filter (e.g. 'Assembly-CSharp')."
                            },
                            maxResults = new
                            {
                                type = "integer",
                                description = "Maximum number of matching types to return (1-200). Defaults to 25."
                            },
                            gameDirectory = new
                            {
                                type = "string",
                                description = "Optional path to the game root directory. If omitted, uses the active game session."
                            }
                        },
                        required = new[] { "pattern" }
                    }
                },
                new
                {
                    name = "notes",
                    description = "Saves agent online research notes (e.g. recommended BepInEx version, runtime environment, known assembly hooks) as a permanent documentation artifact (RESEARCH.md) in the workspace.",
                    inputSchema = new
                    {
                        type = "object",
                        properties = new
                        {
                            notes = new
                            {
                                type = "string",
                                description = "Markdown-formatted research notes and findings."
                            },
                            gameDirectory = new
                            {
                                type = "string",
                                description = "Optional path to the game root directory. If omitted, uses the active game session."
                            }
                        },
                        required = new[] { "notes" }
                    }
                },
                new
                {
                    name = "type",
                    description = "[Step 3.5 of Modding Pipeline] Retrieves decompiled type definitions (base classes, interfaces, fields, methods, source file path, and optional source code). Supports signaturesOnly and memberFilter to keep large classes compact.",
                    inputSchema = new
                    {
                        type = "object",
                        properties = new
                        {
                            fullTypeName = new
                            {
                                type = "string",
                                description = "Fully qualified type name (e.g. 'GameLogic.Characters.PlayerController')."
                            },
                            signaturesOnly = new
                            {
                                type = "boolean",
                                description = "If true, omits rawSourceCode and returns only signatures and sourceFilePath. Defaults to false."
                            },
                            memberFilter = new
                            {
                                type = "string",
                                description = "Optional substring to filter fields, methods, and extracted C# member blocks."
                            },
                            gameDirectory = new
                            {
                                type = "string",
                                description = "Optional root directory of the game or database path. If omitted, uses the active game from the last inspection."
                            }
                        },
                        required = new[] { "fullTypeName" }
                    }
                },
                new
                {
                    name = "scaffold",
                    description = "[Step 4 of Modding Pipeline] Initializes a ready-to-compile BepInEx 5 C# mod project (.csproj) configured with BepInEx.AssemblyPublicizer and Harmony patch templates. Set includeAssetPipeline: true ONLY if the mod requires custom sprites/textures; otherwise keep false for code/logic mods.",
                    inputSchema = new
                    {
                        type = "object",
                        properties = new
                        {
                            modName = new
                            {
                                type = "string",
                                description = "PascalCase name of the mod (e.g. InfiniteStaminaMod)."
                            },
                            pluginGuid = new
                            {
                                type = "string",
                                description = "Reverse-domain plugin GUID identifier (e.g. com.author.infinitestamina)."
                            },
                            version = new
                            {
                                type = "string",
                                description = "Semantic version of the mod plugin (e.g. 1.0.0)."
                            },
                            outputDirectory = new
                            {
                                type = "string",
                                description = "Directory where the mod project folder will be created."
                            },
                            gameDirectory = new
                            {
                                type = "string",
                                description = "Root directory of the target Unity game to configure references and post-build deploy targets."
                            },
                            publicizedAssemblies = new
                            {
                                type = "array",
                                items = new { type = "string" },
                                description = "Names of assemblies to publicize (e.g. ['Assembly-CSharp']). Defaults to ['Assembly-CSharp']."
                            },
                            includeAssetPipeline = new
                            {
                                type = "boolean",
                                description = "Whether to configure a loose asset pipeline (Assets/ folder, AssetLoader.cs utility, and post-build deployment of loose assets to the plugin directory). Leave false (default) for code/logic-only mods."
                            }
                        },
                        required = new[] { "modName", "pluginGuid", "version", "outputDirectory", "gameDirectory" }
                    }
                },
                new
                {
                    name = "add-reference",
                    description = "[Step 4.5 of Modding Pipeline] Safely adds a verified Managed/*.dll <Reference> entry (with optional <Publicize>true</Publicize>) to a mod's .csproj file. Use this when 'build' reports CS0012 missing assembly references.",
                    inputSchema = new
                    {
                        type = "object",
                        properties = new
                        {
                            projectFilePath = new
                            {
                                type = "string",
                                description = "Path to the mod .csproj file."
                            },
                            assemblyName = new
                            {
                                type = "string",
                                description = "Assembly name (with or without .dll suffix) located in the game's Managed/ directory (e.g. 'UnityEngine.AnimationModule')."
                            },
                            publicize = new
                            {
                                type = "boolean",
                                description = "If true, adds <Publicize>true</Publicize> to expose private members. Defaults to false."
                            },
                            gameDirectory = new
                            {
                                type = "string",
                                description = "Optional game root directory. If omitted, inferred from the .csproj or active game session."
                            }
                        },
                        required = new[] { "projectFilePath", "assemblyName" }
                    }
                },
                new
                {
                    name = "build",
                    description = "[Step 5 of Modding Pipeline] Executes 'dotnet build' on a mod project (.csproj) and returns structured compiler diagnostics (file, line number, CS error codes) plus automatic missingReferenceHints for CS0012 errors.",
                    inputSchema = new
                    {
                        type = "object",
                        properties = new
                        {
                            projectFilePath = new
                            {
                                type = "string",
                                description = "Absolute path to the mod .csproj file."
                            },
                            configuration = new
                            {
                                type = "string",
                                @enum = new[] { "Debug", "Release" },
                                description = "Build configuration. Defaults to 'Debug'."
                            }
                        },
                        required = new[] { "projectFilePath" }
                    }
                },
                new
                {
                    name = "logs",
                    description = "[Step 6 of Modding Pipeline] Reads recent log entries and stack traces from BepInEx/LogOutput.log, safely sharing access even while the game is actively running, to verify runtime mod execution. If logFilePath is omitted, automatically reads the active game's BepInEx log.",
                    inputSchema = new
                    {
                        type = "object",
                        properties = new
                        {
                            logFilePath = new
                            {
                                type = "string",
                                description = "Optional absolute path to the LogOutput.log file. If omitted, uses the active game's BepInEx/LogOutput.log."
                            },
                            gameDirectory = new
                            {
                                type = "string",
                                description = "Optional root directory of the target game. If omitted, uses the active game session."
                            },
                            maxLines = new
                            {
                                type = "integer",
                                description = "Maximum number of recent lines to tail. Defaults to 200."
                            },
                            minLevel = new
                            {
                                type = "string",
                                @enum = new[] { "Debug", "Info", "Warning", "Error", "Fatal" },
                                description = "Optional minimum log level filter (e.g. 'Warning' or 'Error')."
                            }
                        }
                    }
                },
                new
                {
                    name = "status",
                    description = "[Live Inspection] Checks if the target Unity game is currently running, reporting PID, main window title, execution status, and whether the Candied companion IPC bridge is responding.",
                    inputSchema = new
                    {
                        type = "object",
                        properties = new
                        {
                            gameDirectory = new
                            {
                                type = "string",
                                description = "Optional path to the game root directory. If omitted, uses the active game session."
                            }
                        }
                    }
                },
                new
                {
                    name = "launch",
                    description = "[Live Inspection] Launches the target Unity game in windowed mode (non-fullscreen, 1280x720) or attaches to an existing running instance. Automatically ensures the Candied IPC companion bridge is provisioned in BepInEx plugins.",
                    inputSchema = new
                    {
                        type = "object",
                        properties = new
                        {
                            gameDirectory = new
                            {
                                type = "string",
                                description = "Optional path to the game root directory. If omitted, uses the active game session."
                            },
                            windowed = new
                            {
                                type = "boolean",
                                description = "Whether to launch in windowed mode (-screen-fullscreen 0 -popupwindow). Defaults to true."
                            },
                            screenWidth = new
                            {
                                type = "integer",
                                description = "Screen width in pixels. Defaults to 1280."
                            },
                            screenHeight = new
                            {
                                type = "integer",
                                description = "Screen height in pixels. Defaults to 720."
                            }
                        }
                    }
                },
                new
                {
                    name = "stop",
                    description = "[Live Inspection] Gracefully stops the running Unity game process.",
                    inputSchema = new
                    {
                        type = "object",
                        properties = new
                        {
                            gameDirectory = new
                            {
                                type = "string",
                                description = "Optional path to the game root directory. If omitted, uses the active game session."
                            }
                        }
                    }
                },
                new
                {
                    name = "index-assets",
                    description = "[Asset Inspection] Scans and indexes all game assets (Sprites, Texture2Ds, TextAssets, MonoBehaviours) into the persistent SQLite index in Zone 3 (.candied/games/<Game>/index.db). Subsequent discovery and search calls execute in <5ms.",
                    inputSchema = new
                    {
                        type = "object",
                        properties = new
                        {
                            gameDirectory = new
                            {
                                type = "string",
                                description = "Optional path to the game root directory. If omitted, uses the active game session."
                            }
                        }
                    }
                },
                new
                {
                    name = "discover-assets",
                    description = "[Asset Inspection] Queries indexed Unity game asset archives (.assets, resources.assets, AssetBundles) offline with compact deduplicated container paths and pagination (totalMatches, offset). For runtime-loaded AssetBundles, use 'inspect-runtime-assets'.",
                    inputSchema = new
                    {
                        type = "object",
                        properties = new
                        {
                            searchPattern = new
                            {
                                type = "string",
                                description = "Optional name filter pattern (e.g. 'Beard', 'Hair', 'Hat', 'Face', 'Icon', 'Quest')."
                            },
                            assetType = new
                            {
                                type = "string",
                                @enum = new[] { "Sprite", "Texture2D", "TextAsset", "MonoBehaviour", "All" },
                                description = "Optional asset type filter ('Sprite', 'Texture2D', 'TextAsset', 'MonoBehaviour', 'All'). If omitted, searches all supported types."
                            },
                            maxResults = new
                            {
                                type = "integer",
                                description = "Maximum number of assets to return per page (1-500). Defaults to 50."
                            },
                            offset = new
                            {
                                type = "integer",
                                description = "Pagination offset into matched results. Defaults to 0."
                            },
                            containerName = new
                            {
                                type = "string",
                                description = "Optional filter to search only within a specific container or bundle archive."
                            },
                            pathId = new
                            {
                                type = "integer",
                                description = "Optional filter to target a specific Unity PathID."
                            },
                            gameDirectory = new
                            {
                                type = "string",
                                description = "Optional path to the game root directory. If omitted, uses the active game session."
                            }
                        }
                    }
                },
                new
                {
                    name = "extract-assets",
                    description = "[Asset Inspection] Directly extracts visual assets (PNG), text assets (TXT/JSON/CSV/XML), and MonoBehaviour/ScriptableObject configurations (JSON) from Unity disk archives (.assets and AssetBundles) into Zone 3 (.candied/games/<Game>/assets/) completely offline without running the game.",
                    inputSchema = new
                    {
                        type = "object",
                        properties = new
                        {
                            searchPattern = new
                            {
                                type = "string",
                                description = "Optional name filter pattern (e.g. 'Camera', 'Canvas', 'Player', 'Dialogue')."
                            },
                            assetType = new
                            {
                                type = "string",
                                @enum = new[] { "Sprite", "Texture2D", "TextAsset", "MonoBehaviour", "All" },
                                description = "Optional asset type filter ('Sprite', 'Texture2D', 'TextAsset', 'MonoBehaviour', 'All'). If omitted, extracts all matched types."
                            },
                            maxResults = new
                            {
                                type = "integer",
                                description = "Maximum number of assets to extract. Defaults to 50."
                            },
                            outputDirectory = new
                            {
                                type = "string",
                                description = "Optional custom output directory. If omitted, saves into .candied/games/<Game>/assets/."
                            },
                            containerName = new
                            {
                                type = "string",
                                description = "Optional filter to extract only from a specific container or bundle archive."
                            },
                            pathId = new
                            {
                                type = "integer",
                                description = "Optional filter to extract a specific Unity PathID."
                            },
                            disambiguateCollisions = new
                            {
                                type = "boolean",
                                description = "Whether to append PathID to prevent name collisions when multiple archives contain assets with identical names. Defaults to true."
                            },
                            gameDirectory = new
                            {
                                type = "string",
                                description = "Optional path to the game root directory. If omitted, uses the active game session."
                            }
                        }
                    }
                },
                new
                {
                    name = "capabilities",
                    description = "[Live Perception - Step 1] Queries the in-game Candied.VisionBridge for active engine capabilities: available UI frameworks (uGUI, UI Toolkit), input systems (InputSystem vs legacy), screenshot capture methods, limitations, and input click methods. Call this before inspecting UI or sending inputs.",
                    inputSchema = new
                    {
                        type = "object",
                        properties = new
                        {
                            port = new
                            {
                                type = "integer",
                                description = "Optional HTTP port for Candied.VisionBridge. If omitted, defaults to CANDIED_PORT environment variable."
                            },
                            token = new
                            {
                                type = "string",
                                description = "Optional authentication token for Candied.VisionBridge. If omitted, defaults to CANDIED_TOKEN environment variable."
                            }
                        }
                    }
                },
                new
                {
                    name = "screenshot",
                    description = "[Live Perception - Step 2] Captures a pixel-accurate screenshot of the running game using in-game camera rendering / ScreenCapture, saves it to Zone 3 disk, and returns the file path, resolution, and uniformity status.",
                    inputSchema = new
                    {
                        type = "object",
                        properties = new
                        {
                            savePath = new
                            {
                                type = "string",
                                description = "Optional absolute path where to save the captured PNG file. Defaults to .candied/games/<Game>/screenshots/."
                            },
                            gameDirectory = new
                            {
                                type = "string",
                                description = "Optional game directory to resolve the screenshot storage location."
                            },
                            port = new
                            {
                                type = "integer",
                                description = "Optional HTTP port for Candied.VisionBridge. Defaults to CANDIED_PORT."
                            },
                            token = new
                            {
                                type = "string",
                                description = "Optional authentication token. Defaults to CANDIED_TOKEN."
                            }
                        }
                    }
                },
                new
                {
                    name = "click",
                    description = "[Live Interaction - Step 3] Sends an in-process mouse click at the given pixel coordinates (x, y) relative to the screenshot image (top-left origin) and returns a follow-up screenshot with a visual marker at the clicked position.",
                    inputSchema = new
                    {
                        type = "object",
                        properties = new
                        {
                            x = new
                            {
                                type = "number",
                                description = "X coordinate in screenshot image space (0 to width - 1, origin top-left)."
                            },
                            y = new
                            {
                                type = "number",
                                description = "Y coordinate in screenshot image space (0 to height - 1, origin top-left)."
                            },
                            button = new
                            {
                                type = "integer",
                                description = "Mouse button (0=left, 1=right, 2=middle). Defaults to 0 (left)."
                            },
                            holdFrames = new
                            {
                                type = "integer",
                                description = "Number of frames to hold down the mouse button before release. Defaults to 6."
                            },
                            method = new
                            {
                                type = "string",
                                @enum = new[] { "input_system", "legacy", "postmessage" },
                                description = "Optional input injection method override. Defaults to active method detected in capabilities."
                            },
                            port = new
                            {
                                type = "integer",
                                description = "Optional HTTP port for Candied.VisionBridge."
                            },
                            token = new
                            {
                                type = "string",
                                description = "Optional authentication token."
                            }
                        },
                        required = new[] { "x", "y" }
                    }
                },
                new
                {
                    name = "wait-idle",
                    description = "[Live Interaction - Step 4] Waits for the game rendering and frame buffer to stabilize (useful after sending clicks or menu transitions).",
                    inputSchema = new
                    {
                        type = "object",
                        properties = new
                        {
                            maxFrames = new
                            {
                                type = "integer",
                                description = "Maximum number of stable frames to wait for. Defaults to 10."
                            },
                            timeoutMs = new
                            {
                                type = "integer",
                                description = "Timeout in milliseconds. Defaults to 3000."
                            },
                            port = new
                            {
                                type = "integer",
                                description = "Optional HTTP port for Candied.VisionBridge."
                            },
                            token = new
                            {
                                type = "string",
                                description = "Optional authentication token."
                            }
                        }
                    }
                },
                new
                {
                    name = "scene-info",
                    description = "[Live Perception] Retrieves runtime Unity scene information (active scene name, build index, root GameObject count, loaded scene count, and screen dimensions).",
                    inputSchema = new
                    {
                        type = "object",
                        properties = new
                        {
                            port = new
                            {
                                type = "integer",
                                description = "Optional HTTP port for Candied.VisionBridge."
                            },
                            token = new
                            {
                                type = "string",
                                description = "Optional authentication token."
                            }
                        }
                    }
                },
                new
                {
                    name = "inspect-scene-objects",
                    description = "[Live Inspection] Inspects Unity runtime cameras and renderers via VisionBridge with full hierarchy path, local transform, sorting layer/order, bounds, sprite name/pivot, attached components, materials/shaders, and visibility. Supports filtering and truncation.",
                    inputSchema = new
                    {
                        type = "object",
                        properties = new
                        {
                            nameFilter = new
                            {
                                type = "string",
                                description = "Optional substring to filter cameras and renderers by object name, full hierarchy path, or sprite name (case-insensitive)."
                            },
                            includeInactive = new
                            {
                                type = "boolean",
                                description = "If true, includes inactive scene GameObjects and disabled renderers (excluding unloaded prefab assets). Defaults to false."
                            },
                            visibleOnly = new
                            {
                                type = "boolean",
                                description = "If true, returns only renderers with isVisible=true. Defaults to false."
                            },
                            sortingLayer = new
                            {
                                type = "string",
                                description = "Optional exact sortingLayerName filter (case-insensitive)."
                            },
                            maxResults = new
                            {
                                type = "integer",
                                description = "Maximum number of renderers to return (1-1000). Defaults to 100."
                            },
                            port = new
                            {
                                type = "integer",
                                description = "Optional HTTP port for Candied.VisionBridge."
                            },
                            token = new
                            {
                                type = "string",
                                description = "Optional authentication token."
                            }
                        }
                    }
                },
                new
                {
                    name = "inspect-runtime-assets",
                    description = "[Live Inspection] Inspects AssetBundles currently loaded in Unity memory and runtime-loaded Unity assets (Sprite, Texture2D, Material, GameObject, ScriptableObject, Shader, AudioClip, or All) via VisionBridge.",
                    inputSchema = new
                    {
                        type = "object",
                        properties = new
                        {
                            nameFilter = new
                            {
                                type = "string",
                                description = "Optional case-insensitive substring to match loaded asset names and bundle asset paths."
                            },
                            assetType = new
                            {
                                type = "string",
                                description = "Unity asset type to query via Resources.FindObjectsOfTypeAll (e.g. 'Sprite', 'Texture2D', 'Material', 'GameObject', 'ScriptableObject', 'All'). Defaults to 'Sprite'."
                            },
                            maxResults = new
                            {
                                type = "integer",
                                description = "Maximum number of loaded assets to return (1-500). Defaults to 50."
                            },
                            port = new
                            {
                                type = "integer",
                                description = "Optional HTTP port for Candied.VisionBridge."
                            },
                            token = new
                            {
                                type = "string",
                                description = "Optional authentication token."
                            }
                        }
                    }
                },
                new
                {
                    name = "list-ui",
                    description = "[Live Perception] Lists interactive UI elements (Selectable / uGUI / UI Toolkit) when supported by the game. If the game uses custom non-enumerable controls (e.g. 2D colliders), returns an actionable suggestion to use screenshot + click instead.",
                    inputSchema = new
                    {
                        type = "object",
                        properties = new
                        {
                            port = new
                            {
                                type = "integer",
                                description = "Optional HTTP port for Candied.VisionBridge."
                            },
                            token = new
                            {
                                type = "string",
                                description = "Optional authentication token."
                            }
                        }
                    }
                },
                new
                {
                    name = "list-games",
                    description = "[Automation - Step 1] Lists all registered games in the allowlist along with their engine, architecture, and VisionBridge installation status.",
                    inputSchema = new
                    {
                        type = "object",
                        properties = new { }
                    }
                },
                new
                {
                    name = "game-launch",
                    description = "[Automation - Step 2] Launches a registered game inside Candied Studio as an independent detached process, waits for 2 consecutive stable windows (diff < 0.25% with stable scene and active renderer count, plus optional log marker), and returns execution status.",
                    inputSchema = new
                    {
                        type = "object",
                        properties = new
                        {
                            name = new
                            {
                                type = "string",
                                description = "The registered name of the game in the allowlist."
                            },
                            waitForLog = new
                            {
                                type = "string",
                                description = "Optional substring that must appear in BepInEx/LogOutput.log before declaring launch ready."
                            }
                        },
                        required = new[] { "name" }
                    }
                },
                new
                {
                    name = "game-close",
                    description = "[Automation - Step 3] Closes the active game running inside Candied Studio and cleans up session state.",
                    inputSchema = new
                    {
                        type = "object",
                        properties = new { }
                    }
                }
            }
        };
    }
}
