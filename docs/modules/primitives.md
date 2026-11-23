# Module: Candied.Primitives

## 1. Overview
`Candied.Primitives` is the zero-dependency foundational package for the entire solution. It contains only language-level monads, strongly-typed identifiers, and shared immutable value objects.

* **Namespace:** `Candied.Primitives`
* **Dependencies:** None (Pure .NET BCL)
* **Code Scope:** < 100 lines total

---

## 2. Types & Contracts

### A. `Result<TValue, TError>`
A readonly struct representing either a successful outcome (`Value`) or a failed outcome (`Error`). Avoids throwing unchecked exceptions across module boundaries.

```csharp
public readonly struct Result<TValue, TError>
{
    public bool IsSuccess { get; }
    public bool IsFailure { get; }
    public TValue Value { get; }  // Throws if IsFailure
    public TError Error { get; }  // Throws if IsSuccess
}
```

#### Usage Example:
```csharp
// Successful result via implicit conversion or factory
Result<GameEnvironment, DetectionError> ok = env;
Result<GameEnvironment, DetectionError> ok2 = Result.Ok<GameEnvironment, DetectionError>(env);

// Failure result
Result<GameEnvironment, DetectionError> fail = new DetectionError("Directory not found");
```

### B. Strongly-Typed Identifiers
Readonly record structs that prevent primitive obsession (passing an arbitrary string where a specific ID is expected):
* `GameId(string Value)`: Unique identifier for a detected game installation.
* `SymbolId(string Value)`: Fully-qualified identifier for an indexed code symbol.
* `PluginGuid(string Value)`: BepInEx plugin GUID (e.g. `com.author.modname`).

### C. `Unit`
Represents a void return in functional operations where an operation succeeds without returning data:
```csharp
public readonly record struct Unit;
```
