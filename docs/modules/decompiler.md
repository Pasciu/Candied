# Module: Candied.Decompiler

## 1. Overview
`Candied.Decompiler` handles game inspection, assembly classification, PE metadata auditing, whole-project decompilation, Roslyn AST symbol parsing, SQLite index storage, and generic multi-type offline Unity asset extraction.

* **Namespace:** `Candied.Decompiler`
* **Dependencies:** `Candied.Core`, `Candied.Primitives`
* **External Packages:**
  * `ICSharpCode.Decompiler` (ILSpy whole-project decompiler engine)
  * `Microsoft.CodeAnalysis.CSharp` (Roslyn AST parser)
  * `Microsoft.Data.Sqlite` (`9.0.23`, fast local SQLite storage with patched native SQLite runtime)
  * `AssetsTools.NET` (Unity .assets and AssetBundle deserializer)
  * `AssetsTools.NET.Texture` (Texture/Sprite decoder)
  * `StbImageWriteSharp` (Lightweight PNG image serializer)

---

## 2. Components & Adapters

In compliance with Multi-Level Fractal Modularity (Rule 8 in `AGENTS.md`), each adapter implements **exactly one port**:

```
Candied.Decompiler/
├── DependencyInjection.cs               <-- Sole public entry point (AddDecompilerInfrastructure)
├── Adapters/
│   ├── UnityGameDetectorAdapter.cs          <-- Implements IGameDetector (internal, discovery & environment audit)
│   ├── ILSpyDecompilerEngineAdapter.cs      <-- Implements IDecompilerEngine (internal, decompilation only)
│   ├── SqliteSymbolIndexAdapter.cs          <-- Implements ISymbolIndex (internal, Roslyn symbol index & query)
│   ├── SqliteAssetIndexAdapter.cs           <-- Implements IAssetIndex (internal, persistent SQLite asset index & FTS)
│   └── AssetsToolsAssetExtractorAdapter.cs  <-- Implements IAssetExtractor (internal, offline asset extraction)
├── Resources/
│   └── classdata.tpk                        <-- Embedded Unity type package (290 KB)
└── Internal/
    ├── RoslynSymbolExtractor.cs             <-- Roslyn syntax walker utility (internal)
    ├── UnityAssemblyFilter.cs               <-- Pure assembly classification and tier grouping (internal)
    ├── DecompileCacheData.cs                <-- Decompilation cache metadata record (internal)
    └── Assets/
        ├── UnityAssetContainerScanner.cs    <-- Scans .assets, .bundle, .ab with memory-bounded sequential unloading
        ├── UnityCrossFileResolver.cs        <-- Resolves external sprite texture dependencies across containers
        ├── UnityTextureDecoder.cs           <-- UV slicing, BGRA decoding, and 90° atlas packing un-rotation
        ├── UnityMultiTypeAssetExtractor.cs  <-- Extracts Texture2D, Sprite, TextAsset, and MonoBehaviour (JSON)
        └── FileNameDisambiguator.cs         <-- Sanitizes filenames and avoids collision overwrites via PathID
```

---

## 3. Subsystem Breakdown

### A. Game Discovery & Environment Audit Pipeline (`UnityGameDetectorAdapter` & `UnityAssemblyFilter`)
* **Port:** `IGameDetector`
* **Responsibility:** Exclusively inspects filesystem layout to identify Unity games, detect runtime environment, and classify game assemblies:
  1. Scans target directory for `*_Data` directories and locates `Managed/`.
  2. Inspects presence of `GameAssembly.dll` or `il2cpp_data` to classify `EngineType.Mono` vs `EngineType.Il2Cpp`.
  3. Audits the PE Header of the game executable or `UnityPlayer.dll` to detect machine architecture (`x64`, `x86`, `ARM64`).
  4. Inspects `BepInEx/core/` to detect BepInEx installation status and extract product/file version (e.g. `5.4.21.0`).
  5. Inspects PE custom metadata on the primary game assembly to detect the target .NET framework (`net472`, `netstandard2.0`, etc.).
  6. Discovers and categorizes assemblies into semantic tiers via pure `UnityAssemblyFilter`:
     - **CoreGame (`AssemblyTier.CoreGame`):** Assemblies matching `[GameName].*.dll`, `Assembly-CSharp.dll`, and `Assembly-CSharp-firstpass.dll`.
     - **GameModular (`AssemblyTier.GameModular`):** Non-standard gameplay logic assemblies.
     - **AuxiliaryOrThirdParty (`AssemblyTier.AuxiliaryOrThirdParty`):** Excludes universal BCL/framework libraries (`mscorlib`, `netstandard`, `System.*`, `Microsoft.*`), Unity engine modules (`UnityEngine.*`, `Unity.*`), and standard third-party middleware (`Newtonsoft.Json`, `DOTween`, `Steamworks`, `Rewired`).
  7. Cross-references each discovered assembly against `.candied/games/<GameName>/decompiled/.{assembly}.cache` to mark `isDecompiled`.
  8. Emits diagnostic warnings (e.g., missing BepInEx, BepInEx 6 pre-release on Mono, or IL2CPP runtime).

### B. Decompilation Pipeline (`ILSpyDecompilerEngineAdapter`)
* **Port:** `IDecompilerEngine`
* **Responsibility:** Exclusively manages assembly decompilation using ILSpy with intelligent file-level caching:
  1. Checks for existing decompilation cache marker (`.{assemblyName}.cache`).
     - Verifies assembly file size in bytes and last write timestamp (`Ticks`).
     - Verifies that decompiled source files actually exist on disk.
     - If unchanged, immediately returns cached `DecompilationOutput` (<1ms), bypassing the decompiler completely.
  2. If cache miss or invalidated:
     - Instantiates `ICSharpCode.Decompiler.CSharp.ProjectDecompiler.WholeProjectDecompiler` with `UniversalAssemblyResolver`.
     - Adds the game's `Managed/` directory to assembly resolution search paths so cross-references (`UnityEngine.dll`) resolve without errors.
     - Exports full `.cs` source tree organized by namespace to the isolated per-assembly decompiled directory (`.candied/games/<GameName>/decompiled/<AssemblyName>/`).
     - Writes `.{assemblyName}.cache` metadata record inside the assembly's folder for future invocations.
     - Emits progress reports and returns `DecompilationOutput`.

### C. AST Extraction & Symbol Indexing (`RoslynSymbolExtractor` + `SqliteSymbolIndexAdapter`)
* **Port:** `ISymbolIndex`
* **Responsibility:** Parses C# source code with Roslyn, extracts symbol signatures and type definitions, and stores them in SQLite with incremental change detection.

1. **Incremental Change Detection (`indexed_assemblies`):**
   * Before parsing, checks file count and latest file write ticks across all `.cs` files in the assembly's `sourceDirectory`.
   * Queries SQLite `indexed_assemblies` table by `assembly_name`. If counts and timestamps match and symbols exist, returns `Unit` immediately (<3ms).
   * If files have changed: deletes previous symbols only for the specified target assembly (`DELETE FROM symbols WHERE assembly_name = @assembly_name;`), fully preserving other indexed assemblies.

2. **Roslyn Extraction & AST Walker Optimization:**
   * Parses `.cs` code with `CSharpSyntaxTree.ParseText(..., LanguageVersion.Latest)`.
   * Member-level traversal (`root.Members` and nested `typeDecl.Members`). Method bodies and statements are skipped during symbol extraction for optimal throughput.
   * Accurately resolves hierarchical nested types (`Namespace.OuterClass.InnerClass`).
   * Extracts classes, structs, interfaces, records, enums, methods, constructors, properties, and fields.
   * Preserves 100% full source code for every type via `typeDecl.ToFullString()` in `raw_source_code` (`type_details`).

### D. Persistent Asset Indexing Pipeline (`SqliteAssetIndexAdapter`)
* **Port:** `IAssetIndex`
* **Responsibility:** Stores and queries discovered game asset metadata in `.candied/games/<GameName>/index.db` using SQLite and FTS5 for sub-5ms searches across large games.

**SQLite Schema (`DiscoveredAssets` & `FtsDiscoveredAssets`):**
```sql
CREATE TABLE IF NOT EXISTS DiscoveredAssets (
    PathId INTEGER NOT NULL,
    ContainerPath TEXT NOT NULL,
    ContainerName TEXT NOT NULL,
    Name TEXT NOT NULL,
    AssetType TEXT NOT NULL,
    Width INTEGER NOT NULL,
    Height INTEGER NOT NULL,
    ByteSize INTEGER NOT NULL,
    Extension TEXT,
    PRIMARY KEY (ContainerPath, PathId)
);

CREATE INDEX IF NOT EXISTS IX_DiscoveredAssets_Name ON DiscoveredAssets(Name);
CREATE INDEX IF NOT EXISTS IX_DiscoveredAssets_Type ON DiscoveredAssets(AssetType);
CREATE INDEX IF NOT EXISTS IX_DiscoveredAssets_Container ON DiscoveredAssets(ContainerName);

CREATE VIRTUAL TABLE IF NOT EXISTS FtsDiscoveredAssets USING fts5(
    Name, ContainerName, AssetType, content='DiscoveredAssets', content_rowid='rowid'
);
```

### E. Offline Multi-Type Unity Asset Extraction Pipeline (`AssetsToolsAssetExtractorAdapter`)
* **Port:** `IAssetExtractor`
* **Responsibility:** Coordinates physical reading and extraction of Unity visual assets, text/data assets, and serializable components directly from `.assets` archives and Addressable `.bundle` files on disk:

1. **Memory-Bounded Container Scanning (`UnityAssetContainerScanner`):**
   - Discovers all `.assets`, `.bundle`, `.ab`, and `.unity3d` archives in `{Game}_Data/` and `StreamingAssets/`.
   - Loads and unloads containers sequentially (`am.UnloadBundleFile`), maintaining constant memory usage \(O(1)\) and preventing OOM memory spikes.
2. **Cross-File Dependency Resolution (`UnityCrossFileResolver`):**
   - Automatically resolves sprite references to external textures (e.g. `sharedassets0.assets`) using `am.GetExtAsset(inst, texRef)` and `am.LoadDependencies(inst)`.
3. **Sprite Atlas UV & 90° Packing Rotation (`UnityTextureDecoder`):**
   - Inspects `m_RD.settingsRaw` flag bit 2 (`(settingsRaw & 0x4) != 0`).
   - If packed rotated in an atlas, transposes dimensions and restores original orientation with 90° clockwise rotation.
4. **Multi-Type Asset Extraction (`UnityMultiTypeAssetExtractor`):**
   - **`Texture2D`:** Decodes raw picture data and writes PNG files.
   - **`Sprite`:** Resolves texture, slices rectangular subregion, unpacks orientation, and exports PNG.
   - **`TextAsset`:** Extracts raw script bytes or strings, detects format (JSON, XML, CSV, TXT), and exports readable files.
   - **`MonoBehaviour` / `ScriptableObject`:** Recursively traverses `AssetTypeValueField` tree and serializes formatted JSON.
5. **Collision Disambiguation (`FileNameDisambiguator`):**
   - Prevents silent file overwrites when different archives contain identically-named assets (e.g. `icon`), appending `_{pathId}.{ext}`.
