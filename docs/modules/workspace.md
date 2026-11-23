# Module: Candied.Workspace

## 1. Overview & Purpose
`Candied.Workspace` is an infrastructure adapter module responsible for all operations on editable mod codebases:
1. **Mod Project Scaffolding:** Generating standard BepInEx 5 plugin projects targeting `netstandard2.1` with `BepInEx.AssemblyPublicizer.MSBuild` and automatic post-build deployment.
2. **Deterministic Build Runner:** Executing `dotnet build` via subprocess, capturing standard output/error, and parsing MSBuild/Roslyn compiler diagnostics into structured error records for AI self-debugging.
3. **Runtime Log Monitoring:** Interfacing with BepInEx runtime logs (`LogOutput.log`).
4. **Automated Environment Provisioning & Rollback:** Installing architecture-matched BepInEx releases, pre-configuring logging, creating an installation manifest (`.candied_manifest.json`) in the game directory, and offering safe uninstallation/rollback without touching vanilla assets.

This module implements the ports defined in `Candied.Core.Ports` and keeps all concrete classes `internal` behind public dependency injection registration.

---

## 2. Ports Implemented & Sub-Component Architecture

In compliance with Multi-Level Fractal Modularity (Rule 8 in `AGENTS.md`), each adapter implements **exactly one port** and delegates pure parsing and template rendering to dedicated pure sub-components:

```
Candied.Workspace/
├── DependencyInjection.cs               <-- Sole public entry point (AddWorkspaceInfrastructure)
├── Adapters/
│   ├── DiskProjectGeneratorAdapter.cs   <-- Implements IProjectGenerator (I/O orchestration only)
│   ├── DotNetBuildRunnerAdapter.cs      <-- Implements IBuildRunner (Process lifecycle only)
│   ├── BepInExLogMonitorAdapter.cs      <-- Implements ILogMonitor (FileStream reading only)
│   ├── BepInExEnvironmentProvisionerAdapter.cs <-- Implements IEnvironmentProvisioner (Download, extraction, manifest, rollback)
│   ├── DiskDocumentationGeneratorAdapter.cs <-- Implements IDocumentationGenerator (Writes Zone 3 docs)
│   └── OsGameProcessManagerAdapter.cs   <-- Implements IGameProcessManager (Attach-first, windowed launch, process termination)
├── Rendering/                           <-- Pure Template Renderers (Zero I/O, fully testable in memory)
│   ├── CsprojTemplateRenderer.cs        <-- MSBuild project XML generator with publicizer rules
│   ├── PluginTemplateRenderer.cs        <-- BaseUnityPlugin bootstrap C# generator
│   ├── HarmonyPatchTemplateRenderer.cs  <-- Harmony patch boilerplate generator
│   ├── ModRootReadmeTemplateRenderer.cs <-- Natural-language mod overview generator (zero code references)
│   ├── PatchesReadmeTemplateRenderer.cs <-- Gameplay hook & Harmony mechanics guide generator
│   ├── AssetsReadmeTemplateRenderer.cs  <-- Loose asset architecture & disk loading guide generator
│   ├── AssetLoaderTemplateRenderer.cs   <-- Loose sprite and texture loader C# generator
│   ├── BepInExConfigTemplateRenderer.cs <-- BepInEx.cfg INI configuration generator (Console & Disk logging)
│   ├── GameOverviewTemplateRenderer.cs  <-- Generates GAME_OVERVIEW.md guide
│   ├── NamespaceMapTemplateRenderer.cs  <-- Generates NAMESPACES.md cheat-sheet
│   └── SymbolIndexJsonRenderer.cs       <-- Generates SYMBOLS_INDEX.json machine map
├── Analysis/                            <-- Pure Analyzers (Zero I/O)
│   └── GameArchitectureAnalyzer.cs      <-- Analyzes symbols to detect managers, controllers, namespaces
└── Parsing/                             <-- Pure Text Parsers (Zero I/O, regex & AST extraction)
    ├── MsBuildDiagnosticParser.cs       <-- Roslyn compiler diagnostic parser & deduplicator
    └── BepInExLogParser.cs              <-- BepInEx log line parser & multiline stack trace stitcher
```

---

## 3. Component Details

### A. Scaffolding Pipeline (`DiskProjectGeneratorAdapter` + `Rendering/`)
Generates a complete, ready-to-compile BepInEx mod repository on disk. Filesystem orchestration is decoupled from template content generation:

* **Target Directory Layout:**
  ```
  <ModRootDirectory>/
  ├── <ModName>.csproj
  ├── Plugin.cs
  ├── AssetLoader.cs         (opt-in when IncludeAssetPipeline = true)
  ├── README.md              <-- Natural language player overview (zero source code references)
  ├── Patches/
  │   ├── ExamplePatch.cs
  │   └── README.md          <-- Implementation guide for gameplay hooks & Harmony mechanics
  └── Assets/                (opt-in when IncludeAssetPipeline = true)
      └── README.md          <-- Implementation guide for loose assets & runtime disk loading
  ```

* **`CsprojTemplateRenderer` (`Rendering/CsprojTemplateRenderer.cs`):**
  - **Target Framework:** `netstandard2.1` (standard for Unity Mono and BepInEx 5).
  - **Language Version:** `latest` with nullable annotations enabled (`<Nullable>enable</Nullable>`).
  - **Unsafe Code:** `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>` for Harmony memory operations.
  - **Analyzer & Audit Warning Suppression:** Automatically injects `<NoWarn>$(NoWarn);CA1707;NU1900;NU1901;NU1902;NU1903;NU1904</NoWarn>` to prevent standard Harmony double-underscore parameters (`__instance`, `__result`, `__state`, `__args`) and BepInEx NuGet feed audit warnings from failing builds when warnings as errors are enabled.
  - **NuGet Packages:**
    - `BepInEx.Core` (`5.4.21`): Core plugin framework and HarmonyX.
    - `BepInEx.AssemblyPublicizer.MSBuild` (`0.4.3`): Automatic stripping of `private`/`internal` access modifiers from game assemblies at compile time.
  - **Dynamic Assembly Selection & Publicization:**
    - If `Assembly-CSharp.dll` is present, it is publicized and referenced.
    - In modern modular games (where `Assembly-CSharp.dll` is absent), the renderer dynamically selects and publicizes the detected game assemblies from `spec.TargetGame.GameAssemblyPaths` (e.g. `<GameName>.Scripts`, `<GameName>.Core`), eliminating `MSB3245` reference errors and publicizing the actual game classes.
    - Core engine references (`UnityEngine.dll`, `UnityEngine.CoreModule.dll`) from `Managed/` are automatically added with `<Private>false</Private>`.
    - Custom explicit `spec.PublicizedAssemblyNames` are preserved when specified.
  - **Opt-in Loose Asset Pipeline (`IncludeAssetPipeline`):**
    - When `spec.IncludeAssetPipeline == true`:
      - Injects `<None Update="Assets\**" CopyToOutputDirectory="PreserveNewest" />` so texture PNGs/JPEGs and assets are copied to the build output directory.
      - References `UnityEngine.ImageConversionModule.dll` with `<Private>false</Private>`.
      - Extends `DeployToBepInEx` to copy loose assets directly to `<GameRoot>/BepInEx/plugins/<ModName>/Assets/`, preserving subdirectory hierarchies so that mod assets remain visible and customizable on disk rather than packed into the DLL.
    - When `spec.IncludeAssetPipeline == false`:
      - Zero asset references, zero asset copying rules, and zero extra MSBuild targets are generated, keeping logic-only mods completely clean.
  - **Post-Build Deployment (`DeployToBepInEx`):**
    - Automatically copies the compiled `.dll` (and loose assets under `Assets/` when asset pipeline is active) to `<GameRoot>/BepInEx/plugins/<ModName>/` if the game directory exists.

* **`PluginTemplateRenderer` (`Rendering/PluginTemplateRenderer.cs`):**
  - Inherits from `BaseUnityPlugin`.
  - Decorated with `[BepInPlugin(PluginGuid, PluginName, PluginVersion)]`.
  - Initializes `Harmony(PluginGuid)` and calls `_harmony.PatchAll()`.
  - Cleans up hooks in `OnDestroy()` via `_harmony.UnpatchSelf()`.

* **`HarmonyPatchTemplateRenderer` (`Rendering/HarmonyPatchTemplateRenderer.cs`):**
  - Scaffolds a commented, idiomatic Harmony patch template with `[HarmonyPatch]`, `[HarmonyPrefix]`, and `[HarmonyPostfix]`.

* **`AssetLoaderTemplateRenderer` (`Rendering/AssetLoaderTemplateRenderer.cs`):**
  - Generates `AssetLoader.cs` when `IncludeAssetPipeline == true`.
  - Resolves plugin and loose asset file paths on disk using `typeof(AssetLoader).Assembly.Location` and `BepInEx.Paths.PluginPath`.
  - Loads raw asset bytes via `File.ReadAllBytes` and decodes textures via `ImageConversion.LoadImage`.
  - Constructs Unity `Sprite` instances via `Sprite.Create` ready for shop, UI, or character injection.
  - Exposes `ModDirectory`, `AssetsDirectory`, `ResolveAssetPath`, `LoadSprite`, `LoadTexture`, and a backward-compatible `LoadEmbeddedSprite` alias.

* **`ModRootReadmeTemplateRenderer` (`Rendering/ModRootReadmeTemplateRenderer.cs`):**
  - Generates the player-facing `README.md` at the mod project root.
  - Strictly presents a high-level natural language description of mod features, goals, and player experience with **zero source code references**.
  - Pointers guide developers and AI agents to the appropriate subfolder technical READMEs.

* **`PatchesReadmeTemplateRenderer` (`Rendering/PatchesReadmeTemplateRenderer.cs`):**
  - Generates `Patches/README.md` detailing the technical implementation of gameplay hooks.
  - Explains Harmony target interception (`[HarmonyPatch]`), `[HarmonyPrefix]`, `[HarmonyPostfix]`, method cancellation, publicized member access, and code-to-feature mapping.

* **`AssetsReadmeTemplateRenderer` (`Rendering/AssetsReadmeTemplateRenderer.cs`):**
  - Generates `Assets/README.md` when the asset pipeline is enabled.
  - Explains the loose asset deployment architecture, runtime disk loading with `AssetLoader.cs`, supported formats (`.png`, `.jpg`), and subdirectory organization.

---

### B. Build Runner Pipeline (`DotNetBuildRunnerAdapter` + `MsBuildDiagnosticParser`)
Executes the .NET CLI build pipeline and transforms unstructured compiler text into strongly-typed diagnostics:

* **Process Invocation (`DotNetBuildRunnerAdapter`):**
  - Resolves `Path.GetFullPath(projectFilePath)` to ensure both relative and absolute paths resolve correctly regardless of the caller's current working directory.
  - Invokes `dotnet build "<fullProjectPath>" --configuration <configuration>` with redirected standard streams and working directory set to the project root.
  - Honors `CancellationToken`: gracefully kills the spawned process tree if cancelled.
  - Synthesizes fallback error reports if non-zero exit codes occur without specific compiler diagnostics.

* **Pure Diagnostic Parsing (`MsBuildDiagnosticParser`):**
  - Recognizes standard MSBuild / Roslyn diagnostic output formats across platforms:
    ```regex
    ^(?:(?<file>.+?)(?:\((?<line>\d+)(?:,\s*\d+)?\))?\s*:\s*)?(?<severity>warning|error|info)\s+(?<code>[A-Za-z0-9_]+)\s*:\s*(?<message>.*?)(?:\s*\[.*?\])?$
    ```
  - Categorizes severity into `DiagnosticSeverity.Error`, `DiagnosticSeverity.Warning`, or `DiagnosticSeverity.Info`.
  - Deduplicates repeated messages across stdout and stderr via `.Distinct()`.

---

### C. Runtime Log Pipeline (`BepInExLogMonitorAdapter` + `BepInExLogParser`)
Monitors and parses the BepInEx runtime log file (`<GameDir>/BepInEx/LogOutput.log`) to provide deterministic runtime error detection for AI agents:

* **Non-Locking File Streaming (`BepInExLogMonitorAdapter`):**
  - Opens stream with `FileShare.ReadWrite | FileShare.Delete` and `FileAccess.Read` to prevent sharing violations (`IOException`) while the game executable is running.
  - Supports live streaming via `IAsyncEnumerable<LogEntry>` using seek-to-end cursor tailing.

* **Pure Log Parsing & Stack Trace Stitching (`BepInExLogParser`):**
  - Employs a Source-Generated Regex to match standard BepInEx header lines, with optional timestamp prefixes:
    ```regex
    ^(?:\[?(?<time>\d{4}-\d{2}-\d{2}[ T]\d{2}:\d{2}:\d{2}(?:\.\d+)?|\d{2}:\d{2}:\d{2}(?:\.\d+)?)\]?\s*)?\[(?<level>[A-Za-z]+)\s*:\s*(?<source>[^\]]+)\]\s*(?<message>.*)$
    ```
  - Stitches subsequent indented lines and `Exception: at ...` blocks into `LogEntry.StackTrace`.
  - Normalizes log levels (`DEBUG`, `INFO`, `MESSAGE`, `WARNING`, `ERROR`, `FATAL`) to `Candied.Core.Models.LogLevel`.

---

### D. Knowledge Hub Documentation Pipeline (`DiskDocumentationGeneratorAdapter` + `Rendering/` + `Analysis/`)
Generates comprehensive educational and machine-readable reverse engineering documentation in Zone 3 (`.candied/games/<GameName>/docs/`):

* **Pure Analyzer (`GameArchitectureAnalyzer`):**
  - Evaluates `GameEnvironment` and indexed symbols to classify core game logic vs framework assemblies.
  - Detects key system managers (`*Manager`, `*Controller`, `*System`, `*Handler`, `*Service`) as prime targets for Harmony hooks.
  - Groups types by namespace and produces structured `GameArchitectureOverview` metadata.

* **Pure Template Renderers (`Rendering/`):**
  - **`GameOverviewTemplateRenderer`:** Renders `GAME_OVERVIEW.md` detailing Unity engine info, detected game assemblies, key managers, and modding guidelines.
  - **`NamespaceMapTemplateRenderer`:** Renders `NAMESPACES.md` with table of contents and prominent classes grouped by namespace.
  - **`SymbolIndexJsonRenderer`:** Renders `SYMBOLS_INDEX.json` containing hierarchical symbol mapping for instant lookup by AI agents.

* **Disk Generator Adapter (`DiskDocumentationGeneratorAdapter`):**
  - Implements `IDocumentationGenerator`, orchestrating file writes exclusively within the isolated Zone 3 `.candied/games/<GameName>/docs/` directory, completely avoiding game folder access.

---

### E. Environment Provisioning & Rollback Pipeline (`BepInExEnvironmentProvisionerAdapter` + `BepInExConfigTemplateRenderer`)
Manages automated mod loader installation and reversible rollback in Zone 1:

* **Local Zip Cache & Architecture Matching:**
  - Automatically matches target game architecture (`x64` vs `x86`) to the official BepInEx release archive (`BepInEx_x64_5.4.23.2.zip` vs `BepInEx_x86_5.4.23.2.zip`).
  - Downloads release archives to local cache `.candied/cache/bepinex/` and reuses cached archives across runs to prevent redundant downloads and enable offline operations.

* **Pure INI Configuration Template (`BepInExConfigTemplateRenderer`):**
  - Renders `BepInEx/config/BepInEx.cfg` with configurable options:
    - `[Logging.Console] Enabled = true/false`
    - `[Logging.Disk] WriteUnityLog = true/false`
  - Generates standard BepInEx INI syntax without disk I/O.

* **Tracking Manifest (`.candied_manifest.json`):**
  - Before writing files into Zone 1, records the list of all files extracted and created (`winhttp.dll`, `doorstop_config.ini`, core assemblies, config files).
  - Writes `.candied_manifest.json` directly into the game root directory.

* **Safe & Deterministic Rollback (`RollbackEnvironmentAsync`):**
  - Reads `.candied_manifest.json` from the game root.
  - Deletes exclusively the files and directories recorded in the manifest.
  - Leaves all original game executables, vanilla assemblies, user saves, and unrelated files untouched.
  - Removes `.candied_manifest.json` upon complete restoration.

---

### F. Process Management Pipeline (`OsGameProcessManagerAdapter`)
Provides non-invasive runtime process management for Unity games:

* **OS Process Lifecycle (`OsGameProcessManagerAdapter`):**
  - **Attach-First Strategy:** Detects if the game process is already running; attaches directly without spawning redundant processes or conflicting with Steam.
  - **Interactive Windowed Launch:** Launches the game executable with `UseShellExecute = true` in a dedicated compact window (`-screen-fullscreen 0 -screen-width 1280 -screen-height 720 -popupwindow`), ensuring full Windows interactive desktop window integration instead of headless background sub-process execution.
  - **Thorough Process Tree Termination:** Finds all running instances and related game helper processes, attempting clean `CloseMainWindow()` before forceful termination via `process.Kill(entireProcessTree: true)` to guarantee zero zombie or background processes consuming CPU/RAM after execution.

* **Zone 2 & Zone 3 Workspace Storage Discovery (`CandiedWorkspaceStorage`):**
  - Automatically resolves the repository/workspace root by checking `CANDIED_WORKSPACE_ROOT`, then traversing upward from `Directory.GetCurrentDirectory()` for repository markers (`Candied.sln` or `.git`), and if not found, traversing upward from `AppContext.BaseDirectory` (where the published `Candied.Agent.exe` resides) for `Candied.sln` or `.git`.
  - Guarantees that mod projects (`mods/<ModName>/`), asset extraction directories, SQLite indices, and decompiled source trees always resolve to the Candied repository root and never leak into external IDE installation directories (such as `AppData\Local\Programs\antigravity`) when `Candied.Agent.exe` is spawned as an MCP daemon.
