# Autonomous Modding Guide for AI Agents & Modders

This guide is the operational reference for AI agents using **Candied** to reverse engineer, inspect, mod, and test generic Unity Mono games.

**Read this guide in full before using any tool.** The user will describe goals in plain language; this guide tells you how to turn them into a verified mod.

> [!IMPORTANT]
> **Maturity of the live tools (read before using Playbook D and G).**
> The live perception tools (`game-launch`, `screenshot`, `click`, ...) have been
> verified on **one game only** (Unity Mono, New Input System). Everything marked
> *unproven* below is implemented but has never been observed working on a real game.
> On any other game, treat the first click as an experiment and report honestly if it has no effect.

### Multiplayer & Anti-Cheat Warning (Required Agent Check)

Candied has **never been tested on multiplayer games or titles with anti-cheat systems** (e.g. Easy Anti-Cheat, BattlEye, VAC, Riot Vanguard, etc.).
Before running `setup`, deploying a mod DLL, or launching a target game that is a known multiplayer title or shows anti-cheat files/services during `inspect`:
1. **Pause and calmly inform the user** that Candied hasn't been tested on multiplayer or anti-cheat protected games and that modding online titles can carry a risk of account bans.
2. Clarify that they proceed at their own discretion (the author cannot take responsibility for bans or penalties) and **ask for explicit confirmation** before installing BepInEx or modifying the game directory.

---

## 1. Architectural Mental Model

Candied provides a dual-interface toolkit designed for autonomous operation:

```
┌────────────────────────────────────────────────────────────────────────┐
│                        AI Agent / Modder                               │
│          (Cursor, Claude Desktop, Antigravity, VS Code, CLI)           │
└──────────────────┬──────────────────────────────────┬──────────────────┘
                   │ MCP (stdio JSON-RPC)             │ CLI Terminal
                   ▼                                  ▼
┌────────────────────────────────────────────────────────────────────────┐
│                             Candied.Agent                              │
│   • CLI Dispatcher (`dotnet run --project src/Candied.Agent -- <cmd>`) │
│   • MCP Server (`mcp` protocol: tools/call, tools/list, initialize)   │
└──────────────────┬──────────────────────────────────┬──────────────────┘
                   │                                  │
    ┌──────────────┴──────────────┐    ┌──────────────┴──────────────┐
    ▼                             ▼    ▼                             ▼
┌───────────────────────┐ ┌───────────────────────┐ ┌───────────────────────┐
│  Candied.Decompiler   │ │   Candied.Workspace   │ │    Candied.Studio     │
│ • PE / Assembly Audit │ │ • Mod Scaffolding     │ │ • Native Host Embedder│
│ • ILSpy Decompilation │ │ • MSBuild Runner      │ │ • Job Object Lifecycle│
│ • Roslyn SQLite Index │ │ • BepInEx Provisioner │ │ • Real-Time Log Tail  │
│ • Offline Unity Assets│ │ • Log Output Monitor  │ │ • Local HTTP Gateway  │
└───────────────────────┘ └───────────────────────┘ └───────────┬───────────┘
                                                                │
                                                                ▼
                                                   ┌─────────────────────────┐
                                                   │   Candied.VisionBridge  │
                                                   │ In-Game BepInEx Plugin  │
                                                   │ • In-process Screenshot │
                                                   │   (see limits below)    │
                                                   │ • In-Process InputClick │
                                                   │ • Frame Stability (Idle)│
                                                   └─────────────────────────┘
```

### Communication Interfaces

1. **MCP Server (stdio JSON-RPC 2.0):**
   - Run mode: `dotnet run --project src/Candied.Agent --no-build -- mcp` or execute the compiled binary `dotnet ./src/Candied.Agent/bin/Release/net9.0/Candied.Agent.dll mcp`.
   - Used by AI assistants to discover tools (`tools/list`) and execute them (`tools/call`).
2. **Command Line Interface (CLI):**
   - Run mode: `dotnet run --project src/Candied.Agent --no-build -- <command> [args]`
   - Every MCP tool has an equivalent 1:1 CLI command for quick manual debugging or scripted workflows.

### What the live tools can and cannot see

- **The screenshot is an in-process render, not what a human sees on screen.** On the game used for verification the
  bridge uses `camera_fallback`: it renders the active cameras into an off-screen texture.
  Screen-space overlay UI canvases and post-processing applied to the final backbuffer may be **missing**.
  Always read `screenshotMethod` and the listed limitations from `capabilities`.
- **Fixed resolution.** The game is launched at `1024x720`. Screenshot coordinates and click coordinates share
  the same space. The Studio panel size (`panelResolution`) is irrelevant for clicks.
- **Input is injected inside the game process** (no OS mouse movement). The Input System method is the only one
  proven. The legacy-input and `PostMessage` methods are *implemented but never proven on a real game*.

---

## 2. Intent-Driven Playbooks (Situational Recipes)

Instead of forcing a single rigid waterfall sequence, choose the playbook that matches your immediate goal:

```
┌────────────────────────────────────────────────────────────────────────────────┐
│ What is your goal?                                                             │
├──────────────────────────────────────┬─────────────────────────────────────────┤
│ "Inspect a game and set up BepInEx"  │ ➔ Playbook A: Environment & Mod Loader  │
│ "Explore C# logic and hook points"   │ ➔ Playbook B: Code Reverse Engineering  │
│ "Inspect/extract sprites & textures" │ ➔ Playbook C: Offline Visual Assets     │
│ "Run game, inspect screens & clicks" │ ➔ Playbook D: Live Perception & Studio  │
│ "Create and compile a C# code mod"   │ ➔ Playbook E: Code Modding & Harmony    │
│ "Add custom graphics/icons to a mod" │ ➔ Playbook F: Loose Asset Pipeline      │
│ "Check that my mod really changes    │ ➔ Playbook G: Build → Launch → Observe  │
│  what the player sees"               │                                         │
└──────────────────────────────────────┴─────────────────────────────────────────┘
```

Playbooks are meant to be combined. See the cross-links in each playbook.

If the user describes a vague or ambitious goal in plain language (a new screen, a new feature), start with **Playbook H**.

---

### Playbook A: Environment Audit & BepInEx Provisioning
*Goal: Audit target game architecture, verify Unity Mono runtime, and ensure BepInEx is installed.*

1. **Audit Environment:**
   - **MCP:** `inspect(gameDirectory: "<path>")`
   - **CLI:** `dotnet run --project src/Candied.Agent --no-build -- inspect "<path>"`
   - **Inspect output:**
     - `engineType`: Must be `Mono`. If `Il2Cpp`, stop: Candied only supports Unity Mono.
     - `architecture`: Note `x64` vs `x86`.
     - `bepInEx.isInstalled`: If `false`, proceed to step 2. If `true`, BepInEx is ready.
     - `assemblies`: Identifies primary gameplay assemblies (`CoreGame` / `GameModular`).
2. **Provision BepInEx (if missing):**
   - **MCP:** `setup(gameDirectory: "<path>")`
   - **CLI:** `dotnet run --project src/Candied.Agent --no-build -- setup "<path>"`
   - Automatically downloads architecture-matched BepInEx, configures disk/console logs, and writes `.candied_manifest.json`.
3. **Rollback (if needed):**
   - **MCP:** `rollback(gameDirectory: "<path>")`
   - **CLI:** `dotnet run --project src/Candied.Agent --no-build -- rollback "<path>"`
   - Completely removes installed BepInEx files without touching original game assets.
4. **Live tools prerequisites (only if you intend to use Playbook D or G):**
   - Check `list-games()` for the game name and `hasBridgeInstalled`.
   - `register-game` and `install-bridge` modify the user's machine configuration and the game folder.
     Run them **only when the user explicitly asks**, never on your own initiative.

> [!IMPORTANT]
> Run setup, bridge installation and every experiment on a **test copy** of the game, not on the user's
> own installation or save files, unless the user explicitly says otherwise.

---

### Playbook B: Reverse Engineering & Code Exploration
*Goal: Understand how game mechanics, stats, or UI systems work in C# and find exact method hook targets.*

1. **Targeted Decompilation:**
   - **MCP:** `decompile(assemblyName: "Assembly-CSharp.dll", gameDirectory: "<path>")`
   - **CLI:** `dotnet run --project src/Candied.Agent --no-build -- decompile Assembly-CSharp.dll "<path>"`
   - > [!IMPORTANT]
     > Decompile framework assemblies (`UnityEngine.*`, `mscorlib.dll`, `System.*`) only if necessary. Focus on `Assembly-CSharp.dll` and other `GameModular` assemblies for gameplay logic.
2. **Search Symbols (Classes, Methods, Fields):**
   - **MCP:** `query(searchTerm: "ItemData", gameDirectory: "<path>")`
   - **CLI:** `dotnet run --project src/Candied.Agent --no-build -- query ItemData "<path>"`
   - Uses sub-5ms SQLite Full-Text Search (FTS5) to find exact symbol definitions.
3. **Inspect Full Class Source & Signatures:**
   - **MCP:** `type(fullTypeName: "Namespace.ClassName", gameDirectory: "<path>")`
   - **CLI:** `dotnet run --project src/Candied.Agent --no-build -- type Namespace.ClassName "<path>"`
   - Returns decompiled C# source, base types, fields, properties, and methods.
4. **Save Research Findings:**
   - **MCP:** `notes(notes: "### Findings...", gameDirectory: "<path>")`
   - **CLI:** `dotnet run --project src/Candied.Agent --no-build -- notes "..." "<path>"`
   - Saves persistent notes into `docs/RESEARCH.md`.

**Cross-link with Playbook D (from what you see to what you patch):**
read a visible label in a screenshot (e.g. a menu entry), then use `query` with that text
(or a likely identifier) to locate the class that drives it. Text shown on screen is often localised,
so the identifier may differ: treat the match as a lead to confirm with `type`, not as proof.

---

### Playbook C: Offline 2D Visual Asset Discovery & Extraction
*Goal: Inspect or extract character sprites, UI icons, textures, or text assets completely offline without running the game.*

1. **Discover Assets (Zero Execution):**
   - **MCP:** `discover-assets(searchPattern: "*icon*", assetType: "Sprite", limit: 50, gameDirectory: "<path>")`
   - **CLI:** `dotnet run --project src/Candied.Agent --no-build -- discover-assets --pattern "*icon*" --type Sprite --limit 50 --dir "<path>"`
   - Scans Unity `.assets`, `resources.assets`, and `StreamingAssets` bundles directly on disk.
2. **Extract Directly to PNG:**
   - **MCP:** `extract-assets(searchPattern: "*face*", assetType: "Texture2D", outputDirectory: "<outPath>", gameDirectory: "<path>")`
   - **CLI:** `dotnet run --project src/Candied.Agent --no-build -- extract-assets --pattern "*face*" --type Texture2D --out "<outPath>" --dir "<path>"`
   - Extracts and decodes textures/sprites directly into readable PNG images on disk for visual inspection.

**Cross-link with Playbooks F and G:** before creating a custom icon, extract the original and look at it,
so that size, palette and style match. After the mod is built, use Playbook G to see whether your asset
appears in the right place in the running game.

---

### Playbook D: Live Perception & In-Game Studio Interaction
*Goal: Launch the game embedded in Candied Studio, inspect rendered screens, click buttons, and verify reactions.*

> [!WARNING]
> Click support is **proven once, on one game** (Input System). Reliability has not been
> measured. On another game the click may have no effect. If two attempts at a correctly placed target
> change nothing, **stop and report it**. Do not try random coordinates.

1. **Check Registered Games & Bridge Status:**
   - **MCP:** `list-games()`
   - **CLI:** `dotnet run --project src/Candied.Agent --no-build -- list-games`
   - If the game is not registered or the bridge is missing, **ask the user** before running
     `register-game <name> "<path_to_exe>"` or `install-bridge <name>` (see Playbook A, step 4).
2. **Launch Game in Candied Studio:**
   - **MCP:** `game-launch(name: "<registered_name>")`
   - **CLI:** `dotnet run --project src/Candied.Agent --no-build -- game-launch <registered_name>`
   - Starts Candied Studio as a detached desktop window, embeds the game at a fixed resolution (`1024x720`),
     and waits (up to 60 s) for the bridge and a stable menu (`diff < 1.00%`).
   - The result reports window visibility. If it reports an error, read the Studio log path it returns
     (`%APPDATA%/Candied/logs/studio.log`) before retrying. Do not call `game-launch` in a loop.
   - `game-launch` is idempotent: if Studio and the game are already running it returns the current state.
3. **Check Capabilities (before the first click):**
   - **MCP:** `capabilities()`
   - **CLI:** `dotnet run --project src/Candied.Agent --no-build -- capabilities`
   - Read: `screenshotMethod` and its limitations, `inputClickMethod`, `inputClickStatus`, `untestedMethods`,
     and whether `ugui` is enumerable. If the input method is listed as untested, say so in your report.
4. **Capture Baseline Screenshot:**
   - **MCP:** `screenshot(savePath: "<target.png>")`
   - **CLI:** `dotnet run --project src/Candied.Agent --no-build -- screenshot --out "<target.png>"`
   - Check `isUniform == false`. A uniform or half-loaded frame is a loading screen: wait with
     `wait-idle` and capture again. Then inspect the image to locate the click target.
5. **Choose the click point carefully:**
   - Click the **center of the target** (the middle of a button or of its label text). Adjacent entries can be
     only a few tens of pixels apart, so an error of 20 px may hit the neighbouring element.
   - If `list-ui` is available (uGUI enumerable), prefer its bounding boxes. If it answers
     "not available", use screenshot + `click`.
   - Before a click with side effects, consider a negative control: click an empty area and confirm the
     screen does not change.
6. **Interact (Click & Hover):**
   - **MCP:** `click(x: 512, y: 360, button: 0, holdFrames: 6)`
   - **CLI:** `dotnet run --project src/Candied.Agent --no-build -- click 512 360 --hold 6`
   - Dispatches in-process synthetic mouse events without moving the physical OS cursor.
   - `holdFrames: 6` is the value used in the one proven click. It is not known whether a lower value works.
7. **Wait for Scene Stabilization:**
   - **MCP:** `wait-idle(maxFrames: 10, timeoutMs: 3000)`
   - **CLI:** `dotnet run --project src/Candied.Agent --no-build -- wait-idle --max 10 --timeout 3000`
8. **Verify Observable Reaction:**
   - **MCP:** `screenshot(savePath: "<after.png>")`
   - > [!IMPORTANT]
     > A click is **only verified** when a follow-up screenshot shows a visible change (new menu entries,
     > different screen, highlighted element). HTTP 200 means the event was queued, not that it worked.
     > Describe what you actually see in the image and cite the file path.
9. **Clean Shutdown:**
   - **MCP:** `game-close()`
   - **CLI:** `dotnet run --project src/Candied.Agent --no-build -- game-close`
   - Kills the embedded game and child processes cleanly via Windows Job Object. Always do this at the end.

---

### Playbook E: Code Mod Scaffolding, Patching & Compilation
*Goal: Create a clean BepInEx 5 Harmony mod, implement game hooks, and compile with automatic deployment.*

1. **Scaffold Mod Project:**
   - **MCP:** `scaffold(modName: "MyMod", pluginGuid: "com.author.mymod", version: "1.0.0", outputDirectory: "mods/MyMod", gameDirectory: "<path>", includeAssetPipeline: false)`
   - **CLI:** `dotnet run --project src/Candied.Agent --no-build -- scaffold MyMod "<path>" mods/MyMod`
   - Generates `.csproj` (with publicized game assemblies), `Plugin.cs`, and `Patches/` templates.
2. **Implement Harmony Hook in `mods/<ModName>/Patches/`:**
   ```csharp
   using HarmonyLib;

   namespace MyMod.Patches;

   [HarmonyPatch(typeof(TargetClass), nameof(TargetClass.TargetMethod))]
   internal static class TargetMethodPatch
   {
       [HarmonyPrefix]
       private static bool Prefix(ref int someParameter)
       {
           // Modify parameter or return false to skip original game logic
           someParameter *= 2;
           return true; 
       }

       [HarmonyPostfix]
       private static void Postfix(ref bool __result)
       {
           // Modify return value or run post-execution logic
           __result = true;
       }
   }
   ```
3. **Compile and Deploy:**
   - **MCP:** `build(projectFilePath: "mods/MyMod/MyMod.csproj", configuration: "Debug")`
   - **CLI:** `dotnet run --project src/Candied.Agent --no-build -- build mods/MyMod/MyMod.csproj Debug`
   - If build fails: review structured compiler diagnostics (`file`, `line`, `code`, `message`), edit C# code, and re-run `build`.
   - On success: automatically deploys the compiled DLL to `<GameRoot>/BepInEx/plugins/MyMod/`.
   - **Precaution:** run `game-close` before `build`. A running game may keep the plugin DLL locked.
     (This is a precaution, not a verified behaviour of `build`.)
4. **Inspect Runtime Logs:**
   - **MCP:** `logs(logFilePath: "<GameRoot>/BepInEx/LogOutput.log", maxLines: 50)`
   - **CLI:** `dotnet run --project src/Candied.Agent --no-build -- logs "<GameRoot>/BepInEx/LogOutput.log" 50`
   - Logs only tell you the plugin loaded or crashed. To check the **visible** effect, continue with Playbook G.

---

### Playbook F: Asset-Driven Mod (Loose Asset Pipeline)
*Goal: Ship custom textures, portraits, or icons with your mod without hardcoding them into compiled DLLs.*

1. **Scaffold with Loose Asset Pipeline:**
   - **MCP:** `scaffold(..., includeAssetPipeline: true)`
   - **CLI:** `dotnet run --project src/Candied.Agent --no-build -- scaffold MyMod "<path>" mods/MyMod --with-assets`
   - Generates `Assets/` directory, `AssetLoader.cs`, and configures MSBuild to copy loose files to `<GameRoot>/BepInEx/plugins/MyMod/Assets/`.
2. **Add Custom Images:**
   - Drop your custom `.png` files into `mods/MyMod/Assets/` (e.g. `icon_gold.png`).
   - Use Playbook C to extract the original asset first and match its size and style.
3. **Load Assets at Runtime in C#:**
   ```csharp
   Texture2D? texture = AssetLoader.LoadTexture("icon_gold.png");
   Sprite? sprite = AssetLoader.LoadSprite("icon_gold.png", pixelsPerUnit: 100f);
   ```
4. **Compile:**
   - Call `build("mods/MyMod/MyMod.csproj")`. MSBuild builds the DLL and copies `Assets/` directly to the plugin folder.
5. **Verify visually:** run Playbook G to confirm the asset appears in the intended place.

---

### Playbook G: Build → Launch → Observe → Iterate (visual mod verification)
*Goal: confirm that a mod actually loads and changes what the player sees.*

Requires the prerequisites of Playbook D (registered game, bridge installed, test copy of the game).

1. **Baseline BEFORE the mod:**
   - `game-launch` → `capabilities` → navigate to the screen the mod affects (Playbook D, steps 4 to 8)
     → `screenshot(savePath: "baseline.png")` → `game-close`.
   - Write down the exact click sequence you used, so you can repeat it identically.
2. **Build with the game closed:** `build` (see the precaution in Playbook E, step 3).
3. **Relaunch and prove the mod loaded:**
   - `game-launch`, then `logs` and look for your plugin GUID.
   - If it is absent, **stop and debug loading before looking at screenshots**.
4. **Navigate to the same screen** with the same clicks used for the baseline, then
   `screenshot(savePath: "after.png")`.
5. **Compare** `baseline.png` and `after.png`. Describe what changed and where, citing both paths.
   Note that installed third-party mods in the game folder can also alter screenshots. If unsure, say so.
6. **Report the verdict honestly:**
   - Objective statements ("the label text changed from X to Y", "a new icon appears at about (x, y)")
     are verifications.
   - Aesthetic statements ("looks good", "well aligned") are **opinions**: label them as such.
   - If the screenshot method omits overlay UI (see section 1) and your mod changes overlay UI, you
     **cannot** verify it this way: say so instead of guessing.
7. **Iterate:** edit patches → `game-close` → `build` → repeat from step 3. Always finish with `game-close`.

---

### Playbook H: From a plain-language idea to a verified mod
*Goal: turn a request such as "add a screen that does X" into a working mod without guessing.*

1. **Research before code** (Playbooks A, B, C): find out how the relevant game objects are built, how
   existing screens, menus and UI elements are created and wired, and which methods are good hook points.
   Save findings with `notes`.
2. **Judge feasibility honestly.** List what looks possible, what is uncertain and what is not possible.
   - Identify the UI technology the game uses (decompiled UI classes, `capabilities`).
   - If the mod must draw its own UI, check whether the screenshot can capture it (see `screenshotMethod` and
     its limitations). Prove it early with a trivial element (one visible shape) before building the full
     feature. If your UI cannot be captured, say so and propose an alternative. Never claim a visual
     verification you cannot perform.
3. **Ask, then stop.** If the request leaves important choices open (scope, behaviour, what "save" means and
   where data is stored, visual style, how the player interacts, extra buttons), end your turn with:
   a short summary of the research, the feasibility verdict, and a list of concrete questions, each with
   your proposed default. Do not write feature code before the user answers. A throwaway feasibility test
   is allowed.
4. **Build in small vertical slices.** Write the success criterion before each slice and prove it with
   screenshots (Playbook G). A typical order: entry point visible and working, then the core feature, then
   secondary features, then persistence.
5. **Persist mod data outside the game's own save files** (the mod's folder or the BepInEx config area),
   unless the user explicitly asks otherwise.
6. **Check compatibility.** Look at the other plugins in the game's `BepInEx/plugins` folder. If they patch
   the same classes, tell the user about the possible conflict.
7. **Final report:** what works (with screenshot paths), what is not verified, known limits.

---

## 3. Comprehensive Tool Catalog & CLI Command Reference

### Environment & Setup

| MCP Tool | CLI Command | Purpose | Key Parameters |
|---|---|---|---|
| `inspect` | `inspect <gameDir>` | Non-destructively audits game, runtime, and assemblies | `gameDirectory` |
| `setup` | `setup <gameDir>` | Provisions architecture-matched BepInEx | `gameDirectory`, `forceReinstall` |
| `rollback` | `rollback <gameDir>` | Reverses BepInEx installation cleanly | `gameDirectory` |

### Reverse Engineering & Symbols

| MCP Tool | CLI Command | Purpose | Key Parameters |
|---|---|---|---|
| `decompile` | `decompile <assembly> [gameDir]` | Decompiles target assembly via ILSpy into isolated dir | `assemblyName`, `gameDirectory` |
| `query` | `query <term> [gameDir]` | Searches classes, methods, fields in SQLite index | `searchTerm`, `gameDirectory` |
| `type` | `type <typeName> [gameDir]` | Returns decompiled C# source & members for a type | `fullTypeName`, `gameDirectory` |
| `notes` | `notes <content> [gameDir]` | Saves findings to `docs/RESEARCH.md` | `notes`, `gameDirectory` |

### Offline Unity Asset Extraction

| MCP Tool | CLI Command | Purpose | Key Parameters |
|---|---|---|---|
| `discover-assets` | `discover-assets` | Scans `.assets` & bundles for textures/sprites offline | `--pattern`, `--type`, `--limit`, `--dir` |
| `extract-assets` | `extract-assets` | Extracts textures/sprites directly to PNG on disk | `--pattern`, `--type`, `--out`, `--dir` |
| `index-assets` | `index-assets` | Pre-indexes all assets in SQLite database | `--dir` |

### Candied Studio & Live Perception

| MCP Tool | CLI Command | Purpose | Key Parameters | Status |
|---|---|---|---|---|
| `list-games` | `list-games` | Lists registered games and bridge install status | *(none)* | Verified live |
| `game-launch` | `game-launch <name>` | Starts Studio, embeds game, waits for stable menu | `name` (registered name) | Verified live (one game) |
| `game-close` | `game-close` | Terminates embedded game and session cleanly | *(none)* | Verified live |
| `capabilities` | `capabilities` | Reports bridge engines, screenshot method and limits, input method | `port`, `token` | Verified live |
| `screenshot` | `screenshot` | Captures an in-process frame with uniformity check | `savePath`, `port`, `token` | Verified live; may omit overlay UI |
| `click` | `click <x> <y>` | Dispatches in-process click (top-left origin) & returns marked screenshot | `x`, `y`, `button`, `holdFrames` | **Proven once, one game (Input System)**; returns post-click screenshot with crosshair marker |
| `inspect-scene-objects` | `inspect-scene-objects [--name <filter>]` | Minimal inspection of active cameras & renderers (sorting layer/order, bounds, materials, isVisible) | `name`, `port`, `token` | Verified live |
| `wait-idle` | `wait-idle` | Waits for render/animation settling | `maxFrames`, `timeoutMs` | Verified live |
| `scene-info` | `scene-info` | Returns active Unity scene name, index, root count | `port`, `token` | Unit-tested; not proven live |
| `list-ui` | `list-ui` | Enumerates uGUI Selectables with bounding boxes | `port`, `token` | Unit-tested only; not verified on a uGUI-based game |

Click methods: **Input System** (proven once), **legacy input** and **`PostMessage`** (implemented, never proven live),
**EventSystem uGUI** (implemented, never proven live on a pure uGUI game).

### Allowlist Management (CLI Only)

These commands change the user's configuration or game folder. Run them only on explicit user request.

| CLI Command | Purpose | Example |
|---|---|---|
| `register-game <name> <exePath>` | Adds a game to `%APPDATA%/Candied/registered_games.json` (rejects IL2CPP) | `dotnet run --project src/Candied.Agent -- register-game MyGame "C:\Games\Game.exe"` |
| `unregister-game <name>` | Removes a game from the allowlist | `dotnet run --project src/Candied.Agent -- unregister-game MyGame` |
| `install-bridge <name>` | Deploys `Candied.VisionBridge.dll` to game's `BepInEx/plugins/` | `dotnet run --project src/Candied.Agent -- install-bridge MyGame` |

### Mod Scaffolding & Build

| MCP Tool | CLI Command | Purpose | Key Parameters |
|---|---|---|---|
| `scaffold` | `scaffold <name> <gameDir> [outDir]` | Generates mod project with publicized game assemblies | `modName`, `pluginGuid`, `includeAssetPipeline` |
| `build` | `build <csprojPath> [config]` | Compiles mod and deploys DLL to BepInEx plugins | `projectFilePath`, `configuration` |
| `logs` | `logs [logPath] [lines]` | Tails BepInEx `LogOutput.log` non-destructively (path optional; defaults to active game) | `logFilePath`, `maxLines` |

> [!NOTE]
> **Automatic DLL Deployment on Build:**
> When `build` runs, the generated `.csproj` contains an MSBuild target `DeployToBepInEx` that automatically copies the built `.dll` (and any loose assets) directly into the test game's `<GameRootPath>/BepInEx/plugins/<ModName>/` directory. There is **no need** for manual copying. Deployment is restricted strictly to the test game copy referenced during scaffolding. If the game is currently running, Windows locks the DLL file and causes `MSB3027` build errors; always close the game (`game-close`) before building.

> [!TIP]
> **Tool Parameter Ergonomics & Unindexed Type Guidance:**
> - **Dual Case Support:** All tools accept parameters in either `camelCase` or `snake_case` (e.g. `gameDirectory` / `game_directory`, `maxLines` / `max_lines`, `projectFilePath` / `project_file_path`).
> - **Omitted Game Directory:** If `gameDirectory` is omitted, Candied automatically resolves the active running or registered game.
> - **Unindexed Types & Symbols:** Calling `type` or `query` on a type/symbol that has not been indexed will return an actionable error naming the exact command to run: `decompile(assemblyName)` (run `inspect` to view available assemblies).

---

## 4. MCP Server Registration Guide

To register Candied as an MCP server in your AI editor:

### VS Code / Cursor / Claude Desktop / Antigravity (`mcpServers` configuration)

```json
{
  "mcpServers": {
    "candied": {
      "command": "dotnet",
      "args": [
        "run",
        "--project",
        "src/Candied.Agent",
        "--no-build",
        "--",
        "mcp"
      ],
      "cwd": "C:\\path\\to\\Candied"
    }
  }
}
```

Or using the compiled Release binary for fastest startup:

```json
{
  "mcpServers": {
    "candied": {
      "command": "dotnet",
      "args": [
        "C:\\path\\to\\Candied\\src\\Candied.Agent\\bin\\Release\\net9.0\\Candied.Agent.dll",
        "mcp"
      ],
      "cwd": "C:\\path\\to\\Candied"
    }
  }
}
```

### Claude Code

Typical form (check `claude mcp add --help` if your version differs):

```
claude mcp add candied -- dotnet "C:\path\to\Candied\src\Candied.Agent\bin\Release\net9.0\Candied.Agent.dll" mcp
```

Build the solution first (`./check.ps1` or `dotnet build -c Release`). If the `.dll` does not exist, the server cannot start.

---

## 5. Mandatory Mod Documentation Architecture

Every mod created with Candied must maintain a clean two-tier documentation structure:

### Tier 1: Mod Root `README.md` (`mods/<ModName>/README.md`)
* **Audience:** Players and users.
* **Strict Rule:** **ZERO source code references.** Do not mention C# classes, methods, Harmony annotations, or code snippets.
* **Contents:**
  - Natural language summary of what the mod does from the player's perspective.
  - List of user-facing features and gameplay adjustments.
  - Compatibility notes and prerequisites (game version, BepInEx 5).
  - Pointers to `Patches/README.md` for technical implementation.

### Tier 2: Subfolder `README.md` Guides
* **`Patches/README.md`:** Technical map for developers and AI agents. Documents each Harmony patch class, hooked game methods, prefix/postfix behavior, and manipulated game variables.
* **`Assets/README.md`:** (when loose asset pipeline is active) Documents custom external `.png` assets, dimensions, and runtime loading via `AssetLoader`.

---

## 6. Operational Golden Rules for AI Agents

1. **Never decompile framework or BCL assemblies:** Only decompile assemblies categorized as `AssemblyTier.CoreGame` or `AssemblyTier.GameModular`.
2. **Never pass arbitrary file paths to `game-launch`:** `game-launch` strictly accepts registered game names from `%APPDATA%/Candied/registered_games.json`.
3. **Never assume HTTP 200 click implies in-game effect:** Synthetic input injection returns HTTP 200 when the event is queued in the engine. **Always capture a follow-up `screenshot`** to verify observable scene changes.
4. **Coordinate Space Invariant:** In `click(x, y)`, coordinates are relative to the screenshot ($[0..width-1] \times [0..height-1]$, **origin top-left**). Candied automatically handles conversion to Unity Screen space.
5. **Always inspect compiler diagnostics on build failure:** `build` returns structured diagnostic items (`file`, `line`, `message`). Use them to directly fix compilation errors before retrying.
6. **Test on a copy.** Mods and experiments run on a test copy of the game, never on the user's own installation or save files, unless the user explicitly says otherwise.
7. **Never claim a visual result you did not see.** Cite the screenshot path and describe what is in it. If a frame is uniform or looks half-loaded, wait and retake it.
8. **Know the screenshot's blind spots.** Check `screenshotMethod` in `capabilities`. Overlay UI and post-processing may be missing, so a mod that changes them cannot be verified visually.
9. **Report limits honestly.** If a click has no effect after two well-placed attempts, report it. Do not try random coordinates and do not call it a success.
10. **Ask before changing the machine.** `register-game`, `unregister-game`, `install-bridge`, `setup` and `rollback` run only on explicit user request.
11. **Always end a live session with `game-close`,** and close the game before running `build`.
12. **Underspecified request: research and questions first.** Do not implement a large feature on assumptions (Playbook H).
13. **Keep mod data out of the game's save files** unless the user explicitly asks for it.