# Candied Tool Ergonomics & Reliability Roadmap

This document tracks empirical pain points discovered during real end-to-end Unity Mono modding sessions using Candied, together with the approved architectural solutions and implementation status. All solutions are strictly generic across any Unity Mono game.

---

## 1. Priority 1 — Core Reliability & Ergonomics (Completed & Verified Live)

| ID | Area | Empirical Pain Point | Implemented Solution | Status |
|---|---|---|---|---|
| **A** | `game-launch` | Pixel diff `< 1.0%` over a single 1s window false-positives on slow loading screens (e.g. spinning hourglass or slow progress bars with ~0.4% pixel change). | Added `activeRendererCount` and `timeSinceLevelLoad` to `/scene` (`VisionSceneInfoDto`). Upgraded `LaunchGameByNameAsync` stability loop to require **2 consecutive stable 1s windows (`diff < 0.25%`)** with unchanged `activeSceneName`, `loadedSceneCount`, and stable `activeRendererCount` ($\Delta \le 2$), plus optional `waitForLog` marker check in `BepInEx/LogOutput.log`. | Verified Live (`diff: 0.00% < 0.25%`, 2 consecutive windows, landed on real Main Menu) |
| **B** | `inspect-scene-objects` | Only inspected active renderers, dumped 7,000+ objects (~2.4 MB) without limits, and omitted full hierarchy paths, `localPosition`/`localScale`, sprite names/pivots, and attached component names. | Extracted `SceneObjectsInspector` out of `VisionHttpServer`. Introduced `SceneObjectsQuery` (`nameFilter`, `includeInactive`, `visibleOnly`, `sortingLayer`, `maxResults` default `100`). Enriched `VisionSceneRendererDto` & `VisionSceneObjectsResponse` with `fullPath`, `activeSelf`, `activeInHierarchy`, `localPosition`, `localScale`, `spriteName`, `spritePivot`, `components`, `totalMatched`, and `truncated`. Scene-object guard (`scene.IsValid()`) keeps unloaded prefab assets out when `includeInactive=true`. | Verified Live (79 visible renderers capped at 5; inactive scene objects surfaced cleanly with `fullPath`, `spriteName`, `spritePivot`, `components`, `truncated: true`) |
| **C** | `discover-assets` & Runtime Assets | `discover-assets` did not auto-resolve active `gameDirectory`, repeated long `sourceContainerPath` on every item (>46 KB minified JSON), lacked `offset` / `totalMatches`, and offered no runtime visibility into loaded `AssetBundle`s or in-memory assets. | Auto-resolved active game via `ResolveActiveGameDirectoryAsync`, deduplicated container paths into a top-level `containers` dictionary, added `offset` + `AssetDiscoveryPage(TotalMatches, Assets)` (default `maxResults=50`). Added generic VisionBridge endpoint `GET /assets/loaded` (`RuntimeAssetsInspector` via pure reflection) and MCP tool `inspect-runtime-assets`. | Verified Live (`discover-assets` auto-resolved dir, paginated `offset=5` of 28 matches with single `containers` entry; `inspect-runtime-assets` enumerated 4 loaded bundles + 19,498 runtime sprites capped at 8) |
| **D** | `query`, `type`, `search-code` | `query` ignored `SymbolKindFilter`/`NamespacePrefix`/`Limit` already present in `Core`; `type` dumped huge classes as escaped single-line JSON with no summary/member filter; no full-text search across decompiled method bodies. | Exposed `kind` (`all\|types\|members\|class\|struct\|interface\|enum\|method\|field\|property\|event`), `namespacePrefix`, and `limit` on `query`. Added `signaturesOnly`, `memberFilter` (`TypeMemberSourceExtractor`), and Zone 3 `sourceFilePath` to `type`. Added SQLite FTS5 `type_source_fts` with transparent auto-backfill (`type_source_fts_docsize` check), `ISymbolIndex.SearchCodeAsync`, `SearchGameCodeUseCase`, and MCP tool `search-code`. | Verified Live (`query --kind enum/class`, `type --signatures-only`, `type --member`, `search-code` auto-backfilled existing `index.db` and returned snippets + `sourceFilePath`) |
| **G** | `build` & `.csproj` References | `CS0012` / missing managed assembly errors forced manual shell searches in `Managed/` and manual XML edits in `.csproj`. | Added pure `MissingReferenceHintAnalyzer` (English + localized MSBuild `CS0012` extraction) enriching `BuildReport` with verified DLL paths from `Managed/`. Added dedicated port `IProjectReferenceEditor`, pure `CsprojReferenceMutator`, `DiskProjectReferenceEditorAdapter`, `AddProjectReferenceUseCase`, and MCP tool `add-reference`. | Verified Live (real `CS0012` -> `missingReferenceHints` resolved exact DLL -> `add-reference` inferred `Managed/` -> subsequent `build` passed with 0 errors) |

---

## 2. Priority 2 — Future Backlog (Deferred Until Requested)

| ID | Area | Empirical Pain Point | Proposed Future Direction | Status |
|---|---|---|---|---|
| **E** | Live Input & 2D Interactables | Agents can only click (no cursor move/hover without clicking, no keyboard key presses) and must visually estimate `(x, y)` coordinates for 2D `Collider2D`/world-space buttons because `list-ui` only covers uGUI `Selectable`. | Add `move-mouse` (`action: "move"` already exists in `InputClicker`), `key-press`, and unified 2D + uGUI `list-interactables` projecting visible colliders + text labels into screenshot pixel rects. | Backlog |
| **F** | Faster Layout Iteration & Incremental Logs | Minor layout coordinate adjustments require a full close → build → launch → menu cycle (~35–45s), and `logs` always returns from the beginning of the file without source filtering or incremental cursors. | Add `sourceFilter` and incremental cursor/tailing to `logs`; evaluate safe runtime transform inspection/nudging via VisionBridge and a one-step `rebuild-and-relaunch` orchestration helper. | Backlog |

---

## 3. Architectural Guardrails for These Changes

1. **Hexagonal Boundaries:** Every new capability stays strictly within Candied's allowed dependency graph (`Primitives` / `VisionBridge.Protocol` → `Core` → `Decompiler` / `Workspace` → `Agent` / `Studio`). No direct I/O in `Candied.Core`.
2. **Single Port per Adapter:** `IProjectReferenceEditor` is implemented by its own dedicated `internal sealed class DiskProjectReferenceEditorAdapter` delegating XML mutation to a pure, testable `CsprojReferenceMutator`.
3. **Pure Micro-Components (Level 3 Modularity):** `MissingReferenceHintAnalyzer`, `TypeMemberSourceExtractor`, `CsprojReferenceMutator`, and `SceneObjectsInspector` isolate pure parsing/filtering logic from disk, SQLite, or HTTP transport.
4. **Multi-Target VisionBridge Compatibility:** All additions to `Candied.VisionBridge.Protocol` and `Candied.VisionBridge` must compile cleanly across `net35;net472;netstandard2.0;net9.0` with zero new external dependencies and pure reflection for optional Unity modules.
5. **Zero Game-Specific Coupling:** Every probe, query, and heuristic operates exclusively on standard Unity Mono APIs, Roslyn ASTs, SQLite FTS5, and MSBuild diagnostics.
