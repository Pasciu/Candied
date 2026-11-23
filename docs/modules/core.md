# Module: Candied.Core

## 1. Overview
`Candied.Core` contains the pure domain logic, entities, value records, domain errors, and hexagonal port interfaces. It has no knowledge of external frameworks, file systems, databases, or transport mechanisms.

* **Namespace:** `Candied.Core`
* **Dependencies:** `Candied.Primitives`
* **Project Reference Invariants:** Must never reference `Candied.Decompiler`, `Candied.Workspace`, or `Candied.Agent`.
* **Zero I/O Invariant:** Prohibited from importing `System.IO.*` or `System.Diagnostics.Process` (enforced by ArchUnitNET).

---

## 2. Ports (Interfaces)

Ports define the outbound capabilities required by the domain. Concrete adapters in infrastructure modules implement these ports.

```mermaid
classDiagram
    class IGameDetector {
        +DetectGameAsync(string gameDir) Task~Result~GameEnvironment, DetectionError~~
    }
    class IDecompilerEngine {
        +DecompileAssemblyAsync(string assemblyPath, string outputDir) Task~Result~DecompilationOutput, DecompilerError~~
    }
    class ISymbolIndex {
        +IndexSourceTreeAsync(string sourceDir, string? assemblyName, string? databasePath) Task~Result~Unit, IndexError~~
        +SearchSymbolsAsync(SymbolQuery query, string? databasePath) Task~Result~IReadOnlyList~SymbolSummary~, IndexError~~
        +GetAllSymbolsAsync(string? databasePath) Task~Result~IReadOnlyList~SymbolSummary~, IndexError~~
        +GetTypeDetailAsync(string fullTypeName, string? databasePath) Task~Result~TypeDefinitionDetail, IndexError~~
    }
    class IDocumentationGenerator {
        +GenerateKnowledgeHubAsync(GameEnvironment game, IReadOnlyList~SymbolSummary~ symbols, string docsDir) Task~Result~Unit, ScaffoldError~~
        +SaveResearchNotesAsync(string gameName, string notes, string docsDir) Task~Result~Unit, ScaffoldError~~
    }
    class IProjectGenerator {
        +ScaffoldModProjectAsync(ModScaffoldSpec spec) Task~Result~ModProjectManifest, ScaffoldError~~
    }
    class IBuildRunner {
        +BuildModAsync(string projectPath, string config) Task~Result~BuildReport, BuildExecutionError~~
    }
    class ILogMonitor {
        +ReadRecentLogsAsync(string logPath, int maxLines) Task~Result~IReadOnlyList~LogEntry~, LogMonitorError~~
        +StreamLogsAsync(string logPath) IAsyncEnumerable~LogEntry~
    }
    class IEnvironmentProvisioner {
        +ProvisionEnvironmentAsync(ProvisioningSpec spec, string cacheDirectory) Task~Result~ProvisioningReport, ProvisioningError~~
        +RollbackEnvironmentAsync(string gameRootPath) Task~Result~RollbackReport, ProvisioningError~~
    }
    class IGameProcessManager {
        +GetRunningGameAsync(GameEnvironment game) Task~Result~GameProcessInfo?, ProcessManagementError~~
        +LaunchGameAsync(GameEnvironment game, GameLaunchOptions options) Task~Result~GameProcessInfo, ProcessManagementError~~
        +StopGameAsync(GameEnvironment game) Task~Result~Unit, ProcessManagementError~~
    }
    class IAssetExtractor {
        +DiscoverAssetsAsync(GameEnvironment game, AssetDiscoveryFilter filter) Task~Result~IReadOnlyList~DiscoveredAssetInfo~, AssetExtractionError~~
        +ExtractAssetsAsync(GameEnvironment game, AssetExtractionFilter filter, string outputDir) Task~Result~IReadOnlyList~ExtractedAssetInfo~, AssetExtractionError~~
    }
```

---

## 3. Domain Models & Value Objects

All models in `Core` are immutable C# records:

* **`CandiedStorageLayout`:** Pure domain value object defining the directory paths for the Three-Zone Architecture (Zone 1: Game installation, Zone 2: Mod workspace, Zone 3: Artifacts store `.candied/games/<GameName>/`, plus global caching in `.candied/cache/bepinex/`). Performs pure string path calculations with zero `System.IO` dependencies.
* **`ProvisioningSpec`, `ProvisioningReport`, `RollbackReport` & `ProvisioningManifest`:**
  - `ProvisioningSpec`: Specifies target `GameEnvironment`, requested BepInEx version, and logging preferences (Console/Disk).
  - `ProvisioningReport`: Outcome of provisioning, including target architecture, extracted files count, manifest path, and warnings.
  - `RollbackReport`: Outcome of uninstallation, including deleted files and directories count and success status.
  - `ProvisioningManifest`: Serializable model stored in `.candied_manifest.json` tracking installed files and directories with timestamp and release info.
* **`AssemblyTier` & `DiscoveredAssembly`:** Multi-tier semantic categorization of discovered game assemblies:
  - `AssemblyTier.CoreGame`: `[GameName].*.dll`, `Assembly-CSharp.dll`, `Assembly-CSharp-firstpass.dll`.
  - `AssemblyTier.GameModular`: Game-specific secondary or modular assemblies.
  - `AssemblyTier.AuxiliaryOrThirdParty`: Unity engine modules, BCL frameworks, and common middleware.
  ```csharp
  public record DiscoveredAssembly(
      string Name,
      string FullPath,
      long SizeBytes,
      AssemblyTier Tier,
      string Reason,
      bool IsDecompiled = false
  );
  ```
* **`BepInExInstallation`:** Captures BepInEx presence, version string (e.g. `5.4.21.0`), and root path.
* **`GameEnvironment`:** Complete environment audit for a detected game target:
  ```csharp
  public record GameEnvironment(
      GameId Id,
      string GameName,
      string GameRootPath,
      string ManagedPath,
      UnityEngineType EngineType,
      string Architecture,
      string? TargetFramework,
      BepInExInstallation BepInExInfo,
      IReadOnlyList<DiscoveredAssembly> DiscoveredAssemblies,
      IReadOnlyList<string> EnvironmentWarnings
  );
  ```
* **`GameArchitectureOverview` & Sub-models:** Structured data representing detected game architecture, assemblies, namespaces, and gameplay managers used for generating human and machine documentation.
* **`SymbolQuery` & `SymbolSummary`:** Lightweight search filters and returned summaries to prevent blowing up the LLM context window.
* **`TypeDefinitionDetail`:** Detailed C# type signature, base class, implemented interfaces, fields, methods, and raw code snippet.
* **`ModScaffoldSpec` & `ModProjectManifest`:**
  - `ModScaffoldSpec`: Immutable specification for scaffolding BepInEx plugin projects. Includes `ModName`, `PluginGuid`, `Version`, `OutputDirectory`, `TargetGame`, optional `PublicizedAssemblyNames`, and `IncludeAssetPipeline` (boolean flag defaulted to `false`). The agent explicitly toggles `IncludeAssetPipeline: true` only when custom textures/sprites are required, keeping logic-only mods lightweight.
  - `ModProjectManifest`: Describes the generated mod project file paths and status.
* **`LiveInspection` Models (Generic Unity Engine Models):**
  - `GameProcessInfo`: Captures PID, process name, window title, responsiveness, start time, and bridge attachment status.
  - `GameLaunchOptions`: Windowed mode flag, resolution dimensions, and custom launch arguments.
  - `UnityTypeQuery`: Query specification for live Unity objects by type name and optional regex/glob pattern.
  - `UnityObjectSummary` & `UnityObjectDetail`: Generic representation of in-memory Unity objects and their serialized key-value pairs or full JSON properties.
  - `AssetDumpSpec` & `ExtractedAssetReport`: Specifications and output metadata for on-demand extraction of single Sprite or Texture2D assets to PNG.

---

## 4. Use Cases

Use cases in `Candied.Core.UseCases` orchestrate domain workflows:

* **`InspectGameEnvironmentUseCase`:**
  Performs non-destructive environment discovery (engine type, architecture, BepInEx installation status, .NET target framework, classified assemblies catalog) without triggering any decompilation.
* **`SetupGameEnvironmentUseCase`:**
  Audits the target game directory, verifies architecture, and provisions the correct BepInEx loader, logging config, and manifest via `IEnvironmentProvisioner`.
* **`RollbackGameEnvironmentUseCase`:**
  Deterministically reverts all files written during provisioning using the `.candied_manifest.json` tracker via `IEnvironmentProvisioner`.
* **`DecompileAssemblyUseCase`:**
  Decompiles a single specified `.dll` into an isolated assembly directory (`decompiled/<AssemblyName>/`) using `IDecompilerEngine`, incrementally updates the SQLite index via `ISymbolIndex` without wiping other assemblies, and automatically synchronizes the Knowledge Hub (`GAME_OVERVIEW.md`, `NAMESPACES.md`, `SYMBOLS_INDEX.json`) via `IDocumentationGenerator`.
* **`SearchGameSymbolsUseCase`:**
  Pure query use case querying the SQLite symbol index without triggering file operations.
* **`SaveResearchNotesUseCase`:**
  Persists agent online research notes (e.g., community modding guides, BepInEx requirements) into Zone 3 (`RESEARCH.md`) via `IDocumentationGenerator`.
* **`GetTypeDetailUseCase`:**
  Retrieves complete type structure and decompiled source code from the SQLite symbol index, supporting both default and custom `databasePath` locations.
* **`GetGameProcessStatusUseCase`:**
  Checks whether the target Unity game is currently running via `IGameProcessManager`.
* **`LaunchGameUseCase`:**
  Controls game execution using an "Attach-First" strategy: if the game process is already running, attaches to it immediately without spawning duplicate windows; otherwise launches the game in a controlled compact window.
* **`StopGameUseCase`:**
  Terminates the spawned game process cleanly via `IGameProcessManager`.
* **`DiscoverAssetsUseCase`:**
  Scans Unity archives on disk via `IAssetExtractor` to locate visual assets (Sprites, Texture2Ds) by name pattern or asset type completely offline.
* **`ExtractAssetsUseCase`:**
  Extracts targeted Sprites or Textures directly from disk archives into PNG files in Zone 3 (`.candied/games/<Game>/assets/`) via `IAssetExtractor`.
* **`ScaffoldModUseCase`:**
  Orchestrates mod project creation via `IProjectGenerator`.
* **`BuildModUseCase`:**
  Orchestrates compilation and diagnostic capture via `IBuildRunner`.
* **`ReadLogsUseCase`:**
  Orchestrates log monitoring and parsing via `ILogMonitor`.
