# Module: Candied.Primitives

## 1. Overview
`Candied.Primitives` is the zero-dependency foundational package for the entire Candied solution. It contains only language-level functional monads, strongly-typed engine-agnostic identifiers, and shared immutable value primitives.

* **Namespace:** `Candied.Primitives`
* **Target Framework:** `net9.0`
* **Dependencies:** None (Pure .NET BCL; verified by `Candied.ArchitectureTests`)
* **Engine Agnosticism:** Zero references to Unity, BepInEx, C# assemblies, or OS APIs.

---

## 2. Types & Contracts

### A. `Result<TValue, TError>` & `Result`
A `readonly struct` representing either a successful outcome (`Value`) or a typed domain failure (`Error`), eliminating unchecked exception control flow across hexagonal boundaries.

```csharp
public readonly struct Result<TValue, TError>
{
    public bool IsSuccess { get; }
    public bool IsFailure { get; }
    public TValue Value { get; }  // Throws InvalidOperationException if IsFailure
    public TError Error { get; }  // Throws InvalidOperationException if IsSuccess
}
```

### B. `Unit`
Represents a typed `void` return value in functional pipelines where an operation succeeds without returning a payload:

```csharp
public readonly record struct Unit;
```

### C. Engine-Agnostic Strongly-Typed Identifiers
Readonly record structs preventing primitive obsession across target platforms (PC executables, scripted engines, and emulator + ROM pairs):

* `TargetId(string Value)`: Deterministic identifier for a game installation, executable, or ROM/ISO target.
* `SymbolId(string Value)`: Fully-qualified identifier for an indexed symbol (class, struct, function, script global, or native export).
* `ModId(string Value)`: Platform-neutral mod identifier (e.g., reverse-domain `com.author.modname` or package slug).
* `ProcessId(int Value)`: Operating system process identifier for a hosted game or emulator runner.
* `SceneNodeId(string Value)`: Stable runtime scene graph node identifier returned by Vision sensors.

### D. `AssetFormat`
Enumeration describing canonical extracted asset representations in Zone 3:
* `Png`, `Json`, `Text`, `Binary`, `AudioWav`, `AudioOgg`, `ShaderText`, `Unknown`.
