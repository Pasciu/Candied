# Permanent Agent Guidelines for Candied

These rules are permanent and mandatory for any AI coding agent operating on this repository.

---

## 0. Intent Routing (Developer vs Modder)

Before taking any action, determine the user's intent:

* **Role A: Developing Candied (Codebase Maintainer)**
  If the prompt asks to add features, fix bugs, refactor C# code, or maintain the Candied repository itself:
  👉 Follow the strict architectural rules in this document (`AGENTS.md`) and run `./check.ps1` before declaring done.

* **Role B: Creating a Mod (Modder using Candied)**
  If the prompt asks to inspect a game, reverse-engineer game assemblies, or build a mod (e.g., *"guarda in percorso X, fai una mod Y"*):
  👉 **DO NOT modify the Candied codebase.** Candied is the tool provider, not the target game.
  👉 **Switch immediately to [`MODDING.md`](./MODDING.md)** and execute the Candied MCP tools (`inspect`, `setup`, `decompile`, `query`, `scaffold`, `build`, `logs`).

---

## 1. Single Verification Command

Run this command before declaring any work complete:
- **Windows (PowerShell):** `./check.ps1`
- **Linux / macOS:** `./check.sh`
- **Native .NET alternative:** `dotnet test --configuration Release` (runs build with `/p:TreatWarningsAsErrors=true` + all architecture & unit tests)

The verification command runs:
1. `dotnet format --verify-no-changes` (Code style and formatting check)
2. `dotnet build --configuration Release /p:TreatWarningsAsErrors=true` (Type-check, compiler warnings as errors, static analysis)
3. `dotnet test --configuration Release --no-build --verbosity normal` (Architecture boundary tests and unit tests)

---

## 2. Mandatory Rules Before and During Any Change

1. **Read State First:** BEFORE writing or planning any change, you MUST read:
   - `docs/architecture.md`
   - `docs/project-state.md`
2. **Encapsulation & Public Entry Points:**
   - NEVER make classes `public` in infrastructure modules (`Candied.Decompiler`, `Candied.Workspace`) unless they are part of the public DI extension entry points. Keep implementations `internal`.
   - NEVER bypass public contracts or import internal types from another module.
3. **Strict Dependency Hierarchy:**
   - Adhere strictly to the allowed dependency graph in `docs/architecture.md`.
   - `Candied.Core` MUST NEVER reference `Candied.Decompiler`, `Candied.Workspace`, or `Candied.Agent`.
   - `Candied.Decompiler` and `Candied.Workspace` MUST NEVER reference each other.
   - `Candied.Primitives` MUST NEVER reference any project.
4. **Pure Domain vs Side Effects:**
   - NEVER put filesystem I/O (`File`, `Directory`), external process execution (`Process.Start`), or database connections inside `Candied.Core`.
   - All side effects MUST be placed behind ports (interfaces in `Core`) and implemented as adapters in `Candied.Decompiler` or `Candied.Workspace`.
5. **No Scope Creep:**
   - NEVER modify code not explicitly requested by the user's task.
   - No unsolicited refactoring, re-formatting, or "improvements" out of scope. If you notice potential improvements, mention them as suggestions at the end of your response.
6. **Contract Changes & No Backward Compatibility for Incomplete Features:**
   - NEVER preserve backward compatibility or carry legacy luggage/workarounds for broken, incomplete, or experimental features. When a feature is incomplete or flawed, replace it directly with the correct, robust, and clean design.
   - If a task requires changing an existing public interface, DTO contract, or project dependency graph that is already established and complete, STOP immediately and present a clear proposal. However, once approved or when fixing broken/incomplete prototypes, design the cleanest possible contract without backwards-compatibility compromises.
7. **Continuous Technical Documentation (Mandatory):**
   - At EVERY task or modification, you MUST create (if it does not exist) or update (if it exists) the relevant technical documentation in `docs/` in **ENGLISH** (e.g., `docs/modules/<module>.md`, `docs/data-flow.md`, `docs/architecture.md`, `docs/project-state.md`).
   - Documentation must be clear, structured, and helpful for both AI agents and human developers to understand *what is happening, why, and how components interact*.
   - Never consider a task done without updating the corresponding documentation.
8. **Multi-Level Fractal Modularity (Macro to Micro Architecture):**
   - The architectural principles of Candied (Hexagonal boundary isolation, Single Responsibility, High Cohesion, Low Coupling, and Pure vs Impure separation) MUST be applied recursively across ALL abstraction levels:
     * **Level 1: System Macro-Architecture (Projects & Boundaries):** Strict hexagonal boundaries, explicit dependency hierarchy, ports & adapters, ArchUnitNET boundary verification.
     * **Level 2: Module Internal Architecture (Subsystems within Assemblies):**
       - Adapters must adhere to Single Responsibility: an adapter class MUST NOT implement multiple distinct ports (e.g. no combining `IGameDetector` and `IDecompilerEngine`). Each port gets its own dedicated, specialized adapter class.
       - Subsystems within a module must be explicitly decomposed (e.g., discovery vs decompilation vs indexing; scaffolding vs build execution vs log monitoring).
       - Internal components must have clean separation of concerns and not become monolithic "god adapters".
     * **Level 3: Component & Class Architecture (Lower Abstractions):**
       - Separate I/O orchestration from pure domain logic, template generation, and string/data parsing.
       - Template generation (MSBuild XML, C# plugin code, Harmony patches) must reside in dedicated, pure template renderers, isolated from filesystem writing.
       - Text parsing (MSBuild diagnostic parsing, log regex parsing) must reside in dedicated, pure parsers, isolated from OS process or filesystem streaming.
       - Classes must remain highly testable in memory without mocking the entire environment or spawning external processes.
9. **Definition of Done:**
   A task is ONLY complete when:
   - The verification command (`./check.ps1` or `./check.sh`) passes with 0 errors and 0 warnings.
   - Automated tests cover any newly added behavior or rules.
   - Technical documentation in `docs/` (both `docs/project-state.md` and the relevant `docs/modules/<module>.md` / `docs/architecture.md`) is created or updated in English to fully reflect the new state.

---

## 3. Naming and File Structure Conventions

- **Projects & Namespaces:** PascalCase matching folder names (`Candied.Core`, `Candied.Workspace`, etc.).
- **Interfaces:** Prefix with `I` (e.g., `ISymbolIndex`, `IBuildRunner`). Placed under `Candied.Core/Ports/`.
- **Value Objects & DTOs:** Immutable C# `record` types under `Candied.Core/Models/` or `Candied.Primitives/`.
- **Adapters:** Named `<Technology><PortName>Adapter` (e.g., `SqliteSymbolIndexAdapter`, `DotNetBuildRunnerAdapter`, `UnityGameDetectorAdapter`, `ILSpyDecompilerEngineAdapter`).
- **File Scoped Namespaces:** Always use file-scoped namespaces (`namespace Candied.Core;`).
