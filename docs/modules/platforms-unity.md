# Modules: Unity Platform Family (`Unity.Shared`, `Unity.Mono`, `VisionSensors.UnityMono`)

## 1. Vertical Platform Slice Overview

In Candied's multi-platform architecture, all Unity-specific logic is isolated inside dedicated platform and sensor assemblies so `Candied.Core`, `Candied.Infrastructure`, `Candied.Vision`, and `Candied.Studio` remain 100% engine-agnostic:

1. **`src/Platforms/Candied.Platforms.Unity.Shared`** (`net9.0`): Reusable Unity inspection, asset extraction, input binding extraction, and BepInEx log parsing shared between `Unity.Mono` and future `Unity.Il2Cpp`.
2. **`src/Platforms/Candied.Platforms.Unity.Mono`** (`net9.0`): Complete `ITargetPlatform` (`"unity.mono"`) implementing ILSpy decompilation, BepInEx 5 provisioning, C# Harmony mod scaffolding/building, and the dynamic `unity-add-reference` tool.
3. **`src/VisionSensors/Candied.VisionSensors.UnityMono`** (`net35;net472`): Lightweight in-game BepInEx 5 HTTP Vision sensor speaking `Candied.Vision.Protocol` v2.0.

---

## 2. `Candied.Platforms.Unity.Shared`

Exposes `AddUnitySharedServices()` and clean contracts (`Candied.Platforms.Unity.Shared.Contracts`) reusable by any Unity runtime backend:

* **`IUnitySharedInspector` (`UnitySharedInspector`):** Detects `<Game>_Data` layouts, parses Unity engine versions from `globalgamemanagers` / `data.unity3d`, distinguishes **Mono** (`Managed/Assembly-CSharp.dll` or MonoBleedingEdge) from **IL2CPP** (`GameAssembly.dll` + `il2cpp_data/Metadata/global-metadata.dat`), inspects PE headers (`x64`/`x86`), and detects BepInEx installations.
* **`IUnityAssetPipeline` (`UnityAssetsPipelineCapability`):** Offline `AssetsTools.NET` scanner and extractor supporting `.assets`, `resources.assets`, and UnityFS AssetBundles (`.bundle`), with embedded `classdata.tpk`, texture decoding (`RGBA32`, `ARGB32`, `RGB24`, `BGRA32`, `DXT1`, `DXT5`, `BC7`), sprite rect cropping, and collision-safe filename disambiguation.
* **`IUnityRuntimeLogReader` (`BepInExRuntimeLogCapability`):** Non-locking `FileShare.ReadWrite` reader and multi-line stack-trace parser (`BepInExLogParser`) for `BepInEx/LogOutput.log`.
* **`IUnityInputExtractor` (`UnityInputProfileExtractor`):** Offline parser for legacy `InputManager.asset` and Unity `.inputactions` JSON files to seed `GameControlProfile` (`controls.json`) automatically.

---

## 3. `Candied.Platforms.Unity.Mono` (`platformId: "unity.mono"`)

Registered via `services.AddUnityMonoPlatform()`, binding `UnityMonoTargetPlatform` (`Priority: 100`), `UnityMonoToolProvider`, and `UnityMonoGuideProvider`:

* **`UnityMonoTargetPlatform`:** Matches Unity directories where `RuntimeVariant == Mono`. Classifies managed `.dll` assemblies into `CoreLogic`, `ModularLogic`, and `AuxiliaryOrThirdParty` (`UnityAssemblyFilter`) and detects target runtime framework (`net35`, `net472`, `netstandard2.0`, `netstandard2.1`).
* **`ILSpyCodeKnowledgeCapability` (`ICodeKnowledgeCapability`):** Decompiles individual managed modules (`extract-code(moduleName)`) into Zone 3 (`extracted/<ModuleName>/`) with `.cache` metadata validation, then indexes AST symbols and FTS5 source bodies via `ISymbolIndex`.
* **`BepInEx5EnvironmentSetupCapability` (`IEnvironmentSetupCapability`):** Downloads/caches architecture-matched BepInEx 5 archives (`BepInEx_win_{arch}_{ver}.zip` / `BepInEx_{arch}_{ver}.zip` in `.candied/cache/bepinex-mono/`), writes `BepInEx.cfg`, deploys `Candied.VisionSensors.UnityMono.dll` + `Candied.Vision.Protocol.dll` into `BepInEx/plugins/Candied.VisionSensor/`, and records every file in `.candied_manifest.json` for deterministic `rollback`.
* **`BepInEx5ModWorkspaceCapability` (`IModWorkspaceCapability`):**
  * Scaffolds clean SDK-style BepInEx 5 + HarmonyX C# projects in Zone 2 (`mods/<ModName>/`) with `BepInEx.AssemblyPublicizer.MSBuild` and optional `AssetLoader.cs` (`includeAssetPipeline: true`).
  * Runs `dotnet build`, parses MSBuild diagnostics (`MsBuildDiagnosticParser`), detects missing Unity module references (`CS0012` / `CS0246`), and emits actionable `unity-add-reference` resolution hints.
* **Dynamic Platform Tool (`UnityMonoToolProvider` -> `unity-add-reference`):**
  * Dynamically advertised via `IPlatformToolProvider` (never hardcoded in `Candied.Core`). Safely injects `<Reference Include="..."><HintPath>...</HintPath><Private>false</Private><Publicize>true</Publicize></Reference>` into a mod `.csproj`.

---

## 4. `Candied.VisionSensors.UnityMono`

Multi-targeted (`net35;net472`) BepInEx 5 plugin (`com.candied.visionsensor.unitymono`) implementing `Candied.Vision.Protocol` v2.0 inside running Unity Mono games:

* Starts `VisionHttpServer` on `127.0.0.1:{CANDIED_PORT}` with token validation (`X-Candied-Token`) and marshals Unity API calls onto the main game loop via `MainThreadDispatcher`.
* Captures frames (`ScreenshotCapturer` via `ScreenCapture` or depth-sorted offscreen `Camera` render), enumerates uGUI `Selectable` elements (`UguiProvider`), inspects cameras and generic `VisionSceneNodeDto` renderers (`SceneObjectsInspector`), and inspects loaded AssetBundles/Resources (`RuntimeAssetsInspector`).
* Dispatches 3-channel input (`InputDispatcher` + `VirtualInputDeviceManager`): isolated virtual mouse/keyboard/gamepad devices in `UnityEngine.InputSystem`, Harmony postfixes on legacy `UnityEngine.Input` & `Application.isFocused`, and Win32 `PostMessage` fallback.
