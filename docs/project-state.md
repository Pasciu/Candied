# Project State & Roadmap

## 1. Current Architecture Status (Phase 1: Multi-Platform Foundation Complete)

Candied has completed **Phase 1: Multi-Platform Architectural Foundation**, refactoring the entire workbench from a Unity-Mono-only codebase into a clean, composable **Multi-Platform Modding & 2-Tier Live Vision Architecture**:

* **All verification gates (`./check.ps1` & `./setup.ps1`):** PASSING
  - `dotnet format --verify-no-changes`: 0 formatting violations
  - `dotnet build --configuration Release /p:TreatWarningsAsErrors=true`: 0 warnings, 0 errors across all 12 projects
  - `dotnet test --configuration Release`: **71 passing automated tests** (`9` `ArchUnitNET` boundary invariants in `Candied.ArchitectureTests` + `62` unit, SQLite FTS5 integration, Unity Mono platform integration, security, and full vertical-slice E2E tests in `Candied.Tests`)
  - **30 MCP tools advertised** (`29` engine-agnostic Core & Vision v2.0 tools + `1` dynamically discovered `unity-add-reference` tool via `IPlatformToolProvider`)

---

## 2. Active Solution Layout (12 Projects)

| Project | Target Framework(s) | Architectural Role | Status |
|---|---|---|---|
| `src/Candied.Primitives` | `net9.0` | Zero-dependency `Result<T,E>`, `Unit`, and agnostic IDs (`TargetId`, `SymbolId`, `ModId`, `ProcessId`, `SceneNodeId`). | Complete |
| `src/Candied.Core` | `net9.0` | 100% engine-agnostic domain models (`TargetDescriptor`), composable `ITargetPlatform` capability traits, shared ports, and pure use cases. | Complete |
| `src/Candied.Infrastructure` | `net9.0` | Shared SQLite symbol + FTS5 code index, SQLite asset index, 3-Zone storage helper, Knowledge Hub generator, and universal `generic.window` fallback platform. | Complete |
| `src/Candied.Vision.Protocol` | `net35;net472;netstandard2.0;net9.0` | Zero-dependency Vision v2.0 HTTP wire DTOs, `JsonLite`, `CoordinateConverter`, `MainThreadDispatcher`, and `VirtualInputStateTracker`. | Complete |
| `src/Candied.Vision` | `net9.0` | Host-side 2-Tier Vision ports/adapters, `WindowLaunchProfileBuilder` (PC + Emulator `runnerPath`), 3-channel input & `GameControlProfile` (`controls.json`), and `PurePngDecoder`. | Complete |
| `src/Candied.Studio` | `net9.0-windows` | **100% pure standalone 2-Tier Live Vision & Window Host** (`BaseWindow` Win32 capture/input + automatic `SensorBridge` upgrade, plus interactive `🎮 Comandi` tab). Zero references to `Core`, decompilers, or mod compilers. | Complete |
| `src/Platforms/Candied.Platforms.Unity.Shared` | `net9.0` | Shared Unity inspector (Mono vs IL2CPP detection, PE headers, engine version), `AssetsTools.NET` offline asset pipeline, `InputManager`/`.inputactions` control extractor, and BepInEx log parser. | Complete |
| `src/Platforms/Candied.Platforms.Unity.Mono` | `net9.0` | `unity.mono` platform implementation: ILSpy `extract-code`, BepInEx 5 `setup`/`rollback`, Harmony C# `scaffold`/`build`, and dynamic `unity-add-reference` tool. | Complete |
| `src/VisionSensors/Candied.VisionSensors.UnityMono` | `net35;net472` | In-game BepInEx 5 HTTP Vision v2.0 sensor with isolated virtual mouse/keyboard/gamepad, uGUI inspection, and generic scene graph nodes. | Complete |
| `src/Candied.Agent` | `net9.0` | Composition root, JSON-RPC 2.0 MCP stdio server, and CLI dispatcher. | Complete |
| `tests/Candied.ArchitectureTests` | `net9.0` / `net9.0-windows` | `ArchUnitNET` structural boundary, purity, agnosticism, Studio isolation, and single-port adapter enforcement. | Complete (9/9) |
| `tests/Candied.Tests` | `net9.0` / `net9.0-windows` | Unit, integration, security, and vertical-slice E2E test suite. | Complete (62/62) |

---

## 3. Key Capabilities Delivered in Phase 1

1. **Universal Target & Emulator Support (`targetPath` + optional `runnerPath`):**
   - Replaces legacy `gameDirectory` and eliminates the blocking `registered_games.json` allowlist. Any PC game directory/executable or ROM/ISO + Emulator can be inspected (`inspect`) and launched directly (`game-launch`), with non-blocking recent history (`recent-targets`).
2. **2-Tier Live Vision (`BaseWindow` + `SensorBridge`):**
   - **Tier 1 (`BaseWindow`):** Immediate external window screenshot (`PrintWindow`/`BitBlt`) and 3-channel Win32 input dispatch on *any* hosted game or emulator without requiring an in-game plugin.
   - **Tier 2 (`SensorBridge`):** Automatic upgrade when an engine sensor connects over `Candied.Vision.Protocol` v2.0.
3. **3-Channel Input & Hybrid Semantic Control Mapping (`controls.json`):**
   - Native support for `Mouse` (`pointer`), `Keyboard`, and `Gamepad` (`send-input`), plus semantic control profiles (`get-controls`, `set-controls`, `trigger-action`) editable from both MCP/CLI and Studio's **🎮 Comandi** tab.
4. **Clean Cut on MCP/CLI Contracts:**
   - Agnostic `extract-code(moduleName)` replaces `decompile(assemblyName)`; `inspect-scene` returns generic `VisionSceneNodeDto` records; engine-specific tools (`unity-add-reference`) are dynamically provided by `IPlatformToolProvider`.

---

## 4. Roadmap (Next Phases)

* **Phase 2 — `Candied.Platforms.Unity.Il2Cpp` + `Candied.VisionSensors.UnityIl2Cpp`:**
  - Plug into `Candied.Platforms.Unity.Shared` (reusing Unity detection, offline `AssetsTools.NET` extraction, input extraction, and BepInEx log parsing).
  - Implement BepInEx 6 Bleeding Edge (`Unity.IL2CPP`) provisioning, `Cpp2IL` / `Il2CppInterop` dummy assembly code extraction into `ISymbolIndex`, and `Il2CppInterop` Harmony mod scaffolding.
* **Phase 3 — Scripted & Native Engines (`Godot`, `Lua`, Custom C++/Emulators):**
  - Add `Candied.Platforms.Godot` (`.pck` extraction & GDScript symbol indexing via `ISymbolIndex.IndexExtractedSymbolsAsync`) and emulator memory/Lua bridge sensors.
