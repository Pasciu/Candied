# Agent Tools Reference & Live Perception Guide (v2.0)

This guide documents the **Candied v2.0** Model Context Protocol (MCP) and CLI tools for reverse-engineering, modding, hosting, visually inspecting, and interacting with PC games and emulated ROMs.

---

## 1. Two-Tier Live Vision & Session Architecture

Candied separates offline reverse-engineering/modding from **Candied Studio**, a pure standalone 2-Tier Live Vision & Window Host:

1. **Agent <-> Candied Studio (`127.0.0.1:{studioPort}`):**
   - Coordinated via `%APPDATA%/Candied/studio_session.json` (`X-Candied-Studio-Token`).
   - **No Pre-Registration Allowlist:** `game-launch` accepts *any* `targetPath` (PC directory, `.exe`, or ROM/ISO) plus an optional `runnerPath` (emulator executable such as `pcsx2-qt.exe` or `dolphin.exe`). Recently launched targets are saved automatically in `recent_targets.json` (`recent-targets`).
2. **Tier 1 (`BaseWindow`) vs Tier 2 (`SensorBridge`):**
   - **Tier 1 (`BaseWindow`):** Available **immediately** for every hosted window (custom C++, Unity IL2CPP without sensor, Godot, emulators). Captures screenshots via Win32 `PrintWindow`/`BitBlt` and dispatches `pointer`, `keyboard`, and `gamepad` events via Win32 messages to the hosted `HWND`.
   - **Tier 2 (`SensorBridge`):** Activated **automatically** when an in-game Vision v2.0 sensor (such as `Candied.VisionSensors.UnityMono`, installed by `setup`) connects on `127.0.0.1:{CANDIED_PORT}`. Unlocks isolated virtual input devices, `list-ui`, `scene-info`, `inspect-scene`, and `inspect-runtime-assets`.

---

## 2. Recommended Sequential Workflows

### A. Offline Reverse Engineering & Mod Creation
1. `inspect(targetPath, runnerPath?)` -> Audit platform (`unity.mono`, `generic.window`, etc.), capabilities, mod loader, and `modules` catalog.
2. `setup(targetPath?)` -> *(If `EnvironmentSetup` is supported and loader/sensor is missing, after user confirmation)* Provision mod loader & Vision sensor.
3. `extract-code(moduleName)` -> Extract/decompile relevant `CoreLogic` / `ModularLogic` modules into Zone 3 SQLite + FTS5.
4. `query` / `search-code` / `type` -> Locate exact types, methods, fields, and call sites.
5. `index-assets` / `discover-assets` / `extract-assets` -> *(Optional)* Inspect or extract sprites/textures/configs into Zone 3.
6. `scaffold` -> Edit mod sources in `mods/<ModName>/` -> `build` (using dynamic platform tools such as `unity-add-reference` if hinted by `build`).

### B. Live Visual Perception & 3-Channel Interaction
1. `game-launch(targetPath?, runnerPath?)` -> Host target inside Candied Studio and wait for frame stability.
2. `capabilities` & `get-controls` -> Check active Vision tier (`BaseWindow` vs `SensorBridge`), input channels, and mapped semantic actions (`controls.json`).
3. `screenshot` -> **Mandatory before clicking:** inspect the live frame, verify it is not uniform/loading, and read coordinates or current screen state.
4. `send-input` or `trigger-action` -> Dispatch pointer click/move, keyboard key, gamepad button/stick, or semantic action (`"Confirm"`, `"Cancel"`, `"Inventory"`).
5. `wait-idle` -> `screenshot` -> **Mandatory verification:** confirm observable visual change; check `logs` if verifying mod runtime behavior.
6. `game-close` -> Cleanly terminate the hosted process tree before rebuilding mods (`build`).

---

## 3. Tool Reference Summary

### Target Audit, Code & Asset Pipeline
* **`inspect(targetPath, runnerPath?)`:** Non-destructive audit returning `platformId`, `architecture`, `engineVersion`, `runtimeFramework`, `modLoader`, `capabilities`, classified `modules`, and `platformGuide`.
* **`setup(targetPath?, runnerPath?, forceReinstall?, enableConsoleLogging?)` & `rollback(targetPath?, runnerPath?)`:** Manifest-tracked (`.candied_manifest.json`) provisioning and clean uninstallation.
* **`extract-code(moduleName, targetPath?, runnerPath?)`:** Extracts/decompiles a single code module into `.candied/games/<Target>/extracted/<ModuleName>/`, updates SQLite (`symbols`, `type_details`, `type_source_fts`), and refreshes `GAME_OVERVIEW.md`, `NAMESPACES.md`, and `SYMBOLS_INDEX.json`.
* **`query(searchTerm, kind?, namespacePrefix?, moduleFilter?, limit?, targetPath?)`:** Fast symbol search across indexed modules.
* **`search-code(pattern, namespacePrefix?, moduleFilter?, maxResults?, targetPath?)`:** SQLite FTS5 + substring search across extracted code bodies, returning line numbers, snippets, and Zone 3 `sourceFilePath`.
* **`type(fullTypeName, signaturesOnly?, memberFilter?, targetPath?)`:** Returns inheritance, fields, methods, `sourceFilePath`, and full or member-filtered source code.
* **`notes(notes, targetPath?)`:** Appends/saves reverse-engineering findings to `.candied/games/<Target>/docs/RESEARCH.md`.
* **`index-assets`, `discover-assets`, `extract-assets`:** Offline SQLite-backed asset indexing, paginated discovery (`offset`, `maxResults`), and extraction to `.candied/games/<Target>/assets/`.
* **`scaffold`, `build`, `logs`:** Platform-driven mod project generation in `mods/<ModName>/`, compilation with structured diagnostics, and severity-filtered runtime log reading.

### Live Vision, 3-Channel Input & Control Mapping
* **`game-launch(targetPath?, runnerPath?, extraArguments?, waitForLog?, stabilityTimeoutSeconds?)`:** Launches Studio (if needed), embeds the target window, and waits for stable frames.
* **`game-close()` & `recent-targets()` & `status(targetPath?)`:** Session lifecycle and liveness inspection.
* **`screenshot(savePath?, targetPath?, port?, token?)`:** Saves PNG screenshot to `.candied/games/<Target>/screenshots/` and reports `width`, `height`, `visionTier`, and `isUniform`.
* **`send-input(...)`:**
  - `channel: "pointer"`: `action: "click" | "move" | "down" | "up" | "isolate" | "release_isolation"`, `x`, `y` (**top-left screenshot pixel coordinates**), `button` (`0`=left, `1`=right, `2`=middle), `holdFrames`. Clicks automatically save an annotated PNG with a high-contrast crosshair at `(x, y)`.
  - `channel: "keyboard"`: `action: "press" | "down" | "up"`, `key` (e.g., `"Enter"`, `"Escape"`, `"Space"`, `"W"`, `"UpArrow"`, `"F1"`), `holdFrames`.
  - `channel: "gamepad"`: `action: "press" | "down" | "up" | "stick"`, `gamepadButton` (`"South"`, `"East"`, `"West"`, `"North"`, `"DPadUp"`, `"Start"`, `"Select"`, `"LeftShoulder"`, etc.) or `stick` (`"left"` / `"right"`) + `stickX`, `stickY` in `[-1.0..1.0]`.
* **`get-controls(targetPath?)`, `set-controls(actions, targetPath?)`, `trigger-action(actionName, targetPath?)`:** Read, update, and execute semantic game controls stored in `.candied/games/<Target>/controls.json`.
* **Tier 2 Sensor Inspection (`list-ui`, `scene-info`, `inspect-scene`, `inspect-runtime-assets`, `wait-idle`):** Deep engine hierarchy (`VisionSceneNodeDto` with generic `attributes`), interactive UI rects, and runtime loaded assets when a Tier 2 sensor is connected.

### Dynamic Platform Tools (`IPlatformToolProvider`)
* **`unity-add-reference(projectFilePath, moduleName, publicize?, targetPath?)`** *(registered by `unity.mono`)*: Safely adds a Unity managed `.dll` `<Reference>` (with optional `<Publicize>true</Publicize>`) to a BepInEx C# mod `.csproj`.

---

## 4. Golden Rules for AI Agents

1. **Always verify input visually:** HTTP 200 from `send-input` or `trigger-action` only confirms event dispatch. Always follow up with `wait-idle` + `screenshot` to confirm actual in-game state changes.
2. **Top-left pixel coordinates:** Pointer `(x, y)` coordinates always use top-left origin `[0..width-1, 0..height-1]` matching the saved screenshot PNG. Never invert Y manually.
3. **Close before building:** Always call `game-close` before `build` so locked plugin DLLs do not block deployment.
4. **Update `controls.json` when discovering bindings:** Whenever you discover key/gamepad bindings in code (`extract-code` / `search-code`) or in an in-game Controls menu (`screenshot`), save them via `set-controls` so both the user (in Studio's **🎮 Comandi** tab) and subsequent `trigger-action` calls benefit immediately.
