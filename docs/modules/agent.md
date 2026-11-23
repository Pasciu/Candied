# Module: Candied.Agent

## 1. Overview
`Candied.Agent` is the presentation layer and executable entry point of the solution. It provides two operational modes:
1. **Model Context Protocol (MCP) Server:** A JSON-RPC 2.0 stdio server compliant with the MCP specification (`2024-11-05`), allowing LLM assistants (e.g., Claude Desktop, Cursor, Antigravity) to directly discover and invoke workbench capabilities.
2. **CLI Fallback Mode:** Direct terminal execution of commands (e.g. `dotnet run --project src/Candied.Agent -- inspect <gameDir>`).

* **Namespace:** `Candied.Agent`
* **Dependencies:** `Candied.Core`, `Candied.Decompiler`, `Candied.Workspace`, `Candied.Primitives`
* **External Packages:** `Microsoft.Extensions.DependencyInjection`

---

## 2. Directory & Component Structure

```
Candied.Agent/
├── Program.cs                  <-- Composition root and CLI/MCP mode selector
├── AgentWorkbenchService.cs    <-- Domain use case facade, storage layout routing & JSON serialization
└── Mcp/
    ├── McpProtocol.cs          <-- JSON-RPC 2.0 request/response records and error codes
    ├── McpToolDefinitions.cs   <-- JSON Schema declarations for all advertised tools
    └── McpServer.cs            <-- Standard I/O loop, non-blocking concurrent dispatcher, and tool executor
```

---

## 3. Composition Root (`Program.cs`)

`Program.cs` is the only place in the entire solution where all architectural layers assemble:
* Registers infrastructure adapters (`AddDecompilerInfrastructure()`, `AddWorkspaceInfrastructure()`).
* Registers application use cases:
  - `InspectGameEnvironmentUseCase`
  - `SetupGameEnvironmentUseCase`
  - `RollbackGameEnvironmentUseCase`
  - `DecompileAssemblyUseCase`
  - `SearchGameSymbolsUseCase`
  - `SaveResearchNotesUseCase`
  - `ScaffoldModUseCase`
  - `BuildModUseCase`
  - `ReadLogsUseCase`
  - `GetTypeDetailUseCase`
  - `GetGameProcessStatusUseCase`
  - `LaunchGameUseCase`
  - `StopGameUseCase`
  - `DiscoverAssetsUseCase`
  - `ExtractAssetsUseCase`
* Registers presentation components:
  - `AgentWorkbenchService`
  - `McpServer`

When launched without arguments, it defaults to running the `McpServer` listening on `Console.In` and responding on `Console.Out`.

---

## 4. Model Context Protocol (MCP) Implementation

### A. Lifecycle Methods Handled
* **`initialize`**: Responds with protocol version `2024-11-05`, tools capability, server info (`Candied-workbench` v1.0.0), and embedded pipeline `instructions` that automatically instruct the connecting LLM client on the exact 8-step modding workflow.
* **`notifications/initialized`**: Consumed as a notification (no response sent).
* **`ping`**: Responds with `{}`.
* **`tools/list`**: Returns the complete catalog of tools with strict JSON Schema definitions and `[Step X]` pipeline metadata.
* **`tools/call`**: Dispatches the requested tool, extracts typed arguments, invokes `AgentWorkbenchService`, and wraps the result in standard MCP content format with embedded `nextSuggestedStep` state-machine guidance:
  ```json
  {
    "jsonrpc": "2.0",
    "id": 1,
    "result": {
      "content": [
        {
          "type": "text",
          "text": "{\"success\":true,\"nextSuggestedStep\":\"...\",...}"
        }
      ],
      "isError": false
    }
  }
  ```

### B. Non-Blocking Stdio Concurrency
To prevent stdin deadlocks and client timeouts during long-running tasks (such as decompilation or build execution), `McpServer` decouples standard input reading from request execution:
* The read loop immediately parses incoming JSON-RPC lines and spawns worker tasks via `Task.Run`.
* Standard output writing is synchronized through an asynchronous `SemaphoreSlim(1, 1)` write lock.
* This ensures that health-checks, pings, and subsequent tool requests are never blocked behind an ongoing decompilation or build task.

---

## 5. Advertised MCP Tools

The MCP tool names published by `tools/list` use the same identifiers as the primary CLI commands in `Program.cs` (for example, `inspect`, `query`, and `discover-assets`). CLI-only aliases such as `search_symbols` remain available in the CLI but are not separate MCP tools. This replaces the previous MCP-only identifiers; clients and prompts that hardcode those names must switch to the new names or discover them through `tools/list`.

| Tool Name | Parameters | Description |
|---|---|---|
| `inspect` | `gameDirectory` (string) | Non-destructive audit of a Unity game directory without decompilation. Detects engine type, architecture, BepInEx version, target framework, and returns classified assemblies. |
| `setup` | `gameDirectory` (string), `bepInExVersion` (string, optional), `enableConsoleLogging` (bool, optional), `enableDiskLogging` (bool, optional) | Automatically provisions architecture-matched BepInEx 5.x, configures logging in `BepInEx.cfg`, and creates `.candied_manifest.json` in the game root. |
| `rollback` | `gameDirectory` (string) | Reversibly uninstalls BepInEx and restores original game folder state by reading `.candied_manifest.json` and removing only tracked files. |
| `decompile` | `assemblyName` (string), `gameDirectory` (string, optional) | Decompiles a single specified game assembly (.dll) with intelligent caching and incrementally updates the SQLite symbol index. |
| `query` | `searchTerm` (string), `gameDirectory` (string, optional) | Fast symbol query against the SQLite index populated by decompiled assemblies. |
| `notes` | `notes` (string), `gameDirectory` (string, optional) | Saves agent research findings (e.g., community modding guides, BepInEx requirements) into `.candied/games/<GameName>/docs/RESEARCH.md`. |
| `type` | `fullTypeName` (string), `gameDirectory` (string, optional) | Returns interfaces, base class, fields, methods, relative source file path, and raw decompiled source code. |
| `scaffold` | `modName`, `pluginGuid`, `version`, `outputDirectory`, `gameDirectory`, `publicizedAssemblies`, `includeAssetPipeline` (bool, optional) | Generates a ready-to-compile BepInEx 5 plugin with `AssemblyPublicizer` and Harmony templates. Agent opt-in `includeAssetPipeline: true` enables loose sprites and asset loading boilerplate deployed alongside the DLL. |
| `build` | `projectFilePath` (string), `configuration` (string, optional) | Runs `dotnet build`, captures output, and parses errors/warnings into structured diagnostics. |
| `logs` | `logFilePath` (string), `maxLines` (int, optional), `minLevel` (string, optional) | Reads and parses `LogOutput.log` with non-locking file sharing and stack trace stitching. |
| `status` | `gameDirectory` (string, optional) | Checks whether the target game is running, reporting PID, main window title, execution status, and responsiveness. |
| `launch` | `gameDirectory` (string, optional), `windowed` (bool, optional, default: true), `screenWidth` (int, optional, default: 1280), `screenHeight` (int, optional, default: 720) | Launches the target Unity game in windowed mode (non-fullscreen, 1280x720) or attaches to an existing instance. |
| `stop` | `gameDirectory` (string, optional) | Gracefully terminates the running Unity game process. |
| `discover-assets` | `searchPattern` (string, optional), `assetType` (string, optional, 'Sprite' or 'Texture2D'), `maxResults` (int, optional, default: 100), `gameDirectory` (string, optional) | Directly scans Unity asset files (.assets, resources.assets, Addressable .bundle files) on disk to find visual assets without launching the game. |
| `extract-assets` | `searchPattern` (string, optional), `assetType` (string, optional, 'Sprite' or 'Texture2D'), `maxResults` (int, optional, default: 50), `outputDirectory` (string, optional), `gameDirectory` (string, optional) | Extracts targeted Sprites or Textures from Unity disk archives directly to PNG files in Zone 3 (.candied/games/<Game>/assets/). |
| `list-games` | *(none)* | Lists registered Unity Mono games and checks if `Candied.VisionBridge` is deployed. |
| `game-launch` | `name` (string) | Starts Studio host and launches/embeds the registered game, waiting for frame stability (diff < 1%). |
| `game-close` | *(none)* | Terminates the embedded game and cleans up Windows Job Object. |
| `capabilities` | `port` (int, optional), `token` (string, optional) | Queries active VisionBridge modules (input system, screenshot engine, limitations). |
| `screenshot` | `savePath` (string, optional), `port` (int, optional), `token` (string, optional) | Captures in-process frame with uniformity detection. |
| `click` | `x` (number), `y` (number), `button` (int, optional), `holdFrames` (int, optional), `method` (string, optional) | Injects synthetic mouse click and returns post-click verification frame with crosshair marker. |
| `inspect-scene-objects` | `name` (string, optional), `port` (int, optional), `token` (string, optional) | Minimally queries active cameras and scene renderers (sorting layer, order, AABB bounds, shaders/materials, visibility). |
| `wait-idle` | `maxFrames` (int, optional), `timeoutMs` (int, optional) | Waits for scene rendering to stabilize. |
| `scene-info` | `port` (int, optional), `token` (string, optional) | Reports active scene name, index, and loaded status. |
| `list-ui` | `port` (int, optional), `token` (string, optional) | Lists interactive uGUI `Selectable` elements with screen bounding boxes. |

---

## 6. Workbench Facade & Storage Routing (`AgentWorkbenchService`)

`AgentWorkbenchService` serves as the presentation facade connecting MCP / CLI entry points to domain use cases:
* **Workspace Root & Relative Path Anchoring:** Resolves the Candied repository root via `ResolveDefaultWorkspaceRoot` (inspecting `CANDIED_WORKSPACE_ROOT`, `Directory.GetCurrentDirectory()` ancestors, and `AppContext.BaseDirectory` ancestors for `Candied.sln` / `.git`). All relative paths passed to `scaffold`, `build`, or `screenshot` are resolved strictly against `_storageLayout.WorkspaceRoot`, and `Program.Main` anchors the MCP server's working directory to `WorkspaceRoot`, preventing mod sources or artifacts from ever being written inside the parent IDE's application folder (e.g. `AppData\Local\Programs\antigravity`).
* **Zone 3 Storage Routing:** Resolves the workspace root and instantiates `CandiedStorageLayout`. All decompiled code, SQLite index files, and generated docs are routed exclusively to `<WorkspaceRoot>/.candied/games/<GameName>/`, strictly preventing game folder contamination.
* **Automated Knowledge Hub Generation:** During `DecompileAssemblyUseCase`, after the target assembly is decompiled into isolated directory `decompiled/<AssemblyName>/` and indexed, it extracts all cumulative symbols via `GetAllSymbolsAsync` and invokes `IDocumentationGenerator.GenerateKnowledgeHubAsync(...)` to update `GAME_OVERVIEW.md`, `NAMESPACES.md`, and `SYMBOLS_INDEX.json` in Zone 3 with real, cumulative counts across all decompiled assemblies.
* **Context-Aware Session State:** Tracks active game context in memory (`_activeGameDirectory`, `_activeGameDatabasePath`). When subsequent tools (`decompile`, `query`, `type`, `notes`) omit `gameDirectory`, the active context is seamlessly applied.

---

## 7. CLI Command Catalog (`dotnet run --project src/Candied.Agent -- <cmd>`)

The CLI commands are aligned 1:1 with the MCP tool capabilities:

| Command | Arguments | Description |
|---|---|---|
| `inspect` | `<gameDir>` | Audits game environment without decompilation. |
| `setup` | `<gameDir>` | Automatically provisions BepInEx & preconfigures logging with `.candied_manifest.json`. |
| `rollback` | `<gameDir>` | Restores virgin game directory state using manifest. |
| `decompile` | `<assemblyName> [gameDir]` | Targeted single-assembly decompilation with caching & SQLite indexing. |
| `query` / `search_symbols` | `<searchTerm> [gameDir]` | Fast query against SQLite symbol index without decompilation. |
| `type` / `detail` | `<fullTypeName> [gameDir]` | Retrieves decompiled class signature, AST details, and source code. |
| `notes` / `research` | `<notes> [gameDir]` | Persists agent research findings to `RESEARCH.md`. |
| `scaffold` | `<modName> <gameDir> [outDir] [--with-assets]` | Scaffolds a ready-to-compile BepInEx 5 plugin with `AssemblyPublicizer`. Optional `--with-assets` sets up embedded asset pipeline. |
| `build` | `<csprojPath> [config]` | Compiles a mod project with path normalization and displays structured diagnostics. |
| `logs` | `<logFilePath> [maxLines]` | Reads and parses runtime `LogOutput.log`. |
| `status` / `game-status` | `[gameDir]` | Reports running game process status. |
| `launch` | `<gameDir> [--fullscreen] [--width W] [--height H]` | Launches target game in windowed mode (1280x720 default). |
| `stop` | `[gameDir]` | Gracefully terminates target game process. |
| `discover-assets` | `[pattern] [type] [max] [gameDir]` | Scans Unity game asset archives (.assets and .bundle) to discover visual assets offline. |
| `extract-assets` | `[pattern] [type] [max] [gameDir] [outDir]` | Extracts targeted Sprites or Textures directly from disk archives into PNG files in Zone 3 assets. |
| `inspect-scene-objects` | `[--name filter] [--port P] [--token T]` | Minimally queries active cameras and scene renderers (sorting layer, order, bounds, shaders, visibility). |

---

## 8. Publishing & Updating the MCP Binary

When changes are made to `Candied.Agent` or any upstream project, running `dotnet build` only updates `src/Candied.Agent/bin/Release/`. To update the standalone MCP binary in `./publish/Candied.Agent.exe`, run `./setup.ps1` (or `./setup.sh`). See [mcp-update-guide.md](../mcp-update-guide.md) for the complete update and reload procedure.
