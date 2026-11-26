# Module: Candied.Infrastructure

## 1. Overview
`Candied.Infrastructure` provides the engine-agnostic persistence, indexing, platform registry, Knowledge Hub rendering, and OS process adapters shared across all target platforms.

* **Namespace:** `Candied.Infrastructure`
* **Target Framework:** `net9.0`
* **Dependencies:** `Candied.Core`, `Candied.Primitives`
* **External Packages:** `Microsoft.Data.Sqlite`, `Microsoft.CodeAnalysis.CSharp`, `Microsoft.Extensions.DependencyInjection.Abstractions`
* **Encapsulation:** Every concrete adapter and helper class is `internal`; only `DependencyInjection.AddCandiedCoreAndInfrastructure(...)` and `CandiedWorkspaceStorage` are `public`.

---

## 2. Single-Responsibility Adapters (`Candied.Infrastructure.Adapters`)

Each adapter implements exactly **one** domain port from `Candied.Core.Ports` (enforced by `Candied.ArchitectureTests`):

| Adapter Class | Implemented Port | Responsibility |
|---|---|---|
| `DefaultTargetPlatformRegistryAdapter` | `ITargetPlatformRegistry` | Evaluates registered `ITargetPlatform` implementations ordered by descending `Priority`; falls back to `GenericTargetPlatform` (`generic.window`) so any PC executable or emulator + ROM is always inspectable. |
| `SqliteSymbolIndexAdapter` | `ISymbolIndex` | Manages per-target Zone 3 SQLite databases (`index.db`) with incremental module tracking (`indexed_modules`), `symbols`, `type_details` (preserving generic arity such as `Result<TValue, TError>`), and synchronized SQLite **FTS5** full-text search (`type_source_fts`). Supports both C# AST indexing (via `RoslynSymbolExtractor`) and direct symbol ingestion for future IL2CPP/Lua/GDScript platforms. |
| `SqliteAssetIndexAdapter` | `IAssetIndex` | Stores and queries discovered target asset catalogs in SQLite (`assets` table) with fast offset/limit pagination, container deduplication, and exact `AssetKey` lookup. |
| `DiskDocumentationGeneratorAdapter` | `IDocumentationGenerator` | Writes rendered Knowledge Hub markdown and JSON files (`GAME_OVERVIEW.md`, `NAMESPACES.md`, `SYMBOLS_INDEX.json`, `RESEARCH.md`) to Zone 3 (`docs/`). |
| `OsTargetProcessManagerAdapter` | `ITargetProcessManager` | Tracks, launches, and stops standalone target processes when running outside Studio hosting. |

---

## 3. Internal Pure Subsystems (`Candied.Infrastructure.Internal`)

* **`GenericTargetPlatform` (`generic.window`, Priority `-1000`):** Universal fallback `ITargetPlatform` for custom C++ games, unknown engines, and emulators (`RunnerPath` + ROM `TargetPath`). Reports `LiveVisionSensor` capability via Studio's Tier 1 `BaseWindow` host even when no engine plugin exists.
* **`RoslynSymbolExtractor`:** Pure syntax-only C# parser extracting classes, structs, records, interfaces, enums, fields, properties, constructors, methods, events, inheritance lists, and generic type parameters without requiring full MSBuild compilation.
* **`KnowledgeHubRenderers`:** Pure string builders formatting cumulative multi-module symbol statistics into human- and LLM-readable markdown overviews.
