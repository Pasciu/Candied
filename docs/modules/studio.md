# Module: Candied.Studio

## 1. Overview
`Candied.Studio` (`net9.0-windows`, WPF + Win32 Interop) is a **100% pure, standalone 2-Tier Live Vision & Window Host Application**.

* **Namespace:** `Candied.Studio`
* **Dependencies:** `Candied.Primitives`, `Candied.Vision.Protocol`, `Candied.Vision` **ONLY**
* **Strict Isolation Invariant (Enforced by `Candied.ArchitectureTests`):**
  `Candied.Studio` has **ZERO** references to `Candied.Core`, `Candied.Infrastructure`, `Candied.Platforms.*`, decompilers (`ILSpy`), SQLite symbol indexes, or mod compilers. It knows nothing about how mods are compiled; its sole responsibility is hosting any PC game or emulator window cleanly and providing 2-Tier live visual perception + 3-channel input.

---

## 2. Two-Tier Live Vision Architecture

1. **Tier 1 — Universal Base Window Mode (`VisionTier.BaseWindow`):**
   * Active immediately upon launching *any* Windows game executable or emulator + ROM (`RunnerPath` + `TargetPath`), with zero in-game plugins required and zero pre-registration allowlist.
   * **Process Tree Containment:** Assigns the launched process to a Win32 Job Object (`JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE`) via `WindowsJobObject` so orphan child processes never survive Studio exit or `game-close`.
   * **Window Discovery & Embedding:** `Win32WindowLocator` locates the primary rendered top-level window across the target process tree (ignoring console/splash helpers), and `Win32WindowEmbedder` + `TargetHwndHost` reparent it seamlessly inside Studio's aspect-locked viewport (`WS_CHILD`, `ViewportCalculator`).
   * **Tier 1 Screenshot Capture:** Captures the hosted window via Win32 `PrintWindow(PW_RENDERFULLCONTENT)` with GDI `BitBlt` fallback (`TargetHostController.CaptureWindowPng`).
   * **Tier 1 3-Channel Input Dispatch:** Dispatches `pointer` (`WM_MOUSEMOVE`, `WM_LBUTTONDOWN/UP`, `WM_RBUTTONDOWN/UP`, `WM_MBUTTONDOWN/UP`), `keyboard`, and `gamepad` (mapped via `VirtualKeyMapper` to `WM_KEYDOWN`/`WM_KEYUP`) directly to the hosted `HWND` without hijacking the user's physical OS cursor.

2. **Tier 2 — Engine Sensor Bridge (`VisionTier.SensorBridge`):**
   * Before spawning the target process, `TargetHostController` injects `CANDIED_PORT` and `CANDIED_TOKEN` into the child process environment.
   * Background polling automatically detects when an in-game sensor (such as `Candied.VisionSensors.UnityMono`) responds to `GET /hello` and upgrades the session seamlessly to `VisionTier.SensorBridge`, unlocking isolated virtual input devices, scene hierarchy inspection, interactive UI element bounds, and runtime asset inspection.

---

## 3. Local HTTP Host Server (`StudioHttpServer`)

Studio binds an ephemeral localhost HTTP server (`127.0.0.1:{studioPort}`, authenticated via `X-Candied-Studio-Token`) and writes `%APPDATA%/Candied/studio_session.json`:

* `GET /status`: Reports Studio window visibility/monitor bounds, hosted process status, active `VisionTier` (`BaseWindow` vs `SensorBridge`), viewport dimensions, and sensor endpoint metadata.
* `POST /launch`: Accepts direct `{ targetPath, runnerPath?, extraArguments? }` (updating recent targets history in `recent_targets.json`), launches & embeds the target, and ensures Studio's window is visible on screen.
* `POST /close`: Terminates the hosted process tree cleanly via Job Object.
* `GET /screenshot`: Routes to Tier 2 sensor `GET /screenshot` when connected (falling back automatically to Tier 1 `CaptureWindowPng` if the sensor frame is uniform/unavailable or when running in `BaseWindow` mode).
* `POST /input`: Routes 3-channel `VisionInputRequest` to Tier 2 sensor `POST /input` when connected, or executes Tier 1 Win32 `HWND` input dispatch.

---

## 4. Studio UI & Interactive Controls Tab (`MainWindow.xaml`)

* **Top Launch Bar:** Direct file/folder picker for any PC game or ROM (`TargetPath`), optional Emulator/Runner picker (`RunnerPath`), recent targets dropdown, Launch/Stop controls, Screenshot button, and live `Tier 1: BaseWindow` / `Tier 2: SensorBridge` status badge.
* **4-Tab Inspector Panel:**
  1. **Stato & Capacità:** Active engine/window metrics, resolution, and declarative Vision capabilities.
  2. **UI Elements:** Interactive UI hierarchy when Tier 2 `ui_elements` is active.
  3. **🎮 Comandi (`GameControlProfile`):** Displays and edits semantic action bindings (`Confirm`, `Cancel`, `Pause`, custom actions across `Keyboard`, `Gamepad`, `Mouse`) persisted to `controls.json`, with one-click live test buttons (`▶ Prova`) and Virtual D-Pad / Face / Start-Select quick pad.
  4. **Log Sessione:** Timestamped diagnostic stream also persisted to `%APPDATA%/Candied/logs/studio.log`.
