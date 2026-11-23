# Architecture Reference

## 1. Module Map

```
┌─────────────────────────────────┐   ┌─────────────────────────────────┐
│         Candied.Agent           │   │         Candied.Studio          │  (Presentation Layer: CLI, MCP, WPF Host)
└────────────────┬────────────────┘   └────────────────┬────────────────┘
                 │                                     │
                 └──────────────────┬──────────────────┘
                                    │
┌───────────────────────────────────▼───────────────────────────────────┐
│                            Candied.Core                               │  (Domain Rules & Use Cases)
└─────────────────┬───────────────────────────────────┬─────────────────┘
                  │                                   │
┌─────────────────▼──────────────┐   ┌────────────────▼─────────────────┐
│      Candied.Decompiler        │   │        Candied.Workspace         │  (Infrastructure Adapters)
│      (Decompile & Index)       │   │   (Scaffolding, Build, Logs)     │
└────────────────────────────────┘   └──────────────────────────────────┘
                                    │
┌───────────────────────────────────▼───────────────────────────────────┐
│                         Candied.Primitives                            │  (Foundational Value Types)
└───────────────────────────────────────────────────────────────────────┘
```

| Module | Single Responsibility | Reason to Change | Forbidden Behaviors (MUST NOT) |
|---|---|---|---|
| **`Candied.Primitives`** | Fundamental types, result monad, strongly-typed IDs | Primitive type changes | No external dependencies, no domain/business logic, no I/O. |
| **`Candied.Core`** | Domain modeling, use cases, port interfaces, pure business rules | Business/workflow rules | No direct I/O, no filesystem calls (`System.IO.File`), no process execution (`Process.Start`), no transport serialization. |
| **`Candied.Decompiler`** | Assembly decompilation, Roslyn symbol extraction, SQLite FTS index storage | Decompiler updates, Roslyn AST changes, index schema changes | No mod project scaffolding, no build execution, no MCP transport. |
| **`Candied.Workspace`** | Mod project (.csproj) scaffolding, BepInEx publicizer setup, MSBuild runner, LogOutput tailing | BepInEx folder changes, MSBuild target changes, log format shifts | No symbol parsing, no agent protocol handling, no decompilation logic. |
| **`Candied.Agent`** | Presentation layer: CLI commands and MCP (Model Context Protocol) stdio transport | MCP schema changes, CLI argument/flag changes, prompt format changes | No business logic, no direct file/database operations, no decompiler calls. |
| **`Candied.Studio`** | Presentation layer: Interactive WPF host workbench for generic Unity Mono games, Win32 window embedding (`-parentHWND` & `SetParent`), Job Object lifecycle control, and real-time log monitoring | Studio UI layout, embedding heuristics, controller UX | No direct domain bypass, must strictly consume Core use cases and workspace infrastructure. |

---

## 2. Allowed Dependency Graph

```
Candied.Primitives            -> (None)
Candied.VisionBridge.Protocol -> (None)
Candied.VisionBridge          -> Candied.VisionBridge.Protocol
Candied.Core                  -> Candied.Primitives, Candied.VisionBridge.Protocol
Candied.Decompiler            -> Candied.Core, Candied.Primitives
Candied.Workspace             -> Candied.Core, Candied.Primitives, Candied.VisionBridge.Protocol
Candied.Agent                 -> Candied.Core, Candied.Decompiler, Candied.Workspace, Candied.Primitives
Candied.Studio                -> Candied.Core, Candied.Decompiler, Candied.Workspace, Candied.Primitives, Candied.VisionBridge.Protocol
```

### Dependency Rules:
1. `Candied.Core` MUST NOT reference `Candied.Decompiler`, `Candied.Workspace`, `Candied.Agent`, or `Candied.Studio`.
2. `Candied.Decompiler` and `Candied.Workspace` MUST NOT reference each other.
3. `Candied.Primitives` MUST NOT reference any other module.
4. Circular dependencies are strictly forbidden and blocked by MSBuild.
5. All data crossing module boundaries must be immutable C# `record` types or read-only collections (`IReadOnlyList<T>`).

---

## 3. Public Contracts Summary

### `Candied.Core` (Ports / Inbound & Outbound)
- `IGameDetector`: Detects Unity engine type (Mono vs IL2CPP), architecture (x64/x86), .NET target framework, classified assembly catalog, and BepInEx installation status with version detection.
- `IDecompilerEngine`: Decompiles game assemblies to `.candied/games/<GameName>/decompiled/` with intelligent timestamp/size caching.
- `ISymbolIndex`: Indexes Roslyn AST into SQLite; searches symbols (supporting exact kind, enum-only, namespace prefix, and result limits); performs FTS5 full-text source code search (`SearchCodeAsync`) with automatic backfill on pre-existing indexes; retrieves full type definitions with assembly name and relative source path.
- `IProjectGenerator`: Generates BepInEx mod project (`.csproj`, `Plugin.cs`, `Patches/`).
- `IProjectReferenceEditor`: Dedicated port for safely mutating `.csproj` `<Reference>` entries (`AddManagedReferenceAsync`) with verified `Managed/*.dll` path resolution and optional `<Publicize>true</Publicize>`.
- `IDocumentationGenerator`: Generates Knowledge Hub artifacts (`GAME_OVERVIEW.md`, `NAMESPACES.md`, `SYMBOLS_INDEX.json`, `RESEARCH.md`) in Zone 3.
- `IBuildRunner`: Executes `dotnet build` on mod project, returns structured compiler diagnostics enriched with `MissingReferenceHints` for `CS0012` missing-assembly errors.
- `ILogMonitor`: Tails `BepInEx/LogOutput.log`, streams log entries, identifies runtime errors.
- `IEnvironmentProvisioner`: Downloads and extracts architecture-matched BepInEx releases, configures logging, generates `.candied_manifest.json`, and safely uninstalls tracked files.
- `IGameProcessManager`: Controls Unity game execution lifecycle with attach-first detection, windowed launch, and clean process termination.
- `IAssetIndex`: Manages persistent SQLite asset metadata storage, indexing, and paginated full-text search (`SearchAssetsPageAsync` returning `AssetDiscoveryPage(TotalMatches, Assets)`) for sub-5ms discovery.
- `IAssetExtractor`: Discovers and extracts Unity visual assets (Sprites, Texture2Ds), text/data assets (TextAsset), and configuration components (MonoBehaviour/ScriptableObject) from `.assets` archives and AssetBundles directly to files offline without running the game.
- `InspectGameEnvironmentUseCase`: Pure non-destructive game audit and assembly tier classification without decompilation.
- `SetupGameEnvironmentUseCase`: Provisions the matching BepInEx runtime into Zone 1 with manifest recording.
- `RollbackGameEnvironmentUseCase`: Safely uninstalls BepInEx and restores original game folder state via `.candied_manifest.json`.
- `DecompileAssemblyUseCase`: Targeted single-assembly decompilation into isolated assembly folder, incremental SQLite index update, and automated Knowledge Hub synchronization.
- `SearchGameSymbolsUseCase`: Fast symbol querying against the active SQLite index.
- `SearchGameCodeUseCase`: FTS5 + exact substring search across decompiled type source bodies in the active SQLite index.
- `AddProjectReferenceUseCase`: Validates and adds verified managed assembly references to a mod `.csproj` via `IProjectReferenceEditor`.
- `SaveResearchNotesUseCase`: Persists agent research notes to Zone 3 (`RESEARCH.md`).
- `GetTypeDetailUseCase`: Retrieves decompiled member signatures, AST details, and raw source code from target SQLite index.
- `GetGameProcessStatusUseCase`: Checks process execution status.
- `LaunchGameUseCase`: Initiates controlled windowed game launch or attaches to existing running game.
- `StopGameUseCase`: Gracefully shuts down the target game process.
- `IndexAssetsUseCase`: Scans all Unity game archives and indexes asset metadata into Zone 3 SQLite (`index.db`).
- `DiscoverAssetsUseCase`: Scans or fast-queries indexed Unity assets matching filters, types, containers, PathIDs, and pagination offsets (`ExecutePageAsync`).
- `ExtractAssetsUseCase`: Extracts visual assets (PNG), text assets (TXT/JSON/CSV/XML), and configurations (JSON) directly to Zone 3 (`.candied/games/<GameName>/assets/`).
- `IVisionBridgeClient`: HTTP communication port interacting with in-game `Candied.VisionBridge` HTTP server (`/hello`, `/capabilities`, `/screenshot`, `/ui/elements`, `/scene`, `/scene/objects`, `/assets/loaded`, `/ui/click`, `/input/click`, `/wait-idle`).
- `IRegisteredGameRegistry`: Manages the user's allowlist of registered Unity Mono games (`%APPDATA%/Candied/registered_games.json`), strictly rejecting IL2CPP runtimes.
- `IStudioSessionManager`: Atomically persists and retrieves the active Candied Studio session metadata (`studio_session.json`), actively verifying process liveness to purge stale sessions.
- `IStudioClient`: Communicates with the Candied Studio host via local HTTP (`GET /status`, `POST /launch`, `POST /close`) and spawns Studio as an independent detached process (`UseShellExecute = true`).
- Pure Domain Analyzers (`Candied.Core.Analysis`):
  - `PurePngDecoder`: Pure domain PNG scanline decoder using standard BCL `ZLibStream` to calculate in-memory pixel diffs and draw visual click markers.
  - `TypeMemberSourceExtractor`: Pure brace/string/comment-aware C# member block extractor supporting `type(memberFilter=...)`.
  - `MissingReferenceHintAnalyzer`: Pure locale-independent `CS0012` missing assembly name extractor.

---

## 4. Three-Zone Workspace Storage Architecture

Candied strictly isolates game files, developer mod sources, and intermediate reverse-engineering artifacts across three immutable zones:

```
┌────────────────────────────────────────────────────────────────────────┐
│                        THREE-ZONE STORAGE LAYOUT                       │
└────────────────────────────────────────────────────────────────────────┘

  [ZONE 1: Game Installation]       (READ-ONLY except post-build deploy & tracked provisioning)
    └── Target game directory (Steam, GOG, etc.)
    └── NEVER contaminated with intermediate decompiled sources or SQLite databases.
    └── Permitted writes:
        - BepInEx/plugins/<ModName>/<ModName>.dll on mod build.
        - Managed BepInEx environment files, strictly tracked in `.candied_manifest.json` for 100% reversible rollback.

  [ZONE 2: Development Workspace]   (READ/WRITE, Version-Controlled)
    └── <RepoRoot>/mods/<ModName>/
        ├── <ModName>.csproj
        ├── Plugin.cs
        └── Patches/

  [ZONE 3: Candied Artifacts Store] (READ/WRITE, Local & Git-Ignored)
    └── <RepoRoot>/.candied/
        ├── cache/bepinex/           <-- Global zip cache for BepInEx release archives
        └── games/<GameName>/
            ├── decompiled/          <-- Isolated C# source code per game
            │   └── <AssemblyName>/  <-- Isolated per-assembly source folder
            │       └── .{Assembly}.cache<-- Incremental cache hashes
            ├── index.db             <-- SQLite symbol & type detail index
            ├── docs/                <-- Knowledge & Learning Hub
            │   ├── GAME_OVERVIEW.md <-- Architectural overview & key managers
            │   ├── NAMESPACES.md    <-- Namespace & class cheat-sheet
            │   └── SYMBOLS_INDEX.json<-- Fast hierarchical symbol lookup map
            ├── ipc/                 <-- File-based IPC commands & responses with Candied companion bridge
            └── assets/              <-- On-demand extracted PNG sprites and textures
```

Managed via the pure domain value object `CandiedStorageLayout` (zero `System.IO` dependencies).

### Infrastructure Modules (`Candied.Decompiler`, `Candied.Workspace`)
- Expose only dependency injection registration extensions (`AddDecompilerInfrastructure()`, `AddWorkspaceInfrastructure()`).
- All concrete implementation classes are marked `internal`.

---

## 5. Pure Logic vs Side Effects (Ports & Adapters)

- **Pure Domain (`Candied.Core`)**:
  - Pure calculation of project configurations, patch templates, and symbol relevance.
  - Zero side effects: no I/O, no DB calls, no external process spawning.
- **Side Effects (`Candied.Decompiler`, `Candied.Workspace`)**:
  - Implements ports defined in `Core`.
  - Handles disk I/O, SQLite connections, process invocation (`dotnet build`), and network/stdio streams.
- **Composition Root**:
  - Resides exclusively in `Candied.Agent` (`Program.cs`) via `Microsoft.Extensions.DependencyInjection`.

---

## 6. Multi-Level Fractal Modularity (Macro to Micro Architecture)

Candied enforces modularity not just across assemblies (macro), but down into individual modules, subsystems, and classes (micro):

```
Level 1: System Macro-Architecture (Projects & Boundaries)
  └── Ports & Adapters, strict assembly hierarchy, ArchUnitNET boundary verification.

Level 2: Module Internal Architecture (Subsystems within Assemblies)
  ├── Single Port per Adapter: Each adapter class implements exactly one port.
  │   ├── UnityGameDetectorAdapter : IGameDetector
  │   ├── ILSpyDecompilerEngineAdapter : IDecompilerEngine
  │   ├── SqliteSymbolIndexAdapter : ISymbolIndex
  │   ├── SqliteAssetIndexAdapter : IAssetIndex
  │   ├── AssetsToolsAssetExtractorAdapter : IAssetExtractor
  │   ├── DiskProjectGeneratorAdapter : IProjectGenerator
  │   ├── DiskProjectReferenceEditorAdapter : IProjectReferenceEditor
  │   ├── DotNetBuildRunnerAdapter : IBuildRunner
  │   ├── BepInExLogMonitorAdapter : ILogMonitor
  │   ├── DiskDocumentationGeneratorAdapter : IDocumentationGenerator
  │   ├── BepInExEnvironmentProvisionerAdapter : IEnvironmentProvisioner
  │   └── OsGameProcessManagerAdapter : IGameProcessManager
  └── No "God Adapters": Subsystems are cleanly separated into dedicated classes.

Level 3: Component & Class Architecture (Lower Abstractions)
  ├── Asset Extraction & Decoding Subsystems:
  │   ├── UnityAssetContainerScanner (Memory-bounded sequential archive discovery)
  │   ├── UnityCrossFileResolver (Sprite external texture dependency resolution)
  │   ├── UnityTextureDecoder (Atlas rect slicing & 90° packing un-rotation)
  │   ├── UnityMultiTypeAssetExtractor (Texture2D, Sprite, TextAsset, MonoBehaviour serialization)
  │   └── FileNameDisambiguator (Collision-safe file writing via PathID)
  ├── Presentation & Window Embedding Subsystems (Candied.Studio):
  │   ├── UnityHwndHost (Win32 HwndHost container with DPI-aware resizing and airspace isolation)
  │   ├── WindowsJobObject (Win32 Job Object with JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE)
  │   ├── UnityWindowLocator (Toolhelp32 BFS process tree traversal & UnityWndClass locator)
  │   ├── Win32WindowEmbedder (Window style calculation, SetParent, and SetWindowPos)
  │   ├── UnityGameHostController (Game validation, IL2CPP rejection, launch, fallback, and log tailing)
  │   └── MainWindowViewModel (MVVM state management, asynchronous commands, and UI log buffering)
  ├── Pure Template Renderers & XML Mutators:
  │   ├── CsprojTemplateRenderer (MSBuild XML generation)
  │   ├── CsprojReferenceMutator (Pure XDocument <Reference> injection & Managed/ path extraction)
  │   ├── PluginTemplateRenderer (BaseUnityPlugin C# generation)
  │   ├── HarmonyPatchTemplateRenderer (Harmony patch template generation)
  │   ├── GameOverviewTemplateRenderer (GAME_OVERVIEW.md guide generation)
  │   ├── NamespaceMapTemplateRenderer (NAMESPACES.md cheat-sheet generation)
  │   ├── SymbolIndexJsonRenderer (SYMBOLS_INDEX.json machine map generation)
  │   ├── BepInExConfigTemplateRenderer (BepInEx.cfg configuration generation)
  │   ├── AssetLoaderTemplateRenderer (Runtime loose sprite loader utility)
  │   └── GenericBridgeTemplateRenderer (Pure generic Unity BepInEx companion bridge code)
  ├── VisionBridge Runtime Inspectors (`Candied.VisionBridge`):
  │   ├── SceneObjectsInspector (Filtered camera/renderer hierarchy, local transforms, sprite pivot, components)
  │   └── RuntimeAssetsInspector (Pure-reflection loaded AssetBundle & Resources asset discovery)
  ├── Pure Analyzers:
  │   ├── GameArchitectureAnalyzer (Analyzes symbols, namespaces, and gameplay managers)
  │   ├── TypeMemberSourceExtractor (Brace/string/comment-aware C# member block extraction)
  │   └── MissingReferenceHintAnalyzer (Locale-independent CS0012 missing assembly extraction)
  └── Pure Text & Diagnostic Parsers:
      ├── MsBuildDiagnosticParser (Roslyn error/warning regex & deduplication)
      └── BepInExLogParser (Regex log header matching & stack trace stitching)
```

---

## 7. Candied.Studio Architecture (Interactive Game Host Workbench)

`Candied.Studio` is an interactive desktop host application built with WPF and .NET 9. It provides runtime embedding and lifecycle orchestration for generic Unity Mono games.

### Key Architectural Invariants for Studio:
1. **Generic & Zero Hardcoding:** Operates on any Unity Mono standalone game selected at runtime via file picker. Absolutely no game names or game paths are hardcoded.
2. **Strict IL2CPP Rejection:** Before launch, the target game environment is inspected via `IGameDetector`. If `UnityEngineType.Il2Cpp` is detected, execution is immediately rejected with an explicit, user-friendly Italian message.
3. **Win32 Airspace Isolation:** Due to WPF's airspace limitation (`HwndHost` renders over any overlapping WPF controls), controls and logs are strictly arranged side-by-side using a two-column `Grid` with a `GridSplitter`:
   - Column 0: `UnityHwndHost` containing the native Win32 child window.
   - Column 2: Dashboard with game status, embedding indicators, launch controls, and real-time log output.
4. **Deterministic Lifetime (Windows Job Objects):** All spawned processes are assigned to a Windows Job Object configured with `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE`. When `Candied.Studio` is closed or if it experiences an abnormal crash, the operating system unconditionally terminates the game and all spawned child processes.
5. **Two-Tier Embedding Strategy:**
   - *Tier 1 (Direct Launch):* Game is spawned with `-parentHWND <hwnd> -popupwindow -screen-fullscreen 0 -screen-width <w> -screen-height <h>`.
   - *Tier 2 (Fallback Heuristics):* If no child window attaches to the container within 5 seconds (e.g. launcher/Steam process respawn), `UnityWindowLocator` performs BFS process tree traversal (`CreateToolhelp32Snapshot`) and searches for `UnityWndClass`. Once found, `Win32WindowEmbedder` strips top-level styles (`WS_POPUP`, `WS_CAPTION`, `WS_THICKFRAME`), applies `WS_CHILD | WS_VISIBLE | WS_CLIPCHILDREN`, calls `SetParent`, and synchronizes dimensions via `SetWindowPos`.
6. **Pure Business Logic Separation:** All embedding, locator, Job Object, and lifecycle management logic resides in decoupled, testable services (`IWindowsJobObject`, `IUnityWindowLocator`, `IWindowEmbedder`, `IUnityGameHostController`), completely free from WPF code-behind.

---

## 8. Candied.VisionBridge Subsystem (In-Game HTTP Bridge & Vision Protocol)

The VisionBridge subsystem bridges the AI Agent / Host workbench with the running Unity Mono game process, enabling pixel-accurate visual perception and semantic UI inspection.

```
┌────────────────────────────────┐                 ┌────────────────────────────────┐
│   Candied.Studio / MCP Host    │                 │   Unity Mono Game (In-Process) │
│                                │                 │                                │
│   IVisionBridgeClient          │─── HTTP/JSON ──▶│   HttpListener (127.0.0.1)     │
│   (HttpVisionBridgeClient)     │   X-Candied-    │   VisionHttpServer             │
│                                │     Token       │         │                      │
│   - GET /hello                 │                 │         ▼                      │
│   - GET /capabilities          │                 │   MainThreadDispatcher         │
│   - GET /screenshot (PNG)      │                 │   (drained in Update())        │
│   - GET /ui/elements (uGUI)    │                 │         │                      │
│   - POST /ui/click             │                 │   ┌─────┴──────────────────┐   │
│   - GET /wait-idle             │                 │   ▼                        ▼   │
│                                │                 │ UguiProvider      Screenshot   │
│   Overlay Renderer:            │                 │ (IUiProvider)      Capturer    │
│   Draws bounding rects directly│                 │ (EventSystem)    (PNG Encode)  │
│   onto captured screenshot     │                 └────────────────────────────────┘
└────────────────────────────────┘
```

### Architectural Principles of VisionBridge:
1. **Multi-Targeting Compatibility:**
   - `Candied.VisionBridge.Protocol` multi-targets `net35;net472;netstandard2.0;net9.0`. It contains pure shared DTOs, `CoordinateConverter`, and `JsonLite` (a lightweight, zero-dependency JSON parser/serializer).
   - `Candied.VisionBridge` multi-targets `net35;net472` using `Microsoft.NETFramework.ReferenceAssemblies`, guaranteeing seamless loading on both legacy .NET 3.5 Unity runtimes and modern .NET 4.x/Unity 2021+ runtimes under BepInEx 5.
2. **Pure Reflection for Optional Unity Modules:**
   - Optional modules (`UnityEngine.UI`, `UIElements`, `InputSystem`, `ScreenCapture`) are never statically referenced. The plugin uses `CapabilitiesDetector` reflection lookups so games lacking a specific module never throw runtime type load exceptions.
3. **Strict Security & Zero CORS:**
   - Bound strictly to `127.0.0.1`. Port is dynamic (passed by host via `CANDIED_PORT`), authentication token is random per-session (passed via `CANDIED_TOKEN`).
   - Every request requires matching `X-Candied-Token` header. Missing or invalid token immediately responds with HTTP 401 and an empty body (`ContentLength64 = 0`) to prevent credential leakage. Tokens are never logged.
   - `Host` header must start with `127.0.0.1`; unexpected `Origin` or `Host` headers are rejected with HTTP 403.
4. **Thread-Safe Main Thread Dispatching:**
   - Any API accessing the Unity engine (`GameObject`, `Transform`, `Camera`, `EventSystem`, `Screen`, `InputSystem`) is marshaled through `MainThreadDispatcher` and drained on `Update()`. Requests wait synchronously with timeouts, bubbling exceptions back as structured JSON errors (`code`, `message`, `suggestion`).
5. **Robust Screen Capture with Camera Fallback:**
   - Capturing screenshots via standard `ScreenCapture.CaptureScreenshotAsTexture` or raw backbuffer `ReadPixels` fails in embedded child windows (`-parentHWND`) because the OS compositor redirects the top-level swapchain.
   - `ScreenshotCapturer` detects and employs an off-screen camera rendering pipeline (`CaptureViaCameras`): all active cameras are sorted by depth and rendered to an off-screen `RenderTexture`, followed by `ReadPixels` from the active render texture. This guarantees pixel-accurate frame capture regardless of window embedding mode.
   - The active capture method (`camera_fallback` vs `screen_capture`) and known limitations (e.g. `ScreenSpaceOverlay` canvases and screen-space post-processing passes occurring after cameras are not captured by camera rendering) are reported dynamically in `GET /capabilities`.
   - External capture via Win32 `BitBlt` on DirectX/Vulkan child windows is subject to desktop composition boundaries, returning 0/black without Desktop Duplication / `Windows.Graphics.Capture`.
6. **Viewport Letterboxing, 16:9 Cinema Stage & Native Coordinate Transformation:**
   - Empirical verification in child windows (`WS_CHILD` with `-parentHWND`) revealed that invoking `Screen.SetResolution(w, h, false)` forces Unity to renegotiate display monitor bounds via Win32 `MonitorFromWindow`. Because child windows lack dedicated desktop monitor handles, this fails with invalid window handle errors, detaches the swap chain, and permanently halts frame rendering.
   - Consequently, dynamic viewport adjustments are performed via 16:9 launch (`-screen-width 1280 -screen-height 720 -screen-fullscreen 0`) paired with a strict **16:9 WPF Cinema Stage Bezel** (`ViewportCalculator.CalculateStageViewport`, supporting presets `auto_16_9`, `960x540`, `1280x720`, and `1920x1080`) and host-side aspect-ratio letterboxing / pillarboxing:
     * $TargetAspect = \frac{1920}{1080} = \frac{16}{9}$
     * Because Unity cameras and `CanvasScaler` layouts scale proportionally to aspect ratio, locking the embedded container to exact 16:9 ensures the field of view and UI layout match `1920x1080` identically even inside compact viewports (`960x540` or `1280x720`), while keeping native `Screen.width x Screen.height` 1:1 with `CandiedVirtualMouse` and `ScreenshotCapturer`.
     * $Reference1080pX = \lfloor\frac{FrameX \times 1920}{FrameWidth}\rfloor$, $Reference1080pY = \lfloor\frac{FrameY \times 1080}{FrameHeight}\rfloor$.
   - Bidirectional coordinate mapping:
     * Game to Panel: $PanelX = LetterboxX + \lfloor\frac{GameX \times LetterboxW}{GameWidth}\rfloor$, $PanelY = LetterboxY + \lfloor\frac{GameY \times LetterboxH}{GameHeight}\rfloor$
     * Panel to Game: $GameX = \lfloor\frac{(PanelX - LetterboxX) \times GameWidth}{LetterboxW}\rfloor$, $GameY = \lfloor\frac{(PanelY - LetterboxY) \times GameHeight}{LetterboxH}\rfloor$ (clamped to $[0, GameWidth - 1]$ and $[0, GameHeight - 1]$).
7. **Empirical Windows.Graphics.Capture (WGC) vs In-Game Capture:**
   - WGC via `IGraphicsCaptureItemInterop.CreateForWindow` strictly rejects `WS_CHILD` windows with Win32 HRESULT `0x80070057` (`E_INVALIDARG`).
   - Top-level host HWND succeeds with WGC, capturing the complete letterboxed container.
   - `ScreenshotCapturer` inside `Candied.VisionBridge` clears `RenderTexture.active = null` before reading backbuffer pixels to prevent contamination by active render targets, providing clean frame captures.
8. **Airspace-Safe UI Visual Overlay:**
   - Overcoming WPF's Win32 airspace barrier, UI bounding boxes and hierarchical element tags are rendered directly onto the captured screenshot bitmap (`DrawingVisual` + `RenderTargetBitmap`) within the side dashboard, providing 100% pixel-accurate visualization with zero airspace conflicts.
9. **In-Process Internal Input Injection (`POST /input/click`) & Isolated Virtual Input Devices:**
   - External OS-level synthetic input (`SendInput`) did not work in background, cause not isolated.
   - `Candied.VisionBridge` implements generic in-process input isolation and injection via `VirtualInputDeviceManager`, `VirtualInputStateTracker`, and `InputClicker`:
     * **Virtual Device Provisioning & Physical Mouse Isolation:** Using pure reflection on `UnityEngine.InputSystem`, `VirtualInputDeviceManager` creates dedicated software devices (`CandiedVirtualMouse` and `CandiedVirtualKeyboard` via `InputSystem.AddDevice`), disables physical OS hardware mouse devices (`InputSystem.DisableDevice(dev, false)`), promotes `CandiedVirtualMouse` via `MakeCurrent()`, and continuously pins the virtual mouse state on every frame (`OnFrameUpdate`). This prevents physical OS mouse movements by the user from ever overwriting the in-game cursor position.
     * **Legacy Input & Focus Harmony Interception:** Generic Harmony postfixes on `UnityEngine.Application.isFocused`, `UnityEngine.Input.mousePosition`, `UnityEngine.Input.mousePresent`, and `UnityEngine.Input.GetMouseButton*` read directly from `VirtualInputStateTracker` when isolated, ensuring games using Legacy Input or hybrid checks also remain isolated from the physical OS mouse and never pause when running in the background.
     * **Pre-Hover & Post-Release Stabilization:** Coordinates are converted from image-space (top-left origin, Y down) to Unity screen space (bottom-left origin, Y up) via `CoordinateConverter.ImageToUnityScreenPoint`. `InputClicker` schedules:
       - Step 1 (Pre-Hover): Holds `(screenX, screenY)` with `buttons = 0` for 4 frames so game collider/hover systems register the target before `MouseDown`.
       - Step 2 (Press & Hold): Queues `buttons = (1 << button), clickCount = 1` across `holdFrames` (default 6).
       - Step 3 (Release & Settle): Queues `buttons = 0` for 3 frames so UI transitions settle before post-click screenshot capture.
   - **Empirical Rigorous Protocol & Finding (Status: PROVEN ONCE ON ONE GAME, RELIABILITY NOT MEASURED):**
     * Note on earlier run: The initial reported result of "79.03% diff, click verified" was INVALID because the pre-click frame captured the loading splash screen transition, measuring game loading rather than button click response. Position/hover was proven, but click was NOT proven.
     * Re-executed under strict, pre-committed criteria on real Main Menu (post-25s loading wait & RGB validation at (511, 416) $R=205, G=173, B=130$):
       1. Stability: Main Menu stable when two frames 1s apart differ by $< 1.0\%$ (passed: **0.00%** diff, saved `screen_01_baseline_stable.png`).
       2. Negative control: Click on empty area $(100, 100)$ must differ from baseline by $< 2.0\%$ (passed: **0.00%** diff, saved `screen_02_negative_control.png`).
       3. Solo hover: Cursor moved to Settings $(511, 416)$ without pressing (button highlighted, diff **1.28%** on Central ROI, saved `screen_03_hover.png`).
       4. Success threshold: Diff between Hover and Post-click $> 30.0\%$ on Central ROI $[200, 100, 624, 500]$ OR presence of Settings screen (title/elements different from Main Menu).
       5. Measured results:
          - Method 1 (Input System, 6 frames hold): **7.14%** diff (22,277 / 312,000 pixels changed on Central ROI).
          - Visual confirmation in artifact `screen_04_after_click.png`: The Settings screen opened completely with subtitle "Settings" and options "Game Settings", "Controls", "Accessibility Settings" (hovered by cursor at 511, 416), "Video Settings", "Sound Settings", "Back".
          - Reason for ~7% diff: Unity Mono Target Game renders on a static alchemical parchment backdrop with unchanged laboratory apparatus; the 22,277 changed pixels account for the entirety of the text replacement.
          - Status: Click via Input System tested once on one game (Unity Mono Target Game), reliability not measured. Saved `screen_04_after_click.png` (1,771,255 bytes). In-process internal mouse injection via `UnityEngine.InputSystem.QueueStateEvent<MouseState>` with multi-frame hold (6 frames) and Win32 message synchronization successfully activates button interactions in embedded child windows without physical OS cursor manipulation, but must always be verified by follow-up screenshots. Legacy and PostMessage methods remain implemented but untested live.

---

## 9. What We Do NOT Build Now

1. **No IL2CPP runtime unhollowing / Cpp2IL**: Target is strictly Unity Mono games. IL2CPP runtime is rejected with an explicit diagnostic message.
2. **No internal LLM orchestration in Core**: The codebase serves external AI agents via the MCP protocol and interactive developers via Studio; it does not invoke external LLM APIs itself.
3. **No cloud synchronization / multi-user workspace**: Strictly single-developer local filesystem operation.
