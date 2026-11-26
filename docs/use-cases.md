# Use Case Scenarios

This document outlines end-to-end user and AI agent workflows enabled by Candied's multi-platform modding and 2-Tier live vision architecture.

---

## Scenario 1: Creating a Gameplay or Accessibility Mod (PC Game)

**Goal:** Describe a desired gameplay or UI improvement in plain language and turn it into a verified, reversible mod.

```mermaid
sequenceDiagram
    autonumber
    actor User
    participant Agent as AI Agent
    participant Candied as Candied MCP Workbench
    participant Studio as Candied Studio (2-Tier Vision)

    User->>Agent: "Increase movement speed and add a quick-craft hotkey in my game"
    Agent->>Candied: inspect(targetPath)
    Candied-->>Agent: TargetDescriptor (platformId: "unity.mono", capabilities, modules)

    opt Mod loader or Vision sensor not yet provisioned
        Agent->>User: Asks confirmation to provision mod loader + Vision sensor
        User->>Agent: Confirms
        Agent->>Candied: setup(targetPath)
        Candied-->>Agent: ProvisioningReport (.candied_manifest.json saved)
    end

    Agent->>Candied: extract-code(moduleName: "Assembly-CSharp")
    Agent->>Candied: query(searchTerm: "PlayerMovement") & search-code(pattern: "moveSpeed")
    Agent->>Candied: type(fullTypeName: "Game.PlayerMovement")

    Agent->>Candied: scaffold(modName: "QuickMoveMod", targetPath)
    Agent->>Candied: build(projectFilePath: "mods/QuickMoveMod/QuickMoveMod.csproj")

    Agent->>Candied: game-launch(targetPath)
    Candied->>Studio: Hosts game window (auto-upgrades Tier 1 BaseWindow -> Tier 2 SensorBridge)
    Agent->>Candied: screenshot() & send-input / trigger-action("Confirm")
    Agent->>Candied: logs(minLevel: "Info") & game-close()
```

---

## Scenario 2: Live Inspection & Control Mapping on Any Window or Emulator

**Goal:** Visually inspect, map controls, and navigate a custom C++ game or an emulated console ROM (`targetPath` + `runnerPath`) that has **no** in-game sensor plugin.

1. The user or agent calls `inspect(targetPath: "C:/Roms/Game.iso", runnerPath: "C:/Emulators/pcsx2-qt.exe")`. Candied resolves `generic.window` with `LiveVisionSensor` support via Studio Tier 1.
2. Calling `game-launch(targetPath, runnerPath)` opens `Candied.Studio`, embeds the emulator viewport in **Tier 1 (`BaseWindow`)** mode, and captures screenshots immediately via Win32 `PrintWindow`/`BitBlt`.
3. The user or agent defines semantic bindings (`Confirm` -> Gamepad `South`, `Pause` -> Gamepad `Start`) via `set-controls` or Studio's **🎮 Comandi** tab (`controls.json`), then interacts cleanly via `trigger-action` or `send-input(channel: "gamepad", ...)` verified by follow-up `screenshot` calls.

---

## Scenario 3: Diagnosing Mod Conflicts or Runtime Exceptions

1. The user reports an unexpected behavior or crash when loading mods.
2. The agent invokes `logs(targetPath, minLevel: "Warning")` to retrieve parsed multi-line stack traces via the target platform's `IRuntimeLogCapability`.
3. Using `search-code` and `type` against extracted modules in Zone 3, the agent pinpoints the failing hook or missing reference without modifying the game installation.
