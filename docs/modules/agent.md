# Module: Candied.Agent

## 1. Overview
`Candied.Agent` (`net9.0`) is the CLI and Model Context Protocol (MCP) presentation orchestrator of Candied. It operates in two modes:
1. **MCP JSON-RPC 2.0 Stdio Server** (default when invoked with no CLI arguments): Exposes Candied's 29 engine-agnostic core & vision tools plus all dynamically registered `IPlatformToolProvider` tools (30 total with `unity-add-reference`) to AI coding assistants (Antigravity, Claude Desktop, Cursor).
2. **Direct CLI Mode**: Executes any core or platform-specific tool from the terminal (`dotnet run --project src/Candied.Agent -- <command> ...`).

* **Namespace:** `Candied.Agent`
* **Dependencies:** `Candied.Primitives`, `Candied.Core`, `Candied.Infrastructure`, `Candied.Vision.Protocol`, `Candied.Vision`, and registered `Candied.Platforms.*` modules (currently `Candied.Platforms.Unity.Mono`).

---

## 2. Composition Root (`Program.cs`)

`Program.cs` is the single composition root assembling all layers via `Microsoft.Extensions.DependencyInjection`:
```csharp
services.AddCandiedCoreAndInfrastructure();
services.AddVisionInfrastructure();
services.AddUnityMonoPlatform(); // Future platforms plug in here with 1 line!
services.AddTransient<AgentWorkbenchService>();
services.AddTransient<McpServer>();
```

Adding a new target platform in future phases (e.g., `services.AddUnityIl2CppPlatform()` or `services.AddGodotPlatform()`) requires registering its DI extension in `Program.cs`—zero changes to `Candied.Core`, `Candied.Infrastructure`, `Candied.Vision`, `Candied.Studio`, or `McpServer`.

---

## 3. Non-Blocking MCP Server & Dynamic Tool Discovery

* **`McpServer`:** Implements JSON-RPC 2.0 (`initialize`, `notifications/initialized`, `ping`, `tools/list`, `tools/call`) with concurrent `Task.Run` request execution and `SemaphoreSlim(1, 1)` stdout serialization so long-running `extract-code` or `build` calls never block `ping` health checks.
* **`McpToolDefinitions`:** Combines the **29 engine-agnostic Core & Vision tools** with every `PlatformToolDefinition` discovered from `IEnumerable<IPlatformToolProvider>` (such as `unity-add-reference`).
* **Parameter Ergonomics:** Accepts both `camelCase` (`targetPath`, `runnerPath`, `moduleName`) and `snake_case` (`target_path`, `runner_path`, `module_name`) parameters seamlessly.

---

## 4. Advertised MCP & CLI Tool Catalog (v2.0 Clean Cut)

### A. Agnostic Audit, Code, Assets & Modding Tools (14 Tools)
| Tool | Primary Parameters | Purpose |
|---|---|---|
| `inspect` | `targetPath`, `runnerPath?` | Non-destructive audit of any PC game directory/executable or ROM + Emulator. Returns `platformId`, `capabilities`, `codeModules`, and platform modding guide. |
| `setup` | `targetPath?`, `runnerPath?`, `forceReinstall?`, `enableConsoleLogging?` | Provisions the target platform's mod loader and in-game Vision sensor with `.candied_manifest.json`. |
| `rollback` | `targetPath?`, `runnerPath?` | Restores clean target directory state from `.candied_manifest.json`. |
| `extract-code` | `moduleName`, `targetPath?`, `runnerPath?` | Extracts/decompiles a single code module into Zone 3, indexes symbols + FTS5 code in SQLite, and updates the Knowledge Hub. |
| `query` | `searchTerm`, `kind?`, `namespacePrefix?`, `moduleFilter?`, `limit?`, `targetPath?` | Fast SQLite symbol lookup across extracted modules. |
| `search-code` | `pattern`, `namespacePrefix?`, `moduleFilter?`, `maxResults?`, `targetPath?` | Full-text SQLite FTS5 + substring search across extracted code bodies with line snippets. |
| `type` | `fullTypeName`, `signaturesOnly?`, `memberFilter?`, `targetPath?` | Inspects full type inheritance, members, Zone 3 `sourceFilePath`, and full or member-filtered source code. |
| `notes` | `notes`, `targetPath?` | Persists reverse-engineering notes to Zone 3 `docs/RESEARCH.md`. |
| `index-assets` | `targetPath?`, `forceReindex?` | Scans and indexes target game assets into Zone 3 SQLite (`assets` table). |
| `discover-assets` | `searchPattern?`, `assetType?`, `maxResults?`, `offset?`, `containerName?`, `assetKey?`, `targetPath?` | Paginated offline asset discovery with container deduplication. |
| `extract-assets` | `searchPattern?`, `assetType?`, `maxResults?`, `outputDirectory?`, `containerName?`, `assetKey?`, `targetPath?` | Extracts sprites/textures/data offline into Zone 3 `assets/`. |
| `scaffold` | `modName`, `modId?`, `version?`, `outputDirectory?`, `targetPath?`, `includeAssetPipeline?`, `platformOptions?` | Scaffolds a mod project in Zone 2 (`mods/<ModName>`) using the target platform's `IModWorkspaceCapability`. |
| `build` | `projectFilePath`, `configuration?` | Compiles/packages the mod project and returns structured diagnostics + resolution hints. |
| `logs` | `logFilePath?`, `targetPath?`, `maxLines?`, `minLevel?` | Reads and filters structured runtime logs via `IRuntimeLogCapability` or generic log tailing. |

*(Note: 14 offline tools above + 15 live Vision & Control Mapping tools below = 29 core tools + dynamic platform tools.)*

### B. 2-Tier Live Vision & 3-Channel Control Mapping Tools (15 Tools)
| Tool | Primary Parameters | Purpose |
|---|---|---|
| `game-launch` | `targetPath?`, `runnerPath?`, `extraArguments?`, `waitForLog?`, `stabilityTimeoutSeconds?` | Directly launches and embeds any PC game or Emulator + ROM inside `Candied.Studio` (no pre-registration allowlist required). |
| `game-close` | *(none)* | Terminates the hosted game/emulator process tree cleanly via Studio. |
| `recent-targets` | *(none)* | Lists recently launched targets recorded by Studio/Agent. |
| `status` | `targetPath?` | Reports Studio host status, active `VisionTier` (`BaseWindow` vs `SensorBridge`), and process liveness. |
| `capabilities` | `port?`, `token?` | Audits active screenshot & 3-channel input capabilities across Tier 1 or Tier 2. |
| `screenshot` | `savePath?`, `targetPath?`, `port?`, `token?` | Captures PNG screenshot to Zone 3 `screenshots/` with uniformity detection. |
| `send-input` | `channel` (`pointer`/`keyboard`/`gamepad`), `action`, `x?`, `y?`, `button?`, `key?`, `gamepadButton?`, `stick?`, `stickX?`, `stickY?`, `holdFrames?` | Unified 3-channel input injection; pointer clicks return an annotated verification PNG with crosshair marker. |
| `get-controls` | `targetPath?` | Reads the target's `GameControlProfile` (`controls.json`), auto-seeding from platform extractors when available. |
| `set-controls` | `actions[]`, `targetPath?` | Saves or updates semantic action bindings (`Confirm`, `Inventory`, `Jump`, etc.) in `controls.json`. |
| `trigger-action` | `actionName`, `targetPath?` | Resolves a semantic action from `controls.json` and dispatches it over Mouse, Keyboard, or Gamepad. |
| `wait-idle` | `maxFrames?`, `timeoutMs?` | Waits for rendering stabilization (Tier 2 frame counter or Tier 1 pixel-diff settle). |
| `list-ui` | `port?`, `token?` | Lists interactive UI elements exposed by a Tier 2 Vision sensor. |
| `scene-info` | `port?`, `token?` | Queries active scene metadata from a Tier 2 Vision sensor. |
| `inspect-scene` | `nameFilter?`, `includeInactive?`, `visibleOnly?`, `attributeFilter?`, `maxResults?` | Queries cameras and generic `VisionSceneNodeDto` hierarchy nodes from a Tier 2 Vision sensor. |
| `inspect-runtime-assets` | `nameFilter?`, `assetType?`, `maxResults?` | Queries runtime-loaded asset containers and in-memory objects from a Tier 2 Vision sensor. |

### C. Dynamically Registered Platform Tools (`IPlatformToolProvider`)
| Tool | Platform | Parameters | Purpose |
|---|---|---|---|
| `unity-add-reference` | `unity.mono` | `projectFilePath`, `moduleName`, `publicize?`, `targetPath?` | Injects a Unity managed `.dll` `<Reference>` (with optional `Publicize`) into a BepInEx mod `.csproj`. |
