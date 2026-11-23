# Agent Tools Reference & Visual Interaction Guide

This guide is written for AI agents and LLMs operating on running Unity Mono games via Candied. It details the MCP perception and interaction tools, their parameters, the required sequential workflow, and known runtime limitations.

---

## 1. Connection & Session Architecture

Candied coordinates visual interaction through two communication tiers:

1. **MCP Server to Candied Studio (Host Level):**
   - **Protocol:** Ephemeral local HTTP server hosted by Candied Studio on `127.0.0.1:{STUDIO_PORT}`.
   - **Session Coordination File:** `%APPDATA%/Candied/studio_session.json` (`studioPid`, `studioPort`, `studioToken`, `activeGameName`, `gamePid`, `bridgePort`, `bridgeToken`).
   - **Why Local HTTP + Session File:** Provides zero-port-conflict dynamic binding (ephemeral port `0`), cross-process discovery between independently spawned processes, and robust security through session tokens (`X-Candied-Studio-Token`).
   - **Token Privacy:** The session token is never printed in tool outputs, logs, or command-line parameters.
   - **Stale Session Handling:** When checking Studio liveness, Candied inspects the recorded `studioPid`. If the process is dead, the session file is automatically deleted and the host is treated as offline, preventing false "connected" states.
   - **Process Detachment & Visibility Guarantee:** Studio is launched as an independent top-level process via Windows Shell (`UseShellExecute = true`, `WindowStyle = Normal`). It survives MCP server shutdowns, reconnections, or crashes. Studio enforces `EnsureVisibleAndActive()`: normalizes window state if minimized, clamps dimensions and position inside the monitor work area, overrides any `SW_HIDE` from parent process `STARTUPINFO` using explicit Win32 `ShowWindow(SW_SHOWNORMAL)` and `ShowWindow(SW_RESTORE)`, sets `ShowInTaskbar = true`, and brings the window to foreground via `SetForegroundWindow`. All startup lifecycle events and unhandled exceptions are logged to `%APPDATA%/Candied/logs/studio.log`. `game-launch` validates that `MainWindowHandle` is non-zero, `IsWindowVisible == true`, and `isWithinMonitor == true` before declaring success.
   - **Allowlist & Security Guardrails:** `game-launch` accepts **ONLY** registered game names from `%APPDATA%/Candied/registered_games.json`, never arbitrary file paths. Registering an IL2CPP game is strictly rejected. If `Candied.VisionBridge` is missing, `game-launch` does **not** install it automatically; it returns the exact instruction (`esegui: install-bridge <nome>`).

2. **Candied Studio / Agent to In-Game Bridge (Runtime Level):**
   - Communicates with the in-game HTTP server hosted by the BepInEx 5 plugin `Candied.VisionBridge` on `127.0.0.1:{bridgePort}` with header `X-Candied-Token`.
   - If the game is not running or the bridge is unreachable, tools return an actionable error:
     ```json
     {
       "success": false,
       "error": "gioco non connesso",
       "detail": "Candied VisionBridge non è raggiungibile...",
       "suggestion": "Assicurati che il gioco Unity sia avviato con Candied.VisionBridge e che risponda sulla porta specificata."
     }
     ```

---

## 2. Recommended Agent Interaction Order

When interacting with games, an AI agent MUST follow this strict sequential lifecycle:

```
┌────────────────────────┐
│    list-games     │ ─── 1. Check registered games and bridge installation
└───────────┬────────────┘
            │
            ▼
┌────────────────────────┐
│      game-launch       │ ─── 2. Start Studio & embed game; wait for stable menu (diff < 1%)
└───────────┬────────────┘
            │
            ▼
┌────────────────────────┐
│   capabilities    │ ─── 3. Audit active input/screenshot engines
└───────────┬────────────┘
            │
            ▼
┌────────────────────────┐
│    screenshot     │ ─── 4. Capture baseline frame & locate target pixel (x, y)
└───────────┬────────────┘
            │
            ▼
┌────────────────────────┐
│     click      │ ─── 5. Dispatch in-process click (top-left origin coordinates)
└───────────┬────────────┘
            │
            ▼
┌────────────────────────┐
│     wait-idle     │ ─── 6. Wait for scene rendering and transitions to settle
└───────────┬────────────┘
            │
            ▼
┌────────────────────────┐
│    screenshot     │ ─── 7. VERIFICATION: Capture follow-up frame to verify observable change
└───────────┬────────────┘
            │
            ▼
┌────────────────────────┐
│       game-close       │ ─── 8. Terminate game and session when inspection is done
└────────────────────────┘
```

> [!IMPORTANT]
> **MANDATORY POST-LAUNCH VERIFICATION:**
> After `game-launch` succeeds, an agent MUST ALWAYS call `screenshot` before attempting interaction. While `game-launch` waits for frame stability (diff < 1%), capturing a fresh screenshot ensures the agent inspects the actual visible frame, validates against uniform/black screens, and extracts exact button coordinates.

---

## 3. Tool Reference

### `list-games`
* **When to use:** At the very beginning of a session to list registered games and check whether `Candied.VisionBridge` is installed.
* **Inputs:** None.
* **Outputs:**
  - `games` (array of objects):
    - `name` (string): Registered game name (e.g., `"TargetGame"`).
    - `executablePath` (string): Full path to the game executable.
    - `hasBridgeInstalled` (boolean): `true` if `Candied.VisionBridge.dll` exists in `BepInEx/plugins/`.
* **Agent rule:** If `hasBridgeInstalled` is `false`, do not call `game-launch`. Follow the instructions to install the bridge first.

### `game-launch`
* **When to use:** To start Candied Studio (if not already running) and launch/embed a registered Unity Mono game.
* **Inputs:**
  - `name` (string, **required**): The registered name of the game (e.g., `"TargetGame"`). **Never pass a filesystem path.**
  - `waitForLog` / `wait_for_log` (string, optional): Case-insensitive substring that must appear in `BepInEx/LogOutput.log` before declaring launch ready.
* **Behavior & Idempotence:**
  - If Studio or the game is already running, it returns the current running status immediately without spawning a duplicate instance.
  - If the bridge is not installed, it refuses to launch and outputs the exact command: `esegui: install-bridge <nome>`.
  - Automatically waits (up to 60s timeout) for `/hello` to respond and for **2 consecutive stable 1s windows** where pixel diff is `< 0.25%` AND `/scene` confirms `activeSceneName`, `loadedSceneCount`, and `activeRendererCount` ($\Delta \le 2$) are unchanged (preventing false positives on slow loading screens).
* **Outputs:**
  - `success` (boolean): `true` if the game reached stable menu and Studio window is verified visible and clamped.
  - `status` (string): `"Running"`.
  - `gameName` (string): Registered game name.
  - `activeSceneName` (string) & `activeRendererCount` (int): Observed Unity scene state upon stabilization.
  - `window` (object): Window metrics (`mainWindowHandle`, `isWindowVisible: true`, `windowState`, `visibility`, `rect`: `{ left, top, right, bottom, width, height }`, `isWithinMonitor: true`, `isResponding: true`).
  - `resolution` / `gameResolution` / `panelResolution` / `screenshotDimensions` (objects): `{ "width": int, "height": int }`.
  - `waitOutcome` (string): e.g., `"Menu stabile (2 finestre consecutive, diff: 0.00% < 0.25%, scena: 'Main', loadedScenes: 4, renderers: 7457)"`.
  - `bridgePort` (int): Active in-game bridge port.
  - `nextSuggestedStep` (string): Recommended follow-up (`"Usa 'screenshot' per esaminare lo stato visivo o 'click' per interagire con i pulsanti."`).

### `game-close`
* **When to use:** When finishing the modding or inspection session to cleanly terminate the embedded game process.
* **Inputs:** None.
* **Outputs:**
  - `success` (boolean): `true`.
  - `status` (string): `"Closed"`.
  - `message` (string): Confirmation message.
* **Guarantees:** Terminates the game process and its entire process tree via Windows Job Object, and deletes the session file.

### `capabilities`
* **When to use:** Call once at the start of visual inspection to discover active engine modules.
* **Inputs:** `port` (int, optional), `token` (string, optional).
* **Outputs:**
  - `ugui` (boolean): `true` if `UnityEngine.UI.GraphicRaycaster` and `Selectable` are present.
  - `inputSystem` (boolean): `true` if modern `UnityEngine.InputSystem` is present.
  - `legacyInput` (boolean): `true` if `UnityEngine.Input` is present.
  - `screenshotMethod` (string): `screen_capture` (backbuffer) or `camera_fallback` (depth-sorted off-screen camera render).
  - `screenshotLimitations` (string): e.g., camera render does not capture `ScreenSpaceOverlay` canvases.
  - `inputClickMethod` (string): active injection engine (`input_system`, `legacy`, or `postmessage`).
  - `inputClickStatus` (string): validation status (`"provato una volta su un gioco (Unity Mono Target Game), affidabilità non misurata"`).
  - `untestedMethods` (string): `"legacy e postmessage sono implementati ma mai provati dal vivo"`.

### `screenshot`
* **When to use:** To inspect the visual state of the game, read menus, or locate clickable buttons.
* **Inputs:** `savePath` (string, optional), `gameDirectory` (string, optional), `port`, `token`.
* **Outputs:**
  - `filePath` (string): Absolute path to the saved PNG on disk.
  - `width` (int), `height` (int): Frame dimensions (e.g. 1024x720).
  - `isUniform` (boolean): `true` if the captured image is a single flat color (e.g., black screen or loading blackout).
* **Agent rule:** Inspect the generated PNG to extract $(x, y)$ coordinates for the next click.

### `click`
* **When to use:** To click a button, menu item, or game object at specific pixel coordinates.
* **Inputs:**
  - `x` (number, **required**): Horizontal coordinate from $0$ to $width - 1$, **origin top-left**.
  - `y` (number, **required**): Vertical coordinate from $0$ to $height - 1$, **origin top-left**.
  - `button` (int, optional, default `0`): `0` = Left, `1` = Right, `2` = Middle.
  - `holdFrames` (int, optional, default `6`): Number of frames the button is held down before release.
  - `method` (string, optional): Override method (`input_system`, `legacy`, `postmessage`).
* **Outputs:**
  - `success` (boolean): `true` if event queued in Unity engine.
  - `status` (string): `"Dispatched"`.
  - `hitPointX` (int): Click horizontal pixel on image ($0..width-1$).
  - `hitPointY` (int): Click vertical pixel on image ($0..height-1$).
  - `screenshotPath` (string): Absolute path to post-click PNG with an in-memory high-contrast visual marker (red ring with black border and crosshairs) centered exactly at the clicked coordinate.
  - `warning` (string): Caution regarding synthetic background click reliability.
* **Warning semantics:**
  ```json
  {
    "warning": "Il click viene inviato tramite input injection; verificare sempre l'effetto reale con screenshot."
  }
  ```
* **Coordinate transformation:** The tool automatically maps from top-left image coordinates to Unity bottom-left screen space ($UnityY = ScreenHeight - ImageY$). Do not invert coordinates manually.

### `wait-idle`
* **When to use:** Immediately after `click` or a scene transition.
* **Inputs:** `maxFrames` (int, default `10`), `timeoutMs` (int, default `3000`).
* **Outputs:** `idleReached` (boolean), `elapsedFrames` (int).

### `scene-info`
* **When to use:** To check what Unity scene is currently active and its load state.
* **Outputs:** `activeSceneName`, `activeSceneIndex`, `rootCount`, `isLoaded`, `loadedSceneCount`, `screenWidth`, `screenHeight`, `activeRendererCount`, `timeSinceLevelLoad`.
* **Fallback behavior:** If the game uses legacy scene management without `UnityEngine.SceneManagement.SceneManager`, it falls back to `/hello` with `activeSceneName = "Unknown"`.

### `inspect-scene-objects`
* **When to use:** To inspect cameras and scene renderers with full hierarchy paths, local transforms, sorting layers/orders, world-space bounding boxes, sprite names & pivots, attached component names, shader/material names, and visibility. Supports filtering and truncation to prevent oversized dumps.
* **Inputs:**
  - `nameFilter` / `name_filter` (string, optional): Case-insensitive substring filter matching object name, `fullPath`, or `spriteName`.
  - `includeInactive` / `include_inactive` (bool, default `false`): Include inactive scene GameObjects and disabled renderers (excludes unloaded prefab assets via `scene.IsValid()`).
  - `visibleOnly` / `visible_only` (bool, default `false`): Return only renderers with `isVisible == true`.
  - `sortingLayer` / `sorting_layer` (string, optional): Case-insensitive exact match on `sortingLayerName`.
  - `maxResults` / `max_results` (int, default `100`, range `1..1000`): Cap on returned renderers.
  - `port` (int, optional), `token` (string, optional).
* **Outputs:**
  - `success` (boolean), `totalCameras` (int), `totalRenderers` (int), `totalMatched` (int), `truncated` (bool).
  - `cameras` (array): `name`, `orthographic`, `orthographicSize`, `cullingMask`, `depth`, `position` (`[x, y, z]`).
  - `renderers` (array): `name`, `parentName`, `fullPath`, `activeSelf`, `activeInHierarchy`, `rendererType`, `sortingLayerName`, `sortingLayerId`, `sortingOrder`, `localPosition` (`[x, y, z]`), `localScale` (`[x, y, z]`), `center` (`[x, y, z]`), `extents` (`[x, y, z]`), `spriteName`, `spritePivot` (normalized `[x, y]`), `components` (array of component type names), `materials` (`materialName`, `shaderName`), `isVisible`.
* **Example CLI:**
  ```powershell
  dotnet run --project src/Candied.Agent -- inspect-scene-objects --visible-only --max 25
  ```

### `inspect-runtime-assets`
* **When to use:** To inspect `AssetBundle`s currently loaded in Unity memory and runtime-loaded Unity objects (`Sprite`, `Texture2D`, `Material`, `GameObject`, `ScriptableObject`, `Shader`, `AudioClip`, or `All`) via VisionBridge `GET /assets/loaded`. Complements offline `discover-assets` when assets are loaded dynamically at runtime.
* **Inputs:**
  - `nameFilter` / `name_filter` (string, optional): Case-insensitive substring matching loaded asset names and bundle asset paths.
  - `assetType` / `asset_type` (string, default `"Sprite"`): Unity asset type to enumerate via `Resources.FindObjectsOfTypeAll`.
  - `maxResults` / `max_results` (int, default `50`, range `1..500`): Maximum loaded assets returned.
  - `port` (int, optional), `token` (string, optional).
* **Outputs:** `totalBundles`, `totalLoadedAssets`, `returnedAssets`, `truncated`, `bundles` (`name`, `isStreamedScene`, `assetCount`, `matchingAssetPaths`), `loadedAssets` (`name`, `assetType`, `instanceId`).

### Reverse-Engineering & Build Ergonomics Tools (`query`, `type`, `search-code`, `discover-assets`, `add-reference`, `build`)
* **`query`:** Supports `kind` (`all|types|members|class|struct|interface|enum|method|field|property|event`), `namespacePrefix`, and `limit` (default `25`).
* **`type`:** Returns Zone 3 `sourceFilePath` alongside type metadata. Supports `signaturesOnly: true` (omits `rawSourceCode`) and `memberFilter` (filters signatures and extracts only matching C# member blocks via `TypeMemberSourceExtractor`).
* **`search-code`:** Full-text SQLite FTS5 + exact substring search across all decompiled C# source bodies (`pattern`, optional `namespacePrefix`, `assemblyName`, `maxResults` default `25`). Automatically backfills pre-existing SQLite indexes on first query and returns `fullTypeName`, `assemblyName`, `lineNumber`, `snippet`, and Zone 3 `sourceFilePath`.
* **`discover-assets`:** Auto-resolves active `gameDirectory`, deduplicates `sourceContainerPath` into a top-level `containers` dictionary, and supports pagination via `offset` + `maxResults` (default `50`), returning `totalMatches`, `returnedCount`, and `hasMore`.
* **`build` & `add-reference`:** When `build` fails with `CS0012` (missing assembly reference), `missingReferenceHints` lists each missing assembly name, its verified path in `<Game>_Data/Managed/`, and an exact `add-reference(projectFilePath, assemblyName)` command. `add-reference` safely injects `<Reference>` (with optional `<Publicize>true</Publicize>`) into the `.csproj`, inferring `Managed/` automatically from existing `<HintPath>` entries or the active game session.

### `list-ui`
* **When to use:** To enumerate interactive uGUI (`Selectable`) buttons, inputs, and sliders with bounding boxes.
* **Prerequisite check:** Only works if `capabilities` reports `ugui: true` or `uiToolkit: true`.
* **Alternative behavior when not available:**
  If the game does not use standard uGUI (for instance, games using custom 2D/3D colliders), the tool returns an actionable error:
  ```json
  {
    "success": false,
    "error": "list_ui non disponibile",
    "detail": "Il gioco non espone una UI uGUI (Selectable) o UI Toolkit enumerabile.",
    "suggestion": "list_ui non disponibile: usa screenshot + click"
  }
  ```
  Follow the suggested alternative: capture a screenshot with `screenshot` and click with `click`.

---

## 4. Known Limitations & Golden Rules for Agents

1. **Click is NOT guaranteed to trigger scene transitions:**
   Synthetic input injection via `UnityEngine.InputSystem.QueueStateEvent<MouseState>` has been **proven once on a single game (Unity Mono Target Game)**, but its **reliability across other Unity games is unmeasured**. Some games enforce focus checks (`Application.isFocused == false`) in their update loops and may ignore synthetic input when running in the background.
2. **Never assume HTTP 200 means the game reacted:**
   `click` returning `"success": true` means the event was dispatched to the Unity engine event queue, NOT that the game logic triggered a reaction. **Always capture a post-click screenshot to observe whether the scene changed**.
3. **Legacy Input and PostMessage are untested live:**
   `legacy` (`UnityEngine.Input` / raycasting) and `postmessage` (`WM_LBUTTONDOWN`/`WM_LBUTTONUP`) are implemented as fallback mechanisms in the bridge, but have **never been tested live on real games**. Use `input_system` (default) whenever available.
4. **Camera render limitations:**
   When `screenshotMethod` is `camera_fallback`, UI elements rendered on `ScreenSpaceOverlay` canvases or post-processing passes that execute after camera rendering may not be visible in the captured frame. If available, prefer top-level desktop capture.

---

## 5. Tool Parameter Ergonomics & Error Guidance

### Dual Naming Convention (snake_case and camelCase)
All MCP tools and CLI dispatchers accept parameters in either `snake_case` or `camelCase`:
* `game_directory` or `gameDirectory`
* `max_lines` or `maxLines`
* `project_file_path` or `projectFilePath`
* `assembly_name` or `assemblyName`
* `search_term` or `searchTerm`
* `full_type_name` or `fullTypeName`

### Contextual Default Resolution
* **`logs` without path:** If `logFilePath` is omitted or empty, Candied automatically resolves `BepInEx/LogOutput.log` from the currently active running game session, or the single registered game.
* **`gameDirectory` omitted:** Tools requiring `gameDirectory` (e.g. `logs`, `query`, `type`, `notes`) automatically resolve the active game directory. If multiple games are registered and none is currently active, the tool prompts the user/agent to specify which game.
* **Path containment guarantee:** All generated paths, logs, screenshots, and decompiled outputs stay strictly inside the respective game and workspace directory hierarchies.

### Actionable Errors for Unindexed Types and Symbols
When `type` or `query` is called on a symbol or type that does not exist in the SQLite index, the response returns a structured, actionable suggestion directing the agent to decompile the parent assembly first:
```json
{
  "success": false,
  "error": "Type not found: 'TargetNamespace.CustomClass'",
  "suggestion": "Tipo 'TargetNamespace.CustomClass' non trovato nell'indice SQLite. Se appartiene a un assembly non ancora decompilato, esegui prima 'decompile(assemblyName)' specificando l'assembly corrispondente (usa 'inspect' per vedere gli assembly disponibili)."
}
```
