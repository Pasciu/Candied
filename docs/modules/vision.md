# Modules: Candied.Vision.Protocol & Candied.Vision

## 1. Architectural Role
The Vision layer is completely decoupled from `Candied.Core`, `Candied.Infrastructure`, and all modding/decompilation modules. It powers **2-Tier Live Perception**, **3-Channel Input**, and **Hybrid Semantic Control Mapping** for both `Candied.Studio` and `Candied.Agent`.

It is split into two assemblies so in-game sensors running inside older game runtimes never pull modern .NET dependencies:

1. **`Candied.Vision.Protocol`** (`net35;net472;netstandard2.0;net9.0`, **zero external dependencies**)
2. **`Candied.Vision`** (`net9.0`, references only `Candied.Primitives` and `Candied.Vision.Protocol`)

---

## 2. `Candied.Vision.Protocol` (Wire Contract v2.0)

Provides the engine-agnostic localhost HTTP/JSON contract (`127.0.0.1:{CANDIED_PORT}` authenticated via `X-Candied-Token`):

* **Endpoints:**
  * `GET /hello` -> `VisionHelloDto` (`protocolVersion: "2.0"`, `sensorVersion`, `engineName`, `engineVariant`, `engineVersion`, `screenWidth`, `screenHeight`).
  * `GET /capabilities` -> `VisionCapabilitiesDto` (declarative capabilities list: `screenshot`, `ui_elements`, `scene_graph`, `runtime_assets`, `pointer_isolated`, `keyboard`, `gamepad`, plus `metadata` key/value pairs).
  * `GET /screenshot` -> Raw PNG bytes + headers `X-Candied-Uniform`, `X-Candied-Width`, `X-Candied-Height`, `X-Candied-Method`.
  * `POST /input` -> Unified **3-Channel `VisionInputRequest`** (`channel`: `"pointer"`, `"keyboard"`, `"gamepad"`; `action`: `"click"`, `"move"`, `"down"`, `"up"`, `"press"`, `"stick"`, `"isolate"`, `"release_isolation"`) returning `VisionInputResponse`.
  * `GET /ui/elements` -> `VisionUiElementsResponse` (bounding boxes in top-left pixel & normalized `[0..1]` space).
  * `GET /scene` & `GET /scene/objects` -> `VisionSceneInfoDto` & `VisionSceneGraphResponse` (`VisionSceneCameraDto` + generic `VisionSceneNodeDto` with `id`, `name`, `fullPath`, `nodeType`, `boundsCenter`, `boundsExtents`, `components`, and open `attributes` dictionary).
  * `GET /assets/loaded` -> `VisionRuntimeAssetsResponse` (loaded asset containers and in-memory objects).
  * `POST /wait-idle` & `POST /window/resize`.
* **Zero-Dependency Utilities:**
  * `JsonLite`: Deterministic serializer/parser working identically across `.NET 3.5` through `.NET 9.0`.
  * `CoordinateConverter`: Bi-directional conversions between top-left screenshot pixels, normalized `[0..1]` coordinates, and bottom-left engine screen coordinates.
  * `MainThreadDispatcher`: Thread-safe queue marshaling HTTP background thread requests onto a game loop's main thread.
  * `VirtualInputStateTracker`: Pure frame-accurate tracker for isolated virtual mouse coordinates/buttons, held keyboard keys, gamepad buttons, and left/right analog sticks.

---

## 3. `Candied.Vision` (Host Perception, Launch Profiles & Control Mapping)

* **2-Tier Vision Model (`VisionTier`):**
  * `VisionTier.BaseWindow` (**Tier 1**): Works immediately on *any* Win32 window or emulator without an in-game sensor.
  * `VisionTier.SensorBridge` (**Tier 2**): Automatically activated when an in-game or emulator HTTP Vision sensor responds on `CANDIED_PORT`.
* **Window Hosting & Emulator Launch (`WindowLaunchProfileBuilder`):**
  * Builds `WindowLaunchSpec` for both standalone PC targets (`TargetPath`) and emulated games (`RunnerPath` + ROM `TargetPath`).
  * Applies smart windowed arguments for known engines/emulators (Unity `-screen-fullscreen 0 -popupwindow`, Godot `--windowed --resolution`, PCSX2 `-nofullscreen --`, Dolphin `-b -e`, RetroArch).
* **Hybrid Control Mapping (`GameControlProfile` / `controls.json`):**
  * Persisted per target in `.candied/games/<Target>/controls.json` via `JsonGameControlProfileStoreAdapter` (`IGameControlProfileStore`).
  * `ControlActionResolver` resolves semantic actions (e.g., `"Confirm"`, `"Cancel"`, `"Jump"`, `"Inventory"`) into concrete `VisionInputRequest` payloads across `Mouse`, `Keyboard`, or `Gamepad`.
  * `VirtualKeyMapper` translates canonical key names (`Enter`, `Escape`, `Space`, `WASD`, arrows, `F1..F12`) and standard gamepad buttons (`South`, `East`, `West`, `North`, `DPad*`, `Start`, `Select`, shoulders/triggers) into Win32 Virtual-Key codes for Tier 1 dispatch.
* **Pure Frame Analysis (`PurePngDecoder`):**
  * Dependency-free PNG chunk decoder/encoder providing uniformity detection (`IsUniformColor`) and post-click visual crosshair annotation (`DrawClickMarker`).
* **Single-Port Adapters (`Candied.Vision.Adapters`):**
  * `HttpVisionSensorClientAdapter` (`IVisionSensorClient`)
  * `HttpStudioHostClientAdapter` (`IStudioHostClient`)
  * `DiskStudioSessionManagerAdapter` (`IStudioSessionManager`)
  * `JsonGameControlProfileStoreAdapter` (`IGameControlProfileStore`)
  * `JsonRecentTargetsStoreAdapter` (`IRecentTargetsStore`)
