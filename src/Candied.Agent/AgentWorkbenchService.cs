using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Candied.Core.Models;
using Candied.Core.Ports;
using Candied.Core.UseCases;
using Candied.Primitives;
using Candied.Core.Analysis;

namespace Candied.Agent;

public sealed class AgentWorkbenchService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        WriteIndented = false
    };

    private readonly ScaffoldModUseCase _scaffoldModUseCase;
    private readonly BuildModUseCase _buildModUseCase;
    private readonly ReadLogsUseCase _readLogsUseCase;
    private readonly GetTypeDetailUseCase _getTypeDetailUseCase;
    private readonly IGameDetector _gameDetector;
    private readonly IDocumentationGenerator? _documentationGenerator;
    private readonly CandiedStorageLayout _storageLayout;

    private readonly InspectGameEnvironmentUseCase? _inspectGameEnvironmentUseCase;
    private readonly DecompileAssemblyUseCase? _decompileAssemblyUseCase;
    private readonly SearchGameSymbolsUseCase? _searchGameSymbolsUseCase;
    private readonly SaveResearchNotesUseCase? _saveResearchNotesUseCase;
    private readonly SetupGameEnvironmentUseCase? _setupGameEnvironmentUseCase;
    private readonly RollbackGameEnvironmentUseCase? _rollbackGameEnvironmentUseCase;
    private readonly GetGameProcessStatusUseCase? _getGameProcessStatusUseCase;
    private readonly LaunchGameUseCase? _launchGameUseCase;
    private readonly StopGameUseCase? _stopGameUseCase;
    private readonly DiscoverAssetsUseCase? _discoverAssetsUseCase;
    private readonly ExtractAssetsUseCase? _extractAssetsUseCase;
    private readonly IndexAssetsUseCase? _indexAssetsUseCase;
    private readonly IVisionBridgeClient? _visionBridgeClient;
    private readonly IRegisteredGameRegistry? _gameRegistry;
    private readonly IStudioClient? _studioClient;
    private readonly SearchGameCodeUseCase? _searchGameCodeUseCase;
    private readonly AddProjectReferenceUseCase? _addProjectReferenceUseCase;

    private string? _activeGameDatabasePath;
    private string? _activeGameDirectory;

    public AgentWorkbenchService(
        ScaffoldModUseCase scaffoldModUseCase,
        BuildModUseCase buildModUseCase,
        ReadLogsUseCase readLogsUseCase,
        GetTypeDetailUseCase getTypeDetailUseCase,
        IGameDetector gameDetector,
        IDocumentationGenerator? documentationGenerator = null,
        CandiedStorageLayout? storageLayout = null,
        InspectGameEnvironmentUseCase? inspectGameEnvironmentUseCase = null,
        DecompileAssemblyUseCase? decompileAssemblyUseCase = null,
        SearchGameSymbolsUseCase? searchGameSymbolsUseCase = null,
        SaveResearchNotesUseCase? saveResearchNotesUseCase = null,
        SetupGameEnvironmentUseCase? setupGameEnvironmentUseCase = null,
        RollbackGameEnvironmentUseCase? rollbackGameEnvironmentUseCase = null,
        GetGameProcessStatusUseCase? getGameProcessStatusUseCase = null,
        LaunchGameUseCase? launchGameUseCase = null,
        StopGameUseCase? stopGameUseCase = null,
        DiscoverAssetsUseCase? discoverAssetsUseCase = null,
        ExtractAssetsUseCase? extractAssetsUseCase = null,
        IndexAssetsUseCase? indexAssetsUseCase = null,
        IVisionBridgeClient? visionBridgeClient = null,
        IRegisteredGameRegistry? gameRegistry = null,
        IStudioClient? studioClient = null,
        SearchGameCodeUseCase? searchGameCodeUseCase = null,
        AddProjectReferenceUseCase? addProjectReferenceUseCase = null)
    {
        _scaffoldModUseCase = scaffoldModUseCase;
        _buildModUseCase = buildModUseCase;
        _readLogsUseCase = readLogsUseCase;
        _getTypeDetailUseCase = getTypeDetailUseCase;
        _gameDetector = gameDetector;
        _documentationGenerator = documentationGenerator;
        _storageLayout = storageLayout ?? new CandiedStorageLayout(ResolveDefaultWorkspaceRoot());

        _inspectGameEnvironmentUseCase = inspectGameEnvironmentUseCase;
        _decompileAssemblyUseCase = decompileAssemblyUseCase;
        _searchGameSymbolsUseCase = searchGameSymbolsUseCase;
        _saveResearchNotesUseCase = saveResearchNotesUseCase;
        _setupGameEnvironmentUseCase = setupGameEnvironmentUseCase;
        _rollbackGameEnvironmentUseCase = rollbackGameEnvironmentUseCase;
        _getGameProcessStatusUseCase = getGameProcessStatusUseCase;
        _launchGameUseCase = launchGameUseCase;
        _stopGameUseCase = stopGameUseCase;
        _discoverAssetsUseCase = discoverAssetsUseCase;
        _extractAssetsUseCase = extractAssetsUseCase;
        _indexAssetsUseCase = indexAssetsUseCase;
        _visionBridgeClient = visionBridgeClient;
        _gameRegistry = gameRegistry;
        _studioClient = studioClient;
        _searchGameCodeUseCase = searchGameCodeUseCase;
        _addProjectReferenceUseCase = addProjectReferenceUseCase;
    }

    public CandiedStorageLayout StorageLayout => _storageLayout;

    public async Task<string> InspectGameEnvironmentAsync(
        string gameDirectory,
        CancellationToken ct = default)
    {
        Result<GameEnvironment, Candied.Core.Errors.DomainError> detectResult;
        if (_inspectGameEnvironmentUseCase != null)
        {
            detectResult = await _inspectGameEnvironmentUseCase.ExecuteAsync(gameDirectory, ct).ConfigureAwait(false);
        }
        else
        {
            var rawDetect = await _gameDetector.DetectGameAsync(gameDirectory, ct).ConfigureAwait(false);
            detectResult = rawDetect.IsSuccess
                ? Result.Ok<GameEnvironment, Candied.Core.Errors.DomainError>(rawDetect.Value)
                : Result.Fail<GameEnvironment, Candied.Core.Errors.DomainError>(rawDetect.Error);
        }

        if (detectResult.IsFailure)
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = detectResult.Error.Message,
                detail = detectResult.Error.Detail
            }, JsonOptions);
        }

        var gameEnv = detectResult.Value;
        _activeGameDirectory = gameDirectory;
        _activeGameDatabasePath = _storageLayout.GetDatabasePath(gameEnv.GameName);

        string nextStep;
        if (!gameEnv.BepInExInfo.IsInstalled)
        {
            nextStep = "BepInEx is not installed. Call 'setup' to provision it automatically before proceeding.";
        }
        else
        {
            var primaryAssembly = gameEnv.DiscoveredAssemblies
                .FirstOrDefault(a => a.Tier == AssemblyTier.CoreGame)
                ?? gameEnv.DiscoveredAssemblies.FirstOrDefault(a => a.Tier == AssemblyTier.GameModular);
            var asmTarget = primaryAssembly?.Name ?? "Assembly-CSharp";
            nextStep = $"BepInEx is installed. Call 'decompile' on primary game assembly '{asmTarget}' to inspect and index its classes.";
        }

        return JsonSerializer.Serialize(new
        {
            success = true,
            gameName = gameEnv.GameName,
            engineType = gameEnv.EngineType.ToString(),
            unityVersion = gameEnv.UnityVersion,
            architecture = gameEnv.Architecture,
            targetFramework = gameEnv.TargetFramework,
            bepInEx = new
            {
                isInstalled = gameEnv.BepInExInfo.IsInstalled,
                detectedVersion = gameEnv.BepInExInfo.DetectedVersion,
                rootPath = gameEnv.BepInExInfo.BepInExRootPath
            },
            assemblies = gameEnv.DiscoveredAssemblies.Select(a => new
            {
                name = a.Name,
                fullPath = a.FullPath,
                sizeBytes = a.SizeBytes,
                tier = a.Tier.ToString(),
                reason = a.Reason,
                isDecompiled = a.IsDecompiled
            }),
            warnings = gameEnv.EnvironmentWarnings,
            nextSuggestedStep = nextStep
        }, JsonOptions);
    }

    public async Task<Result<string, Candied.Core.Errors.DomainError>> ResolveActiveGameDirectoryAsync(
        string? explicitDirectory = null,
        CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(explicitDirectory))
        {
            _activeGameDirectory = explicitDirectory;
            return Result.Ok<string, Candied.Core.Errors.DomainError>(explicitDirectory);
        }

        if (!string.IsNullOrWhiteSpace(_activeGameDirectory))
        {
            return Result.Ok<string, Candied.Core.Errors.DomainError>(_activeGameDirectory);
        }

        if (_gameRegistry != null)
        {
            try
            {
                var registered = await _gameRegistry.ListRegisteredGamesAsync(ct).ConfigureAwait(false);
                if (registered.Count == 1)
                {
                    _activeGameDirectory = registered[0].GameRootPath;
                    return Result.Ok<string, Candied.Core.Errors.DomainError>(_activeGameDirectory);
                }

                if (registered.Count > 1)
                {
                    var choices = string.Join(", ", registered.Select(g => $"{g.Name} ({g.GameRootPath})"));
                    return Result.Fail<string, Candied.Core.Errors.DomainError>(new Candied.Core.Errors.DetectionError(
                        "Sono registrati più giochi. Specifica 'gameDirectory' o seleziona uno tra: " + choices));
                }
            }
            catch
            {
                // Ignore registry errors and fallback
            }
        }

        return Result.Fail<string, Candied.Core.Errors.DomainError>(new Candied.Core.Errors.DetectionError(
            "Nessuna sessione di gioco attiva e nessun gioco registrato trovato. Specifica 'gameDirectory' o registra un gioco."));
    }

    public async Task<string> DecompileAssemblyAsync(
        string? gameDirectory,
        string assemblyName,
        CancellationToken ct = default)
    {
        var dirRes = await ResolveActiveGameDirectoryAsync(gameDirectory, ct).ConfigureAwait(false);
        if (dirRes.IsFailure)
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = dirRes.Error.Message,
                detail = dirRes.Error.Detail
            }, JsonOptions);
        }
        var targetGameDir = dirRes.Value;

        if (string.IsNullOrWhiteSpace(assemblyName))
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = "Assembly name cannot be empty.",
                detail = "Target assembly name required."
            }, JsonOptions);
        }

        // Normalize assembly name by stripping directory path and .dll extension if provided
        assemblyName = Path.GetFileNameWithoutExtension(assemblyName);

        if (_decompileAssemblyUseCase == null)
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = "DecompileAssemblyUseCase is not configured in this environment.",
                detail = "Missing use case dependency."
            }, JsonOptions);
        }

        var result = await _decompileAssemblyUseCase.ExecuteAsync(targetGameDir, assemblyName, _storageLayout, ct).ConfigureAwait(false);
        if (result.IsFailure)
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = result.Error.Message,
                detail = result.Error.Detail
            }, JsonOptions);
        }

        var gameName = await ResolveGameNameAsync(targetGameDir, ct).ConfigureAwait(false);
        _activeGameDirectory = targetGameDir;
        _activeGameDatabasePath = _storageLayout.GetDatabasePath(gameName);

        return JsonSerializer.Serialize(new
        {
            success = true,
            assembly = result.Value.AssemblyName,
            sourceDirectory = result.Value.OutputDirectory,
            sourceFileCount = result.Value.SourceFileCount,
            artifacts = new
            {
                decompiledDirectory = result.Value.OutputDirectory,
                databasePath = _activeGameDatabasePath,
                docsDirectory = _storageLayout.GetDocsDir(gameName),
                overviewDoc = _storageLayout.GetOverviewDocPath(gameName),
                namespacesDoc = _storageLayout.GetNamespacesDocPath(gameName),
                symbolsIndexJson = _storageLayout.GetSymbolsIndexJsonPath(gameName)
            },
            nextSuggestedStep = $"Assembly '{result.Value.AssemblyName}' decompiled and indexed ({result.Value.SourceFileCount} source files). Call 'query' or 'type' to find target classes, methods, and fields to patch."
        }, JsonOptions);
    }

    public async Task<string> SearchGameSymbolsOnlyAsync(
        string searchTerm,
        string? gameDirectory = null,
        string? kind = null,
        string? namespacePrefix = null,
        int limit = 25,
        CancellationToken ct = default)
    {
        var dirRes = await ResolveActiveGameDirectoryAsync(gameDirectory, ct).ConfigureAwait(false);
        string? databasePath = null;
        if (dirRes.IsSuccess)
        {
            var gameName = await ResolveGameNameAsync(dirRes.Value, ct).ConfigureAwait(false);
            databasePath = _storageLayout.GetDatabasePath(gameName);
        }
        else
        {
            databasePath = _activeGameDatabasePath;
        }

        if (string.IsNullOrWhiteSpace(databasePath) || !File.Exists(databasePath))
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = "Database dei simboli non trovato o non ancora indicizzato.",
                suggestion = "Esegui prima il comando 'decompile(assemblyName)' sull'assembly di gioco primario (es. 'Assembly-CSharp') per popolare l'indice SQLite prima di cercare simboli."
            }, JsonOptions);
        }

        if (_searchGameSymbolsUseCase == null)
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = "SearchGameSymbolsUseCase is not configured in this environment.",
                detail = "Missing use case dependency."
            }, JsonOptions);
        }

        var query = BuildSymbolQuery(searchTerm, kind, namespacePrefix, limit);
        var result = await _searchGameSymbolsUseCase.ExecuteAsync(query, databasePath, ct).ConfigureAwait(false);

        if (result.IsFailure)
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = result.Error.Message,
                detail = result.Error.Detail,
                suggestion = "Esegui prima il comando 'decompile' sull'assembly di interesse per indicizzarlo."
            }, JsonOptions);
        }

        string? suggestion = result.Value.Count == 0
            ? $"Nessun simbolo trovato per '{searchTerm}'. Se appartiene a un assembly non ancora decompilato, esegui prima 'decompile(assemblyName)' (usa 'inspect' per vedere gli assembly disponibili)."
            : null;

        return JsonSerializer.Serialize(new
        {
            success = true,
            count = result.Value.Count,
            filter = new { kind = kind ?? "all", namespacePrefix, limit = query.Limit },
            symbols = result.Value,
            databasePath = databasePath,
            suggestion = suggestion
        }, JsonOptions);
    }

    public async Task<string> SearchGameCodeAsync(
        string pattern,
        string? gameDirectory = null,
        string? namespacePrefix = null,
        string? assemblyName = null,
        int maxResults = 25,
        CancellationToken ct = default)
    {
        var dirRes = await ResolveActiveGameDirectoryAsync(gameDirectory, ct).ConfigureAwait(false);
        string? databasePath = null;
        string? resolvedGameName = null;
        if (dirRes.IsSuccess)
        {
            resolvedGameName = await ResolveGameNameAsync(dirRes.Value, ct).ConfigureAwait(false);
            databasePath = _storageLayout.GetDatabasePath(resolvedGameName);
        }
        else
        {
            databasePath = _activeGameDatabasePath;
        }

        if (string.IsNullOrWhiteSpace(databasePath) || !File.Exists(databasePath))
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = "Database dei simboli non trovato o non ancora indicizzato.",
                suggestion = "Esegui prima il comando 'decompile(assemblyName)' sull'assembly di gioco primario (es. 'Assembly-CSharp') per popolare l'indice SQLite prima di cercare nel codice sorgente."
            }, JsonOptions);
        }

        if (_searchGameCodeUseCase == null)
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = "SearchGameCodeUseCase is not configured in this environment.",
                detail = "Missing use case dependency."
            }, JsonOptions);
        }

        int clampedLimit = Math.Clamp(maxResults <= 0 ? 25 : maxResults, 1, 200);
        int queryLimit = string.IsNullOrWhiteSpace(assemblyName) ? clampedLimit : Math.Min(clampedLimit * 4, 500);
        var query = new CodeSearchQuery(
            SearchTerm: pattern,
            NamespacePrefix: namespacePrefix,
            Limit: queryLimit);

        var result = await _searchGameCodeUseCase.ExecuteAsync(query, databasePath, ct).ConfigureAwait(false);
        if (result.IsFailure)
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = result.Error.Message,
                detail = result.Error.Detail
            }, JsonOptions);
        }

        IEnumerable<CodeSearchMatch> filtered = result.Value;
        if (!string.IsNullOrWhiteSpace(assemblyName))
        {
            string trimmedAsm = assemblyName.Trim();
            filtered = filtered.Where(m =>
                !string.IsNullOrWhiteSpace(m.AssemblyName) &&
                m.AssemblyName.Equals(trimmedAsm, StringComparison.OrdinalIgnoreCase));
        }

        var matches = filtered
            .Take(clampedLimit)
            .Select(m => new
            {
                fullTypeName = m.FullName,
                @namespace = m.Namespace,
                assemblyName = m.AssemblyName,
                lineNumber = m.LineNumber,
                snippet = m.Snippet,
                sourceFilePath = ResolveDecompiledSourcePath(resolvedGameName, m.AssemblyName, m.RelativeSourcePath)
            }).ToList();

        return JsonSerializer.Serialize(new
        {
            success = true,
            pattern = pattern,
            count = matches.Count,
            matches = matches,
            databasePath = databasePath,
            suggestion = matches.Count == 0
                ? $"Nessuna occorrenza di '{pattern}' trovata nel codice decompilato. Se il codice risiede in un altro assembly, esegui 'decompile(assemblyName)'."
                : "Usa 'type(fullTypeName, memberFilter=...)' o leggi direttamente 'sourceFilePath' per ispezionare il contesto completo."
        }, JsonOptions);
    }

    private static SymbolQuery BuildSymbolQuery(string searchTerm, string? kind, string? namespacePrefix, int limit)
    {
        int clampedLimit = Math.Clamp(limit <= 0 ? 25 : limit, 1, 200);
        string normalized = (kind ?? "all").Trim().ToLowerInvariant();

        return normalized switch
        {
            "types" or "type" => new SymbolQuery(searchTerm, SymbolKindFilter.TypeOnly, namespacePrefix, clampedLimit),
            "members" or "member" => new SymbolQuery(searchTerm, SymbolKindFilter.MemberOnly, namespacePrefix, clampedLimit),
            "class" => new SymbolQuery(searchTerm, SymbolKindFilter.TypeOnly, namespacePrefix, clampedLimit, ExactKind: SymbolKind.Class),
            "struct" => new SymbolQuery(searchTerm, SymbolKindFilter.TypeOnly, namespacePrefix, clampedLimit, ExactKind: SymbolKind.Struct),
            "interface" => new SymbolQuery(searchTerm, SymbolKindFilter.TypeOnly, namespacePrefix, clampedLimit, ExactKind: SymbolKind.Interface),
            "enum" => new SymbolQuery(searchTerm, SymbolKindFilter.TypeOnly, namespacePrefix, clampedLimit, EnumOnly: true),
            "method" => new SymbolQuery(searchTerm, SymbolKindFilter.MemberOnly, namespacePrefix, clampedLimit, ExactKind: SymbolKind.Method),
            "field" => new SymbolQuery(searchTerm, SymbolKindFilter.MemberOnly, namespacePrefix, clampedLimit, ExactKind: SymbolKind.Field),
            "property" => new SymbolQuery(searchTerm, SymbolKindFilter.MemberOnly, namespacePrefix, clampedLimit, ExactKind: SymbolKind.Property),
            "event" => new SymbolQuery(searchTerm, SymbolKindFilter.MemberOnly, namespacePrefix, clampedLimit, ExactKind: SymbolKind.Event),
            _ => new SymbolQuery(searchTerm, SymbolKindFilter.All, namespacePrefix, clampedLimit)
        };
    }

    private string? ResolveDecompiledSourcePath(string? gameName, string? assemblyName, string? relativeSourcePath)
    {
        if (string.IsNullOrWhiteSpace(gameName) ||
            string.IsNullOrWhiteSpace(assemblyName) ||
            string.IsNullOrWhiteSpace(relativeSourcePath))
        {
            return null;
        }

        var normalizedRel = relativeSourcePath
            .Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar);
        var fullPath = Path.Combine(_storageLayout.GetDecompiledDir(gameName), assemblyName, normalizedRel);
        return File.Exists(fullPath) ? fullPath : fullPath;
    }

    public async Task<string> SaveResearchNotesAsync(
        string notes,
        string? gameDirectory = null,
        CancellationToken ct = default)
    {
        var dirRes = await ResolveActiveGameDirectoryAsync(gameDirectory, ct).ConfigureAwait(false);
        if (dirRes.IsFailure)
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = dirRes.Error.Message,
                detail = dirRes.Error.Detail
            }, JsonOptions);
        }
        var targetGameDir = dirRes.Value;

        var gameName = await ResolveGameNameAsync(targetGameDir, ct).ConfigureAwait(false);
        if (_saveResearchNotesUseCase == null)
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = "SaveResearchNotesUseCase is not configured in this environment.",
                detail = "Missing use case dependency."
            }, JsonOptions);
        }

        var result = await _saveResearchNotesUseCase.ExecuteAsync(gameName, notes, _storageLayout, ct).ConfigureAwait(false);
        if (result.IsFailure)
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = result.Error.Message,
                detail = result.Error.Detail
            }, JsonOptions);
        }

        var docsDir = _storageLayout.GetDocsDir(gameName);
        var filePath = Path.Combine(docsDir, "RESEARCH.md");

        return JsonSerializer.Serialize(new
        {
            success = true,
            gameName = gameName,
            savedPath = filePath
        }, JsonOptions);
    }

    public async Task<string> GetTypeDetailAsync(
        string fullTypeName,
        string? gameDirectory = null,
        bool signaturesOnly = false,
        string? memberFilter = null,
        CancellationToken ct = default)
    {
        var dirRes = await ResolveActiveGameDirectoryAsync(gameDirectory, ct).ConfigureAwait(false);
        string? databasePath = null;
        string? resolvedGameName = null;
        if (dirRes.IsSuccess)
        {
            resolvedGameName = await ResolveGameNameAsync(dirRes.Value, ct).ConfigureAwait(false);
            databasePath = _storageLayout.GetDatabasePath(resolvedGameName);
        }
        else
        {
            databasePath = _activeGameDatabasePath;
        }

        if (string.IsNullOrWhiteSpace(databasePath))
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = "Database dei simboli non trovato o non ancora indicizzato.",
                suggestion = "Esegui prima il comando 'decompile(assemblyName)' sull'assembly contenente il tipo richiesto per indicizzarne i simboli prima di chiamare 'type'."
            }, JsonOptions);
        }

        var result = await _getTypeDetailUseCase.ExecuteAsync(fullTypeName, databasePath, ct).ConfigureAwait(false);

        if (result.IsFailure)
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = result.Error.Message,
                detail = result.Error.Detail,
                suggestion = $"Tipo '{fullTypeName}' non trovato nell'indice SQLite. Se appartiene a un assembly non ancora decompilato, esegui prima 'decompile(assemblyName)' specificando l'assembly corrispondente (usa 'inspect' per vedere gli assembly disponibili)."
            }, JsonOptions);
        }

        var detail = result.Value;
        string? sourceFilePath = ResolveDecompiledSourcePath(resolvedGameName, detail.AssemblyName, detail.RelativeSourcePath);

        IReadOnlyList<string> fieldSignatures = detail.FieldSignatures;
        IReadOnlyList<string> methodSignatures = detail.MethodSignatures;
        string? rawSourceCode = detail.RawSourceCode;

        if (!string.IsNullOrWhiteSpace(memberFilter))
        {
            string trimmedFilter = memberFilter.Trim();
            fieldSignatures = fieldSignatures
                .Where(f => f.Contains(trimmedFilter, StringComparison.OrdinalIgnoreCase))
                .ToList();
            methodSignatures = methodSignatures
                .Where(m => m.Contains(trimmedFilter, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (!signaturesOnly && !string.IsNullOrEmpty(rawSourceCode))
            {
                rawSourceCode = TypeMemberSourceExtractor.ExtractMatchingMembers(rawSourceCode, trimmedFilter);
            }
        }

        if (signaturesOnly)
        {
            rawSourceCode = null;
        }

        return JsonSerializer.Serialize(new
        {
            success = true,
            type = new
            {
                fullName = detail.FullName,
                @namespace = detail.Namespace,
                baseType = detail.BaseType,
                interfaces = detail.Interfaces,
                fieldSignatures = fieldSignatures,
                methodSignatures = methodSignatures,
                rawSourceCode = rawSourceCode,
                assemblyName = detail.AssemblyName,
                relativeSourcePath = detail.RelativeSourcePath
            },
            sourceFilePath = sourceFilePath,
            signaturesOnly = signaturesOnly,
            memberFilter = memberFilter
        }, JsonOptions);
    }

    public async Task<string> ScaffoldModAsync(
        ModScaffoldSpec spec,
        CancellationToken ct = default)
    {
        if (spec != null && !string.IsNullOrWhiteSpace(spec.OutputDirectory) && !Path.IsPathRooted(spec.OutputDirectory))
        {
            spec = spec with { OutputDirectory = ResolveWorkspacePath(spec.OutputDirectory) };
        }

        var result = await _scaffoldModUseCase.ExecuteAsync(spec!, ct).ConfigureAwait(false);

        if (result.IsFailure)
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = result.Error.Message,
                detail = result.Error.Detail
            }, JsonOptions);
        }

        return JsonSerializer.Serialize(new
        {
            success = true,
            manifest = result.Value,
            nextSuggestedStep = $"Mod scaffolded at '{result.Value.ProjectFilePath}'. Write your Harmony patch class in Patches/, then call 'build' to compile."
        }, JsonOptions);
    }

    public async Task<string> ScaffoldModByPathAsync(
        string modName,
        string pluginGuid,
        string version,
        string outputDirectory,
        string gameDirectory,
        IReadOnlyList<string>? publicizedAssemblies = null,
        bool includeAssetPipeline = false,
        CancellationToken ct = default)
    {
        var detectResult = await _gameDetector.DetectGameAsync(gameDirectory, ct).ConfigureAwait(false);
        if (detectResult.IsFailure)
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = detectResult.Error.Message,
                detail = detectResult.Error.Detail
            }, JsonOptions);
        }

        var targetOutputDir = !string.IsNullOrWhiteSpace(outputDirectory)
            ? ResolveWorkspacePath(outputDirectory)
            : _storageLayout.GetModProjectDir(modName);

        var spec = new ModScaffoldSpec(
            ModName: modName,
            PluginGuid: new PluginGuid(pluginGuid),
            Version: version,
            OutputDirectory: targetOutputDir,
            TargetGame: detectResult.Value,
            PublicizedAssemblyNames: publicizedAssemblies ?? Array.Empty<string>(),
            IncludeAssetPipeline: includeAssetPipeline
        );

        return await ScaffoldModAsync(spec, ct).ConfigureAwait(false);
    }

    public async Task<string> BuildModAsync(
        string projectFilePath,
        string configuration = "Debug",
        CancellationToken ct = default)
    {
        var resolvedProjectPath = projectFilePath;
        if (!string.IsNullOrWhiteSpace(projectFilePath) && !Path.IsPathRooted(projectFilePath))
        {
            var workspaceCandidate = ResolveWorkspacePath(projectFilePath);
            resolvedProjectPath = File.Exists(workspaceCandidate) || !File.Exists(Path.GetFullPath(projectFilePath))
                ? workspaceCandidate
                : Path.GetFullPath(projectFilePath);
        }

        var result = await _buildModUseCase.ExecuteAsync(resolvedProjectPath, configuration, ct).ConfigureAwait(false);

        if (result.IsFailure)
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = result.Error.Message,
                detail = result.Error.Detail
            }, JsonOptions);
        }

        var hints = result.Value.MissingReferenceHints;
        string nextStep;
        if (result.Value.IsSuccess)
        {
            nextStep = "Build succeeded! Mod DLL is deployed to BepInEx/plugins. Launch the game and run 'logs' if you need to inspect runtime behavior.";
        }
        else if (hints != null && hints.Count > 0)
        {
            var first = hints[0];
            nextStep = $"Build failed due to missing assembly reference(s): {string.Join(", ", hints.Select(h => h.AssemblyName))}. Call 'add-reference(projectFilePath, assemblyName=\"{first.AssemblyName}\")' and run 'build' again.";
        }
        else
        {
            nextStep = $"Build failed with {result.Value.Diagnostics.Count} diagnostics. Inspect the errors, fix the C# code in the mod project, and call 'build' again.";
        }

        return JsonSerializer.Serialize(new
        {
            success = true,
            report = result.Value,
            missingReferenceHints = hints,
            nextSuggestedStep = nextStep
        }, JsonOptions);
    }

    public async Task<string> AddProjectReferenceAsync(
        string projectFilePath,
        string assemblyName,
        bool publicize = false,
        string? gameDirectory = null,
        CancellationToken ct = default)
    {
        if (_addProjectReferenceUseCase == null)
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = "AddProjectReferenceUseCase is not configured in this environment.",
                detail = "Missing use case dependency."
            }, JsonOptions);
        }

        var resolvedProjectPath = projectFilePath;
        if (!string.IsNullOrWhiteSpace(projectFilePath) && !Path.IsPathRooted(projectFilePath))
        {
            var workspaceCandidate = ResolveWorkspacePath(projectFilePath);
            resolvedProjectPath = File.Exists(workspaceCandidate) || !File.Exists(Path.GetFullPath(projectFilePath))
                ? workspaceCandidate
                : Path.GetFullPath(projectFilePath);
        }

        string? resolvedGameDir = gameDirectory;
        if (string.IsNullOrWhiteSpace(resolvedGameDir))
        {
            var dirRes = await ResolveActiveGameDirectoryAsync(null, ct).ConfigureAwait(false);
            if (dirRes.IsSuccess)
            {
                resolvedGameDir = dirRes.Value;
            }
        }

        var result = await _addProjectReferenceUseCase.ExecuteAsync(
            resolvedProjectPath,
            assemblyName,
            publicize,
            resolvedGameDir,
            ct).ConfigureAwait(false);

        if (result.IsFailure)
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = result.Error.Message,
                detail = result.Error.Detail
            }, JsonOptions);
        }

        var val = result.Value;
        return JsonSerializer.Serialize(new
        {
            success = true,
            projectFilePath = val.ProjectFilePath,
            assemblyName = val.AssemblyName,
            dllPath = val.DllPath,
            publicized = val.Publicized,
            alreadyReferenced = val.AlreadyReferenced,
            nextSuggestedStep = val.AlreadyReferenced
                ? $"Assembly '{val.AssemblyName}' is already referenced in '{val.ProjectFilePath}'. Run 'build' to verify."
                : $"Added reference '{val.AssemblyName}' ({val.DllPath}). Run 'build' to recompile."
        }, JsonOptions);
    }

    public async Task<string> ReadLogsAsync(
        string? logFilePath = null,
        int maxLines = 200,
        LogLevel? minLevel = null,
        string? gameDirectory = null,
        CancellationToken ct = default)
    {
        string targetLogPath = logFilePath ?? string.Empty;

        if (string.IsNullOrWhiteSpace(targetLogPath))
        {
            var resolveRes = await ResolveActiveGameDirectoryAsync(gameDirectory, ct).ConfigureAwait(false);
            if (resolveRes.IsSuccess)
            {
                var bepLog = Path.Combine(resolveRes.Value, "BepInEx", "LogOutput.log");
                if (File.Exists(bepLog))
                {
                    targetLogPath = bepLog;
                }
                else
                {
                    return JsonSerializer.Serialize(new
                    {
                        success = false,
                        error = $"File di log non trovato in: {bepLog}",
                        detail = "La cartella BepInEx/LogOutput.log non esiste ancora. Avvia il gioco almeno una volta per generare i log, oppure specifica 'logFilePath'."
                    }, JsonOptions);
                }
            }
            else
            {
                return JsonSerializer.Serialize(new
                {
                    success = false,
                    error = resolveRes.Error.Message,
                    detail = resolveRes.Error.Detail
                }, JsonOptions);
            }
        }

        var result = await _readLogsUseCase.ExecuteAsync(targetLogPath, maxLines, minLevel, ct).ConfigureAwait(false);

        if (result.IsFailure)
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = result.Error.Message,
                detail = result.Error.Detail
            }, JsonOptions);
        }

        return JsonSerializer.Serialize(new
        {
            success = true,
            count = result.Value.Count,
            entries = result.Value
        }, JsonOptions);
    }

    public async Task<string> SetupGameEnvironmentAsync(
        string? gameDirectory,
        bool forceReinstall = false,
        bool enableConsoleLogging = true,
        CancellationToken ct = default)
    {
        var targetGameDir = !string.IsNullOrWhiteSpace(gameDirectory)
            ? gameDirectory
            : _activeGameDirectory;

        if (string.IsNullOrWhiteSpace(targetGameDir))
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = "Game directory was not specified and no active game session is cached. Call inspect first or provide gameDirectory.",
                detail = "Target game directory path required."
            }, JsonOptions);
        }

        if (_setupGameEnvironmentUseCase == null)
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = "SetupGameEnvironmentUseCase is not configured in this environment.",
                detail = "Missing use case dependency."
            }, JsonOptions);
        }

        var spec = new ProvisioningSpec(
            TargetVersion: null,
            ForceReinstall: forceReinstall,
            EnableConsoleLogging: enableConsoleLogging
        );

        var result = await _setupGameEnvironmentUseCase.ExecuteAsync(targetGameDir, spec, _storageLayout, ct).ConfigureAwait(false);
        if (result.IsFailure)
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = result.Error.Message,
                detail = result.Error.Detail
            }, JsonOptions);
        }

        _activeGameDirectory = targetGameDir;

        return JsonSerializer.Serialize(new
        {
            success = true,
            modLoader = result.Value.ModLoaderName,
            version = result.Value.Version,
            architecture = result.Value.Architecture,
            filesInstalled = result.Value.FilesInstalledCount,
            consoleLoggingConfigured = result.Value.ConsoleLoggingConfigured,
            manifestPath = result.Value.ManifestPath,
            nextSuggestedStep = "BepInEx provisioned successfully. Call 'decompile' on the primary game assembly."
        }, JsonOptions);
    }

    public async Task<string> RollbackGameEnvironmentAsync(
        string? gameDirectory,
        CancellationToken ct = default)
    {
        var targetGameDir = !string.IsNullOrWhiteSpace(gameDirectory)
            ? gameDirectory
            : _activeGameDirectory;

        if (string.IsNullOrWhiteSpace(targetGameDir))
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = "Game directory was not specified and no active game session is cached. Call inspect first or provide gameDirectory.",
                detail = "Target game directory path required."
            }, JsonOptions);
        }

        if (_rollbackGameEnvironmentUseCase == null)
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = "RollbackGameEnvironmentUseCase is not configured in this environment.",
                detail = "Missing use case dependency."
            }, JsonOptions);
        }

        var result = await _rollbackGameEnvironmentUseCase.ExecuteAsync(targetGameDir, _storageLayout, ct).ConfigureAwait(false);
        if (result.IsFailure)
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = result.Error.Message,
                detail = result.Error.Detail
            }, JsonOptions);
        }

        return JsonSerializer.Serialize(new
        {
            success = true,
            filesRemoved = result.Value.FilesRemovedCount,
            removedFiles = result.Value.RemovedFiles
        }, JsonOptions);
    }

    public async Task<string> GetGameProcessStatusAsync(
        string? gameDirectory = null,
        CancellationToken ct = default)
    {
        var targetGameDir = !string.IsNullOrWhiteSpace(gameDirectory)
            ? gameDirectory
            : _activeGameDirectory;

        if (string.IsNullOrWhiteSpace(targetGameDir))
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = "Game directory was not specified and no active game session is cached. Call inspect first or provide gameDirectory.",
                detail = "Target game directory path required."
            }, JsonOptions);
        }

        if (_getGameProcessStatusUseCase == null)
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = "GetGameProcessStatusUseCase is not configured in this environment.",
                detail = "Missing use case dependency."
            }, JsonOptions);
        }

        var detectResult = await _gameDetector.DetectGameAsync(targetGameDir, ct).ConfigureAwait(false);
        if (detectResult.IsFailure)
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = detectResult.Error.Message,
                detail = detectResult.Error.Detail
            }, JsonOptions);
        }

        var game = detectResult.Value;
        var result = await _getGameProcessStatusUseCase.ExecuteAsync(game, ct).ConfigureAwait(false);
        if (result.IsFailure)
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = result.Error.Message,
                detail = result.Error.Detail
            }, JsonOptions);
        }

        _activeGameDirectory = targetGameDir;
        var process = result.Value;
        var isRunning = process != null;

        var nextStep = isRunning
            ? "Game is running."
            : "Game is not running. Call 'launch' to start it in windowed mode.";

        return JsonSerializer.Serialize(new
        {
            success = true,
            isRunning = isRunning,
            gameName = game.GameName,
            process = process != null ? new
            {
                processId = process.Id.Value,
                processName = process.ProcessName,
                mainWindowTitle = process.MainWindowTitle,
                isResponding = process.IsResponding
            } : null,
            nextSuggestedStep = nextStep
        }, JsonOptions);
    }

    public async Task<string> LaunchGameAsync(
        string? gameDirectory = null,
        bool windowed = true,
        int width = 1280,
        int height = 720,
        CancellationToken ct = default)
    {
        var targetGameDir = !string.IsNullOrWhiteSpace(gameDirectory)
            ? gameDirectory
            : _activeGameDirectory;

        if (string.IsNullOrWhiteSpace(targetGameDir))
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = "Game directory was not specified and no active game session is cached. Call inspect first or provide gameDirectory.",
                detail = "Target game directory path required."
            }, JsonOptions);
        }

        if (_launchGameUseCase == null)
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = "LaunchGameUseCase is not configured in this environment.",
                detail = "Missing use case dependency."
            }, JsonOptions);
        }

        var detectResult = await _gameDetector.DetectGameAsync(targetGameDir, ct).ConfigureAwait(false);
        if (detectResult.IsFailure)
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = detectResult.Error.Message,
                detail = detectResult.Error.Detail
            }, JsonOptions);
        }

        var game = detectResult.Value;

        var launchOptions = new GameLaunchOptions(
            Windowed: windowed,
            WindowWidth: width,
            WindowHeight: height
        );

        var result = await _launchGameUseCase.ExecuteAsync(game, launchOptions, ct).ConfigureAwait(false);
        if (result.IsFailure)
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = result.Error.Message,
                detail = result.Error.Detail
            }, JsonOptions);
        }

        _activeGameDirectory = targetGameDir;
        var process = result.Value;

        return JsonSerializer.Serialize(new
        {
            success = true,
            gameName = game.GameName,
            processId = process.Id.Value,
            processName = process.ProcessName,
            mainWindowTitle = process.MainWindowTitle,
            windowed = windowed,
            resolution = $"{width}x{height}",
            nextSuggestedStep = "Game is running in windowed mode."
        }, JsonOptions);
    }

    public async Task<string> StopGameAsync(
        string? gameDirectory = null,
        CancellationToken ct = default)
    {
        var targetGameDir = !string.IsNullOrWhiteSpace(gameDirectory)
            ? gameDirectory
            : _activeGameDirectory;

        if (string.IsNullOrWhiteSpace(targetGameDir))
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = "Game directory was not specified and no active game session is cached. Call inspect first or provide gameDirectory.",
                detail = "Target game directory path required."
            }, JsonOptions);
        }

        if (_stopGameUseCase == null)
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = "StopGameUseCase is not configured in this environment.",
                detail = "Missing use case dependency."
            }, JsonOptions);
        }

        var detectResult = await _gameDetector.DetectGameAsync(targetGameDir, ct).ConfigureAwait(false);
        if (detectResult.IsFailure)
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = detectResult.Error.Message,
                detail = detectResult.Error.Detail
            }, JsonOptions);
        }

        var game = detectResult.Value;
        var result = await _stopGameUseCase.ExecuteAsync(game, ct).ConfigureAwait(false);
        if (result.IsFailure)
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = result.Error.Message,
                detail = result.Error.Detail
            }, JsonOptions);
        }

        return JsonSerializer.Serialize(new
        {
            success = true,
            gameName = game.GameName,
            message = $"Game process for '{game.GameName}' stopped successfully."
        }, JsonOptions);
    }

    public async Task<string> IndexGameAssetsAsync(
        string? gameDirectory = null,
        CancellationToken ct = default)
    {
        var targetGameDir = !string.IsNullOrWhiteSpace(gameDirectory)
            ? gameDirectory
            : _activeGameDirectory;

        if (string.IsNullOrWhiteSpace(targetGameDir))
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = "Game directory was not specified and no active game session is cached. Call inspect first or provide gameDirectory.",
                detail = "Target game directory path required."
            }, JsonOptions);
        }

        if (_indexAssetsUseCase == null)
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = "IndexAssetsUseCase is not configured in this environment.",
                detail = "Missing use case dependency."
            }, JsonOptions);
        }

        var detectResult = await _gameDetector.DetectGameAsync(targetGameDir, ct).ConfigureAwait(false);
        if (detectResult.IsFailure)
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = detectResult.Error.Message,
                detail = detectResult.Error.Detail
            }, JsonOptions);
        }

        var game = detectResult.Value;
        _activeGameDirectory = targetGameDir;
        var dbPath = _storageLayout.GetDatabasePath(game.GameName);

        var indexResult = await _indexAssetsUseCase.ExecuteAsync(game, dbPath, ct).ConfigureAwait(false);
        if (indexResult.IsFailure)
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = indexResult.Error.Message,
                detail = indexResult.Error.Detail
            }, JsonOptions);
        }

        return JsonSerializer.Serialize(new
        {
            success = true,
            gameName = game.GameName,
            totalIndexed = indexResult.Value,
            databasePath = dbPath,
            message = $"Successfully indexed {indexResult.Value} game assets into SQLite index."
        }, JsonOptions);
    }

    public async Task<string> DiscoverGameAssetsAsync(
        string? searchPattern = null,
        string? assetType = null,
        int maxResults = 50,
        string? gameDirectory = null,
        string? containerName = null,
        long? pathId = null,
        int offset = 0,
        CancellationToken ct = default)
    {
        var dirRes = await ResolveActiveGameDirectoryAsync(gameDirectory, ct).ConfigureAwait(false);
        if (dirRes.IsFailure)
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = dirRes.Error.Message,
                detail = dirRes.Error.Detail
            }, JsonOptions);
        }

        var targetGameDir = dirRes.Value;

        if (_discoverAssetsUseCase == null)
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = "DiscoverAssetsUseCase is not configured in this environment.",
                detail = "Missing use case dependency."
            }, JsonOptions);
        }

        var detectResult = await _gameDetector.DetectGameAsync(targetGameDir, ct).ConfigureAwait(false);
        if (detectResult.IsFailure)
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = detectResult.Error.Message,
                detail = detectResult.Error.Detail
            }, JsonOptions);
        }

        var game = detectResult.Value;
        _activeGameDirectory = targetGameDir;
        var dbPath = _storageLayout.GetDatabasePath(game.GameName);

        int clampedMax = Math.Clamp(maxResults <= 0 ? 50 : maxResults, 1, 500);
        int clampedOffset = Math.Max(0, offset);

        var filter = new AssetDiscoveryFilter(
            SearchPattern: searchPattern,
            AssetType: assetType,
            MaxResults: clampedMax,
            ContainerName: containerName,
            PathId: pathId,
            Offset: clampedOffset);

        var result = await _discoverAssetsUseCase.ExecutePageAsync(game, filter, dbPath, ct).ConfigureAwait(false);
        if (result.IsFailure)
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = result.Error.Message,
                detail = result.Error.Detail
            }, JsonOptions);
        }

        var page = result.Value;
        var containers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var asset in page.Assets)
        {
            if (!string.IsNullOrEmpty(asset.SourceContainer) && !containers.ContainsKey(asset.SourceContainer))
            {
                containers[asset.SourceContainer] = asset.SourceContainerPath;
            }
        }

        return JsonSerializer.Serialize(new
        {
            success = true,
            gameName = game.GameName,
            totalMatches = page.TotalMatches,
            totalDiscovered = page.Assets.Count,
            returnedCount = page.Assets.Count,
            offset = clampedOffset,
            hasMore = clampedOffset + page.Assets.Count < page.TotalMatches,
            filter = new { searchPattern, assetType, maxResults = clampedMax, offset = clampedOffset, containerName, pathId },
            containers = containers,
            assets = page.Assets.Select(a => new
            {
                name = a.Name,
                assetType = a.AssetType,
                container = a.SourceContainer,
                pathId = a.PathId,
                width = a.Width,
                height = a.Height,
                byteSize = a.ByteSize,
                extension = a.Extension
            }),
            nextSuggestedStep = page.Assets.Count > 0
                ? $"Returned {page.Assets.Count} of {page.TotalMatches} matching assets (offset={clampedOffset}). Call 'extract-assets' or paginate with offset={clampedOffset + page.Assets.Count}."
                : "No matching assets found. Try a broader search pattern, check 'inspect-runtime-assets' for runtime AssetBundles, or omit the filter."
        }, JsonOptions);
    }

    public async Task<string> ExtractGameAssetsAsync(
        string? searchPattern = null,
        string? assetType = null,
        int maxResults = 50,
        string? outputDirectory = null,
        string? gameDirectory = null,
        string? containerName = null,
        long? pathId = null,
        bool disambiguateCollisions = true,
        CancellationToken ct = default)
    {
        var targetGameDir = !string.IsNullOrWhiteSpace(gameDirectory)
            ? gameDirectory
            : _activeGameDirectory;

        if (string.IsNullOrWhiteSpace(targetGameDir))
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = "Game directory was not specified and no active game session is cached. Call inspect first or provide gameDirectory.",
                detail = "Target game directory path required."
            }, JsonOptions);
        }

        if (_extractAssetsUseCase == null)
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = "ExtractAssetsUseCase is not configured in this environment.",
                detail = "Missing use case dependency."
            }, JsonOptions);
        }

        var detectResult = await _gameDetector.DetectGameAsync(targetGameDir, ct).ConfigureAwait(false);
        if (detectResult.IsFailure)
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = detectResult.Error.Message,
                detail = detectResult.Error.Detail
            }, JsonOptions);
        }

        var game = detectResult.Value;
        _activeGameDirectory = targetGameDir;

        var filter = new AssetExtractionFilter(
            SearchPattern: searchPattern,
            AssetType: assetType,
            MaxResults: maxResults,
            ContainerName: containerName,
            PathId: pathId,
            DisambiguateCollisions: disambiguateCollisions);

        var result = await _extractAssetsUseCase.ExecuteAsync(game, filter, outputDirectory, ct).ConfigureAwait(false);
        if (result.IsFailure)
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = result.Error.Message,
                detail = result.Error.Detail
            }, JsonOptions);
        }

        var outputDir = string.IsNullOrWhiteSpace(outputDirectory)
            ? _storageLayout.GetAssetsDir(game.GameName)
            : outputDirectory;

        return JsonSerializer.Serialize(new
        {
            success = true,
            gameName = game.GameName,
            totalExtracted = result.Value.Count,
            outputDirectory = outputDir,
            filter = new { searchPattern, assetType, maxResults, containerName, pathId, disambiguateCollisions },
            extractedAssets = result.Value.Select(a => new
            {
                name = a.Name,
                assetType = a.AssetType,
                outputFilePath = a.OutputFilePath,
                fileSizeBytes = a.FileSizeBytes,
                width = a.Width,
                height = a.Height,
                pathId = a.PathId,
                sourceContainer = a.SourceContainer
            }),
            nextSuggestedStep = result.Value.Count > 0
                ? $"Extracted {result.Value.Count} assets to '{outputDir}'. Modders and agents can now inspect them directly."
                : "No matching assets extracted. Check your search pattern."
        }, JsonOptions);
    }

    public async Task<string> GetGameCapabilitiesAsync(
        int? port = null,
        string? token = null,
        CancellationToken ct = default)
    {
        if (_visionBridgeClient == null)
        {
            return SerializeMissingClientError();
        }

        var conn = ResolveBridgeConnection(port, token);
        if (conn.ErrorMessage != null)
        {
            return SerializeConnectionError(conn.ErrorMessage, conn.ErrorDetail, conn.Suggestion);
        }

        var result = await _visionBridgeClient.GetCapabilitiesAsync(conn.BaseUrl!, conn.Token, ct).ConfigureAwait(false);
        if (result.IsFailure)
        {
            return SerializeConnectionError(
                "gioco non connesso",
                $"Impossibile comunicare con il VisionBridge ({result.Error.Code}): {result.Error.Message}",
                "Verifica che il gioco sia in esecuzione con il plugin Candied.VisionBridge e che risponda sulla porta specificata.");
        }

        var caps = result.Value;
        return JsonSerializer.Serialize(new
        {
            success = true,
            capabilities = new
            {
                ugui = caps.Ugui,
                eventSystem = caps.EventSystem,
                uiToolkit = caps.UiToolkit,
                legacyInput = caps.LegacyInput,
                inputSystem = caps.InputSystem,
                screenshot = caps.Screenshot,
                screenshotMethod = caps.ScreenshotMethod,
                screenshotLimitations = caps.ScreenshotLimitations,
                inputClickMethod = caps.InputClickMethod,
                inputClickStatus = caps.InputClickStatus,
                untestedMethods = caps.UntestedMethods
            },
            nextSuggestedStep = "Per interagire con il gioco, cattura un frame con 'screenshot' o consulta la scena con 'scene-info'."
        }, JsonOptions);
    }

    public async Task<string> CaptureGameScreenshotAsync(
        int? port = null,
        string? token = null,
        string? savePath = null,
        string? gameDir = null,
        CancellationToken ct = default)
    {
        if (_visionBridgeClient == null)
        {
            return SerializeMissingClientError();
        }

        var conn = ResolveBridgeConnection(port, token);
        if (conn.ErrorMessage != null)
        {
            return SerializeConnectionError(conn.ErrorMessage, conn.ErrorDetail, conn.Suggestion);
        }

        var capResult = await _visionBridgeClient.GetCapabilitiesAsync(conn.BaseUrl!, conn.Token, ct).ConfigureAwait(false);
        if (capResult.IsFailure)
        {
            return SerializeConnectionError(
                "gioco non connesso",
                $"Impossibile verificare capabilities per screenshot: {capResult.Error.Message}",
                "Assicurati che il gioco sia in esecuzione con Candied.VisionBridge attivo.");
        }

        if (!capResult.Value.Screenshot)
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = "screenshot non disponibile",
                detail = "Il plugin VisionBridge ha segnalato che il motore grafico non supporta la cattura frame in questo contesto.",
                suggestion = "Verifica le capabilities con 'capabilities'."
            }, JsonOptions);
        }

        var scResult = await _visionBridgeClient.CaptureScreenshotAsync(conn.BaseUrl!, conn.Token, ct).ConfigureAwait(false);
        if (scResult.IsFailure)
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = "screenshot fallito",
                detail = scResult.Error.Message,
                suggestion = scResult.Error.Suggestion
            }, JsonOptions);
        }

        var screenshot = scResult.Value;
        string targetPath;

        if (!string.IsNullOrWhiteSpace(savePath))
        {
            targetPath = ResolveWorkspacePath(savePath);
        }
        else
        {
            string targetDir;
            var activeDir = !string.IsNullOrWhiteSpace(gameDir) ? gameDir : _activeGameDirectory;
            if (!string.IsNullOrWhiteSpace(activeDir))
            {
                var gameName = await ResolveGameNameAsync(activeDir, ct).ConfigureAwait(false);
                targetDir = Path.Combine(_storageLayout.GetGameArtifactsDir(gameName), "screenshots");
            }
            else
            {
                targetDir = Path.Combine(_storageLayout.CandiedRoot, "screenshots");
            }

            Directory.CreateDirectory(targetDir);
            targetPath = Path.Combine(targetDir, $"screenshot_{DateTime.UtcNow:yyyyMMdd_HHmmssfff}.png");
        }

        var parentDir = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrEmpty(parentDir) && !Directory.Exists(parentDir))
        {
            Directory.CreateDirectory(parentDir);
        }

        await File.WriteAllBytesAsync(targetPath, screenshot.PngBytes, ct).ConfigureAwait(false);

        return JsonSerializer.Serialize(new
        {
            success = true,
            filePath = targetPath,
            width = screenshot.Width,
            height = screenshot.Height,
            isUniform = screenshot.IsUniform,
            method = capResult.Value.ScreenshotMethod,
            limitations = capResult.Value.ScreenshotLimitations,
            fileSizeBytes = screenshot.PngBytes.Length,
            message = "Screenshot catturato e salvato con successo.",
            nextSuggestedStep = "Ispeziona l'immagine salvata per individuare coordinate (x, y) e usa 'click'."
        }, JsonOptions);
    }

    public async Task<string> ClickGameAtAsync(
        float x,
        float y,
        int button = 0,
        int holdFrames = 6,
        string? method = null,
        int? port = null,
        string? token = null,
        CancellationToken ct = default)
    {
        if (_visionBridgeClient == null)
        {
            return SerializeMissingClientError();
        }

        var conn = ResolveBridgeConnection(port, token);
        if (conn.ErrorMessage != null)
        {
            return SerializeConnectionError(conn.ErrorMessage, conn.ErrorDetail, conn.Suggestion);
        }

        var capResult = await _visionBridgeClient.GetCapabilitiesAsync(conn.BaseUrl!, conn.Token, ct).ConfigureAwait(false);
        if (capResult.IsFailure)
        {
            return SerializeConnectionError(
                "gioco non connesso",
                $"Impossibile verificare capabilities per click: {capResult.Error.Message}",
                "Assicurati che il gioco sia in esecuzione con Candied.VisionBridge attivo.");
        }

        var caps = capResult.Value;
        if (!caps.InputSystem && !caps.LegacyInput)
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = "input non disponibile",
                detail = "Il gioco non supporta né UnityEngine.InputSystem né UnityEngine.Input legacy.",
                suggestion = "Verifica le capabilities con 'capabilities'."
            }, JsonOptions);
        }

        var clickResult = await _visionBridgeClient.SendInputClickAsync(
            conn.BaseUrl!,
            conn.Token,
            x,
            y,
            button,
            method,
            action: "click",
            holdFrames,
            ct).ConfigureAwait(false);

        if (clickResult.IsFailure)
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = "click fallito",
                detail = clickResult.Error.Message,
                suggestion = clickResult.Error.Suggestion
            }, JsonOptions);
        }

        string? markedScreenshotPath = null;
        try
        {
            if (capResult.Value.Screenshot)
            {
                var scResult = await _visionBridgeClient.CaptureScreenshotAsync(conn.BaseUrl!, conn.Token, ct).ConfigureAwait(false);
                if (scResult.IsSuccess && scResult.Value.PngBytes != null && scResult.Value.PngBytes.Length > 0)
                {
                    var markedPng = PurePngDecoder.DrawClickMarker(scResult.Value.PngBytes, (int)Math.Round(x), (int)Math.Round(y));
                    var activeDir = !string.IsNullOrWhiteSpace(_activeGameDirectory) ? _activeGameDirectory : null;
                    string targetDir;
                    if (!string.IsNullOrWhiteSpace(activeDir))
                    {
                        var gameName = await ResolveGameNameAsync(activeDir, ct).ConfigureAwait(false);
                        targetDir = Path.Combine(_storageLayout.GetGameArtifactsDir(gameName), "screenshots");
                    }
                    else
                    {
                        targetDir = Path.Combine(_storageLayout.CandiedRoot, "screenshots");
                    }

                    Directory.CreateDirectory(targetDir);
                    markedScreenshotPath = Path.Combine(targetDir, $"screenshot_click_{DateTime.UtcNow:yyyyMMdd_HHmmssfff}.png");
                    await File.WriteAllBytesAsync(markedScreenshotPath, markedPng, ct).ConfigureAwait(false);
                }
            }
        }
        catch
        {
            // Non-fatal if screenshot capture fails after click
        }

        return JsonSerializer.Serialize(new
        {
            success = true,
            hitPointX = x,
            hitPointY = y,
            button = button,
            method = caps.InputClickMethod,
            screenshotPath = markedScreenshotPath,
            message = clickResult.Value.Message,
            warning = "Il click viene inviato tramite input injection in-process; verificare sempre l'effetto con lo screenshot annotato in screenshotPath o wait-idle.",
            nextSuggestedStep = "Verifica lo screenshot con il marcatore del click in 'screenshotPath' per confermare il punto di impatto."
        }, JsonOptions);
    }

    public async Task<string> WaitGameIdleAsync(
        int maxFrames = 10,
        int timeoutMs = 3000,
        int? port = null,
        string? token = null,
        CancellationToken ct = default)
    {
        if (_visionBridgeClient == null)
        {
            return SerializeMissingClientError();
        }

        var conn = ResolveBridgeConnection(port, token);
        if (conn.ErrorMessage != null)
        {
            return SerializeConnectionError(conn.ErrorMessage, conn.ErrorDetail, conn.Suggestion);
        }

        var result = await _visionBridgeClient.WaitForIdleAsync(conn.BaseUrl!, conn.Token, maxFrames, timeoutMs, ct).ConfigureAwait(false);
        if (result.IsFailure)
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = "wait_idle fallito",
                detail = result.Error.Message,
                suggestion = result.Error.Suggestion
            }, JsonOptions);
        }

        return JsonSerializer.Serialize(new
        {
            success = true,
            idleReached = result.Value.IdleReached,
            elapsedFrames = result.Value.ElapsedFrames,
            message = "Attesa di stabilizzazione rendering completata.",
            nextSuggestedStep = "Cattura un nuovo frame con 'screenshot' per validare l'effetto dell'azione."
        }, JsonOptions);
    }

    public async Task<string> GetGameSceneInfoAsync(
        int? port = null,
        string? token = null,
        CancellationToken ct = default)
    {
        if (_visionBridgeClient == null)
        {
            return SerializeMissingClientError();
        }

        var conn = ResolveBridgeConnection(port, token);
        if (conn.ErrorMessage != null)
        {
            return SerializeConnectionError(conn.ErrorMessage, conn.ErrorDetail, conn.Suggestion);
        }

        var sceneResult = await _visionBridgeClient.GetSceneInfoAsync(conn.BaseUrl!, conn.Token, ct).ConfigureAwait(false);
        if (sceneResult.IsSuccess)
        {
            var s = sceneResult.Value;
            return JsonSerializer.Serialize(new
            {
                success = true,
                activeSceneName = s.ActiveSceneName,
                activeSceneIndex = s.ActiveSceneIndex,
                rootCount = s.RootCount,
                isLoaded = s.IsLoaded,
                loadedSceneCount = s.LoadedSceneCount,
                screenWidth = s.ScreenWidth,
                screenHeight = s.ScreenHeight,
                activeRendererCount = s.ActiveRendererCount,
                timeSinceLevelLoad = s.TimeSinceLevelLoad,
                nextSuggestedStep = "Usa 'screenshot' per osservare visivamente la scena o 'list-ui' per enumerarne i controlli."
            }, JsonOptions);
        }

        // Fallback to /hello if /scene is not supported
        var helloResult = await _visionBridgeClient.GetHelloAsync(conn.BaseUrl!, conn.Token, ct).ConfigureAwait(false);
        if (helloResult.IsSuccess)
        {
            var h = helloResult.Value;
            return JsonSerializer.Serialize(new
            {
                success = true,
                activeSceneName = "Unknown",
                activeSceneIndex = -1,
                rootCount = 0,
                isLoaded = true,
                loadedSceneCount = 1,
                screenWidth = h.ScreenWidth,
                screenHeight = h.ScreenHeight,
                note = "Scena non enumerabile direttamente; dimensioni e versione Unity ricavate da /hello.",
                unityVersion = h.UnityVersion,
                nextSuggestedStep = "Usa 'screenshot' per catturare la schermata attuale."
            }, JsonOptions);
        }

        return SerializeConnectionError(
            "gioco non connesso",
            $"Impossibile interrogare la scena: {sceneResult.Error.Message}",
            "Assicurati che il gioco sia in esecuzione con Candied.VisionBridge attivo.");
    }

    public async Task<string> InspectSceneObjectsAsync(
        string? nameFilter = null,
        int? port = null,
        string? token = null,
        bool includeInactive = false,
        bool visibleOnly = false,
        string? sortingLayer = null,
        int maxResults = 100,
        CancellationToken ct = default)
    {
        if (_visionBridgeClient == null)
        {
            return SerializeMissingClientError();
        }

        var conn = ResolveBridgeConnection(port, token);
        if (conn.ErrorMessage != null)
        {
            return SerializeConnectionError(conn.ErrorMessage, conn.ErrorDetail, conn.Suggestion);
        }

        var query = new SceneObjectsQuery(
            NameFilter: nameFilter,
            IncludeInactive: includeInactive,
            VisibleOnly: visibleOnly,
            SortingLayer: sortingLayer,
            MaxResults: Math.Clamp(maxResults <= 0 ? 100 : maxResults, 1, 1000));

        var result = await _visionBridgeClient.InspectSceneObjectsAsync(conn.BaseUrl!, conn.Token, query, ct).ConfigureAwait(false);
        if (result.IsFailure)
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = "ispezione scena fallita",
                detail = result.Error.Message,
                suggestion = result.Error.Suggestion
            }, JsonOptions);
        }

        var resp = result.Value;
        return JsonSerializer.Serialize(new
        {
            success = resp.Success,
            totalCameras = resp.TotalCameras,
            totalRenderers = resp.TotalRenderers,
            totalMatched = resp.TotalMatched > 0 ? resp.TotalMatched : resp.TotalRenderers,
            truncated = resp.Truncated,
            filter = new { nameFilter, includeInactive, visibleOnly, sortingLayer, maxResults = query.MaxResults },
            cameras = resp.Cameras,
            renderers = resp.Renderers,
            message = resp.Message,
            nextSuggestedStep = resp.Truncated
                ? $"Output troncato a {resp.TotalRenderers} di {resp.TotalMatched} renderer corrispondenti. Restringi con 'nameFilter', 'visibleOnly=true', o 'sortingLayer'."
                : "Analizza fullPath, localPosition, sortingLayer, spriteName, spritePivot, components e isVisible per verificare lo stato di rendering."
        }, JsonOptions);
    }

    public async Task<string> InspectRuntimeAssetsAsync(
        string? nameFilter = null,
        string? assetType = null,
        int maxResults = 50,
        int? port = null,
        string? token = null,
        CancellationToken ct = default)
    {
        if (_visionBridgeClient == null)
        {
            return SerializeMissingClientError();
        }

        var conn = ResolveBridgeConnection(port, token);
        if (conn.ErrorMessage != null)
        {
            return SerializeConnectionError(conn.ErrorMessage, conn.ErrorDetail, conn.Suggestion);
        }

        var query = new RuntimeAssetsQuery(
            NameFilter: nameFilter,
            AssetType: string.IsNullOrWhiteSpace(assetType) ? "Sprite" : assetType.Trim(),
            MaxResults: Math.Clamp(maxResults <= 0 ? 50 : maxResults, 1, 500));

        var result = await _visionBridgeClient.InspectRuntimeAssetsAsync(conn.BaseUrl!, conn.Token, query, ct).ConfigureAwait(false);
        if (result.IsFailure)
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = "ispezione asset runtime fallita",
                detail = result.Error.Message,
                suggestion = result.Error.Suggestion
            }, JsonOptions);
        }

        var resp = result.Value;
        return JsonSerializer.Serialize(new
        {
            success = resp.Success,
            totalBundles = resp.TotalBundles,
            totalLoadedAssets = resp.TotalLoadedAssets,
            returnedAssets = resp.LoadedAssets.Count,
            truncated = resp.Truncated,
            filter = new { nameFilter, assetType = query.AssetType, maxResults = query.MaxResults },
            bundles = resp.Bundles,
            loadedAssets = resp.LoadedAssets,
            message = resp.Message
        }, JsonOptions);
    }

    public async Task<string> ListGameUiElementsAsync(
        int? port = null,
        string? token = null,
        CancellationToken ct = default)
    {
        if (_visionBridgeClient == null)
        {
            return SerializeMissingClientError();
        }

        var conn = ResolveBridgeConnection(port, token);
        if (conn.ErrorMessage != null)
        {
            return SerializeConnectionError(conn.ErrorMessage, conn.ErrorDetail, conn.Suggestion);
        }

        var capResult = await _visionBridgeClient.GetCapabilitiesAsync(conn.BaseUrl!, conn.Token, ct).ConfigureAwait(false);
        if (capResult.IsFailure)
        {
            return SerializeConnectionError(
                "gioco non connesso",
                $"Impossibile verificare capabilities per UI: {capResult.Error.Message}",
                "Assicurati che il gioco sia in esecuzione con Candied.VisionBridge attivo.");
        }

        var caps = capResult.Value;
        bool isEnumerable = caps.Ugui || caps.UiToolkit;

        if (!isEnumerable)
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = "list_ui non disponibile",
                detail = "Il gioco non espone una UI uGUI (Selectable) o UI Toolkit enumerabile.",
                suggestion = "list_ui non disponibile: usa screenshot + click"
            }, JsonOptions);
        }

        var elementsResult = await _visionBridgeClient.GetUiElementsAsync(conn.BaseUrl!, conn.Token, ct).ConfigureAwait(false);
        if (elementsResult.IsFailure)
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = "recupero elementi UI fallito",
                detail = elementsResult.Error.Message,
                suggestion = "list_ui non disponibile: usa screenshot + click"
            }, JsonOptions);
        }

        var elements = elementsResult.Value;
        if (elements.Count == 0)
        {
            return JsonSerializer.Serialize(new
            {
                success = true,
                count = 0,
                elements = Array.Empty<object>(),
                suggestion = "Nessun elemento uGUI trovato nella scena attiva. Se il gioco usa bottoni personalizzati (es. Collider2D), usa screenshot + click per interagire tramite coordinate."
            }, JsonOptions);
        }

        return JsonSerializer.Serialize(new
        {
            success = true,
            count = elements.Count,
            elements = elements.Select(e => new
            {
                id = e.Id,
                type = e.Type,
                text = e.Text,
                interactable = e.Interactable,
                visible = e.Visible,
                pixelRect = new { x = e.PixelRect.X, y = e.PixelRect.Y, width = e.PixelRect.Width, height = e.PixelRect.Height },
                normalizedRect = new { x = e.NormalizedRect.X, y = e.NormalizedRect.Y, width = e.NormalizedRect.Width, height = e.NormalizedRect.Height }
            }),
            nextSuggestedStep = "Usa 'click' passando le coordinate centrali di uno degli elementi scoperti."
        }, JsonOptions);
    }

    public async Task<string> ListRegisteredGamesAsync(CancellationToken ct = default)
    {
        if (_gameRegistry == null)
        {
            return JsonSerializer.Serialize(new { success = false, error = "Game registry is not configured." }, JsonOptions);
        }

        var games = await _gameRegistry.ListRegisteredGamesAsync(ct).ConfigureAwait(false);
        return JsonSerializer.Serialize(new
        {
            success = true,
            count = games.Count,
            games = games.Select(g => new
            {
                name = g.Name,
                executablePath = g.ExecutablePath,
                gameRootPath = g.GameRootPath,
                architecture = g.Architecture,
                unityVersion = g.UnityVersion,
                hasBridgeInstalled = g.HasBridgeInstalled,
                registeredAtUtc = g.RegisteredAtUtc.ToString("o")
            })
        }, JsonOptions);
    }

    public async Task<string> RegisterGameAsync(string name, string executablePath, CancellationToken ct = default)
    {
        if (_gameRegistry == null)
        {
            return JsonSerializer.Serialize(new { success = false, error = "Game registry is not configured." }, JsonOptions);
        }

        var result = await _gameRegistry.RegisterGameAsync(name, executablePath, ct).ConfigureAwait(false);
        if (result.IsFailure)
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = result.Error
            }, JsonOptions);
        }

        var g = result.Value;
        return JsonSerializer.Serialize(new
        {
            success = true,
            message = $"Gioco '{g.Name}' registrato con successo.",
            game = new
            {
                name = g.Name,
                executablePath = g.ExecutablePath,
                gameRootPath = g.GameRootPath,
                architecture = g.Architecture,
                unityVersion = g.UnityVersion,
                hasBridgeInstalled = g.HasBridgeInstalled
            }
        }, JsonOptions);
    }

    public async Task<string> UnregisterGameAsync(string name, CancellationToken ct = default)
    {
        if (_gameRegistry == null)
        {
            return JsonSerializer.Serialize(new { success = false, error = "Game registry is not configured." }, JsonOptions);
        }

        var result = await _gameRegistry.UnregisterGameAsync(name, ct).ConfigureAwait(false);
        if (result.IsFailure)
        {
            return JsonSerializer.Serialize(new { success = false, error = result.Error }, JsonOptions);
        }

        return JsonSerializer.Serialize(new { success = true, message = $"Gioco '{name}' rimosso dall'allowlist." }, JsonOptions);
    }

    public async Task<string> InstallBridgeAsync(string name, CancellationToken ct = default)
    {
        if (_gameRegistry == null)
        {
            return JsonSerializer.Serialize(new { success = false, error = "Game registry is not configured." }, JsonOptions);
        }

        var gameRes = await _gameRegistry.GetRegisteredGameAsync(name, ct).ConfigureAwait(false);
        if (gameRes.IsFailure)
        {
            return JsonSerializer.Serialize(new { success = false, error = gameRes.Error }, JsonOptions);
        }

        var game = gameRes.Value;
        var targetDir = Path.Combine(game.GameRootPath, "BepInEx", "plugins", "Candied.VisionBridge");
        Directory.CreateDirectory(targetDir);

        string? bridgeDll = FindBridgeBinary("Candied.VisionBridge.dll");
        string? protocolDll = FindBridgeBinary("Candied.VisionBridge.Protocol.dll");

        if (string.IsNullOrEmpty(bridgeDll) || !File.Exists(bridgeDll))
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = "Impossibile trovare il binario Candied.VisionBridge.dll compilato. Esegui `dotnet build` prima di installare il bridge."
            }, JsonOptions);
        }

        File.Copy(bridgeDll, Path.Combine(targetDir, "Candied.VisionBridge.dll"), overwrite: true);
        if (!string.IsNullOrEmpty(protocolDll) && File.Exists(protocolDll))
        {
            File.Copy(protocolDll, Path.Combine(targetDir, "Candied.VisionBridge.Protocol.dll"), overwrite: true);
        }

        await _gameRegistry.UpdateBridgeStatusAsync(name, true, ct).ConfigureAwait(false);

        return JsonSerializer.Serialize(new
        {
            success = true,
            message = $"VisionBridge installato con successo per '{name}'.",
            installedPath = targetDir
        }, JsonOptions);
    }

    public Task<string> LaunchGameByNameAsync(string name, CancellationToken ct) =>
        LaunchGameByNameAsync(name, waitForLog: null, ct: ct);

    public async Task<string> LaunchGameByNameAsync(
        string name,
        string? waitForLog = null,
        CancellationToken ct = default)
    {
        if (_gameRegistry == null)
        {
            return JsonSerializer.Serialize(new { success = false, error = "Game registry is not configured." }, JsonOptions);
        }
        if (_studioClient == null)
        {
            return JsonSerializer.Serialize(new { success = false, error = "Studio client is not configured." }, JsonOptions);
        }
        if (_visionBridgeClient == null)
        {
            return JsonSerializer.Serialize(new { success = false, error = "VisionBridge client is not configured." }, JsonOptions);
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = "Il nome del gioco non può essere vuoto.",
                suggestion = "Specifica il nome di un gioco registrato o consulta la lista con 'list-games'."
            }, JsonOptions);
        }

        // 1. Validate allowlist
        var gameRes = await _gameRegistry.GetRegisteredGameAsync(name, ct).ConfigureAwait(false);
        if (gameRes.IsFailure)
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = gameRes.Error,
                suggestion = $"Il gioco '{name}' non è registrato nell'allowlist. Per registrarlo eseguire: dotnet run --project src/Candied.Agent -- register-game <nome> <percorso_exe>"
            }, JsonOptions);
        }

        var game = gameRes.Value;

        // 2. Validate bridge installation (do not install automatically)
        if (!game.HasBridgeInstalled)
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = $"Il bridge VisionBridge non è installato nel gioco '{name}'.",
                suggestion = $"esegui: dotnet run --project src/Candied.Agent -- install-bridge {name}"
            }, JsonOptions);
        }

        // 3. Ensure Studio is running (detached independent process)
        var ensureRes = await _studioClient.EnsureStudioRunningAsync(ct).ConfigureAwait(false);
        if (ensureRes.IsFailure)
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = $"Avvio di Candied Studio fallito: {ensureRes.Error}"
            }, JsonOptions);
        }

        // 4. Check if game is already running (idempotence)
        var statusRes = await _studioClient.GetStatusAsync(ct).ConfigureAwait(false);
        if (statusRes.IsSuccess && statusRes.Value.IsRunning &&
            string.Equals(statusRes.Value.ActiveGameName, name, StringComparison.OrdinalIgnoreCase))
        {
            // Already running the requested game
        }
        else
        {
            var launchRes = await _studioClient.LaunchGameAsync(name, game.ExecutablePath, ct).ConfigureAwait(false);
            if (launchRes.IsFailure)
            {
                return JsonSerializer.Serialize(new
                {
                    success = false,
                    error = $"Avvio del gioco fallito da Studio: {launchRes.Error}"
                }, JsonOptions);
            }
        }

        // 5. Wait loop (up to 60s) for /hello + 2 consecutive stable windows:
        //    diff < 0.25%, unchanged activeSceneName / loadedSceneCount / activeRendererCount,
        //    and optional waitForLog marker in BepInEx/LogOutput.log.
        const double stabilityDiffThreshold = 0.25;
        const int requiredStableWindows = 2;
        const int sampleIntervalMs = 1000;

        var bepInExLogPath = Path.Combine(game.GameRootPath, "BepInEx", "LogOutput.log");
        var sw = Stopwatch.StartNew();
        string lastObservedStatus = "In attesa di connessione HTTP a VisionBridge...";
        int consecutiveStableWindows = 0;

        while (sw.ElapsedMilliseconds < 60000 && !ct.IsCancellationRequested)
        {
            var session = await _studioClient.GetCurrentSessionAsync(ct).ConfigureAwait(false);
            if (session?.BridgePort == null || string.IsNullOrEmpty(session.BridgeToken))
            {
                consecutiveStableWindows = 0;
                lastObservedStatus = "Studio non ha ancora pubblicato porta/token per VisionBridge.";
                await Task.Delay(1000, ct).ConfigureAwait(false);
                continue;
            }

            string baseUrl = $"http://127.0.0.1:{session.BridgePort.Value}";
            string token = session.BridgeToken;

            var helloRes = await _visionBridgeClient.GetHelloAsync(baseUrl, token, ct).ConfigureAwait(false);
            if (helloRes.IsFailure)
            {
                consecutiveStableWindows = 0;
                lastObservedStatus = $"Endpoint /hello non ancora pronto: {helloRes.Error.Message}";
                await Task.Delay(1000, ct).ConfigureAwait(false);
                continue;
            }

            lastObservedStatus = "VisionBridge connesso (/hello OK). Verifica stabilità scena e frame...";

            var scene1Res = await _visionBridgeClient.GetSceneInfoAsync(baseUrl, token, ct).ConfigureAwait(false);
            var sc1 = await _visionBridgeClient.CaptureScreenshotAsync(baseUrl, token, ct).ConfigureAwait(false);
            if (sc1.IsFailure || sc1.Value.IsUniform)
            {
                consecutiveStableWindows = 0;
                lastObservedStatus = sc1.IsFailure
                    ? "Screenshot 1 fallito."
                    : "Screenshot 1 non pronto (immagine uniforme / schermata di caricamento).";
                await Task.Delay(1000, ct).ConfigureAwait(false);
                continue;
            }

            await Task.Delay(sampleIntervalMs, ct).ConfigureAwait(false);

            var scene2Res = await _visionBridgeClient.GetSceneInfoAsync(baseUrl, token, ct).ConfigureAwait(false);
            var sc2 = await _visionBridgeClient.CaptureScreenshotAsync(baseUrl, token, ct).ConfigureAwait(false);
            if (sc2.IsFailure || sc2.Value.IsUniform)
            {
                consecutiveStableWindows = 0;
                lastObservedStatus = sc2.IsFailure
                    ? "Screenshot 2 fallito."
                    : "Screenshot 2 non pronto (immagine uniforme / schermata di caricamento).";
                continue;
            }

            bool sceneStable = true;
            string sceneSummary = "n/d";
            int latestRendererCount = 0;
            string latestSceneName = "Unknown";
            if (scene1Res.IsSuccess && scene2Res.IsSuccess)
            {
                var s1 = scene1Res.Value;
                var s2 = scene2Res.Value;
                latestSceneName = s2.ActiveSceneName;
                latestRendererCount = s2.ActiveRendererCount;
                sceneSummary = $"scena: '{s2.ActiveSceneName}', loadedScenes: {s2.LoadedSceneCount}, renderers: {s2.ActiveRendererCount}";

                bool sameScene = string.Equals(s1.ActiveSceneName, s2.ActiveSceneName, StringComparison.Ordinal);
                bool sameLoadedCount = s1.LoadedSceneCount == s2.LoadedSceneCount;
                bool rendererCountStable = Math.Abs(s2.ActiveRendererCount - s1.ActiveRendererCount) <= 2;

                if (!sameScene || !sameLoadedCount || !rendererCountStable)
                {
                    sceneStable = false;
                    lastObservedStatus = $"Transizione di scena in corso ({s1.ActiveSceneName}/{s1.ActiveRendererCount} -> {s2.ActiveSceneName}/{s2.ActiveRendererCount}).";
                }
            }

            bool logMarkerFound = true;
            if (!string.IsNullOrWhiteSpace(waitForLog))
            {
                logMarkerFound = ContainsLogMarker(bepInExLogPath, waitForLog.Trim());
                if (!logMarkerFound)
                {
                    lastObservedStatus = $"In attesa del marker '{waitForLog}' in BepInEx/LogOutput.log...";
                }
            }

            double diff = PurePngDecoder.CalculateDiffPercentage(sc1.Value.PngBytes, sc2.Value.PngBytes);
            if (diff < stabilityDiffThreshold && sceneStable && logMarkerFound)
            {
                consecutiveStableWindows++;
                if (consecutiveStableWindows < requiredStableWindows)
                {
                    lastObservedStatus = $"Finestra stabile {consecutiveStableWindows}/{requiredStableWindows} (diff: {diff:F2}% < {stabilityDiffThreshold:F2}%, {sceneSummary}). Attesa conferma...";
                    continue;
                }

                // Verify window visibility & clamping right before declaring success
                var finalStatus = await _studioClient.GetStatusAsync(ct).ConfigureAwait(false);
                var win = finalStatus.IsSuccess ? finalStatus.Value.Window : null;

                if (win == null || win.Handle == 0 || !win.IsVisible || !win.IsWithinMonitor)
                {
                    string logPath = GetStudioLogPath();
                    return JsonSerializer.Serialize(new
                    {
                        success = false,
                        error = "Candied Studio è in esecuzione ma la finestra principale non è valida, non è visibile o è fuori dai limiti del monitor.",
                        detail = $"MainWindowHandle: {(win?.Handle ?? 0)}, IsWindowVisible: {(win?.IsVisible ?? false)}, " +
                                 $"WindowState: '{win?.WindowState ?? "Unknown"}', Visibility: '{win?.Visibility ?? "Unknown"}', " +
                                 $"Rect: [Left={(win?.RectLeft ?? 0)}, Top={(win?.RectTop ?? 0)}, Right={(win?.RectRight ?? 0)}, Bottom={(win?.RectBottom ?? 0)}] " +
                                 $"(Width={(win?.Width ?? 0)}, Height={(win?.Height ?? 0)}), IsWithinMonitor: {(win?.IsWithinMonitor ?? false)}, Responding: {(win?.IsResponding ?? false)}.",
                        logPath = logPath,
                        suggestion = $"Controlla i log di Studio in '{logPath}' per verificare eventuali eccezioni o problemi di visualizzazione."
                    }, JsonOptions);
                }

                // Menu is stable and window is verified visible!
                int gameW = sc2.Value.Width > 0 ? sc2.Value.Width : (helloRes.Value?.ScreenWidth ?? 1024);
                int gameH = sc2.Value.Height > 0 ? sc2.Value.Height : (helloRes.Value?.ScreenHeight ?? 720);
                int panelW = finalStatus.IsSuccess ? finalStatus.Value.PanelWidth : 0;
                int panelH = finalStatus.IsSuccess ? finalStatus.Value.PanelHeight : 0;

                return JsonSerializer.Serialize(new
                {
                    success = true,
                    status = "Running",
                    gameName = name,
                    activeSceneName = latestSceneName,
                    activeRendererCount = latestRendererCount,
                    gameResolution = new
                    {
                        width = gameW,
                        height = gameH
                    },
                    panelResolution = new
                    {
                        width = panelW,
                        height = panelH
                    },
                    resolution = new
                    {
                        width = gameW,
                        height = gameH
                    },
                    screenshotDimensions = new
                    {
                        width = sc2.Value.Width,
                        height = sc2.Value.Height
                    },
                    window = new
                    {
                        mainWindowHandle = win.Handle,
                        isWindowVisible = win.IsVisible,
                        windowState = win.WindowState,
                        visibility = win.Visibility,
                        rect = new
                        {
                            left = win.RectLeft,
                            top = win.RectTop,
                            right = win.RectRight,
                            bottom = win.RectBottom,
                            width = win.Width,
                            height = win.Height
                        },
                        isWithinMonitor = win.IsWithinMonitor,
                        isResponding = win.IsResponding
                    },
                    waitOutcome = $"Menu stabile ({requiredStableWindows} finestre consecutive, diff: {diff:F2}% < {stabilityDiffThreshold:F2}%, {sceneSummary})",
                    bridgePort = session.BridgePort.Value,
                    elapsedSeconds = sw.Elapsed.TotalSeconds,
                    nextSuggestedStep = "Usa 'screenshot' per esaminare lo stato visivo o 'click' per interagire con i pulsanti."
                }, JsonOptions);
            }

            consecutiveStableWindows = 0;
            if (sceneStable && logMarkerFound)
            {
                lastObservedStatus = $"Menu non ancora stabile (diff: {diff:F2}% >= {stabilityDiffThreshold:F2}%, {sceneSummary}).";
            }
            await Task.Delay(800, ct).ConfigureAwait(false);
        }

        // Timeout expired
        return JsonSerializer.Serialize(new
        {
            success = false,
            error = "Timeout (60s) scaduto nell'attesa di un menu principale stabile.",
            lastObservedStatus = lastObservedStatus,
            bepInExLogPath = bepInExLogPath,
            suggestion = "Verifica il file di log BepInEx indicato per analizzare errori di runtime del gioco."
        }, JsonOptions);
    }

    private static bool ContainsLogMarker(string logFilePath, string marker)
    {
        try
        {
            if (!File.Exists(logFilePath)) return false;
            using var stream = new FileStream(logFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            string content = reader.ReadToEnd();
            return content.Contains(marker, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public async Task<string> CloseGameAsync(CancellationToken ct = default)
    {
        if (_studioClient == null)
        {
            return JsonSerializer.Serialize(new { success = false, error = "Studio client is not configured." }, JsonOptions);
        }

        var closeRes = await _studioClient.CloseGameAsync(ct).ConfigureAwait(false);
        if (closeRes.IsFailure)
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = closeRes.Error
            }, JsonOptions);
        }

        return JsonSerializer.Serialize(new
        {
            success = true,
            status = "Closed",
            message = "Gioco e sessione chiusi con successo."
        }, JsonOptions);
    }

    private string? FindBridgeBinary(string fileName)
    {
        string[] baseRoots = [_storageLayout.WorkspaceRoot, ResolveDefaultWorkspaceRoot(), Directory.GetCurrentDirectory()];
        string[] relativeSubPaths = [
            Path.Combine("src", "Candied.VisionBridge", "bin", "Release", "net472"),
            Path.Combine("src", "Candied.VisionBridge", "bin", "Debug", "net472"),
            Path.Combine("src", "Candied.VisionBridge", "bin", "Release", "net35"),
            Path.Combine("src", "Candied.VisionBridge", "bin", "Debug", "net35"),
            Path.Combine("src", "Candied.VisionBridge.Protocol", "bin", "Release", "net472"),
            Path.Combine("src", "Candied.VisionBridge.Protocol", "bin", "Debug", "net472"),
            Path.Combine("src", "Candied.VisionBridge.Protocol", "bin", "Release", "net35"),
            Path.Combine("src", "Candied.VisionBridge.Protocol", "bin", "Debug", "net35"),
            Path.Combine("src", "Candied.VisionBridge.Protocol", "bin", "Release", "netstandard2.0"),
            Path.Combine("src", "Candied.VisionBridge.Protocol", "bin", "Debug", "netstandard2.0")
        ];

        foreach (var root in baseRoots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(root)) continue;
            foreach (var sub in relativeSubPaths)
            {
                var candidate = Path.Combine(root, sub, fileName);
                if (File.Exists(candidate)) return candidate;
            }
        }

        return null;
    }

    public static string ResolveDefaultWorkspaceRoot(string? currentDirectory = null, string? baseDirectory = null)
    {
        var envRoot = Environment.GetEnvironmentVariable("CANDIED_WORKSPACE_ROOT");
        if (!string.IsNullOrWhiteSpace(envRoot) && Directory.Exists(envRoot))
        {
            return Path.GetFullPath(envRoot);
        }

        var cwd = !string.IsNullOrWhiteSpace(currentDirectory) ? currentDirectory : Directory.GetCurrentDirectory();
        var appBase = !string.IsNullOrWhiteSpace(baseDirectory) ? baseDirectory : AppContext.BaseDirectory;

        var repoFromCwd = FindAncestorWithRepoMarker(cwd);
        if (repoFromCwd != null)
        {
            return repoFromCwd;
        }

        var repoFromBase = FindAncestorWithRepoMarker(appBase);
        if (repoFromBase != null)
        {
            return repoFromBase;
        }

        return Path.GetFullPath(cwd);
    }

    private static string? FindAncestorWithRepoMarker(string? startPath)
    {
        if (string.IsNullOrWhiteSpace(startPath))
        {
            return null;
        }

        try
        {
            var dir = new DirectoryInfo(startPath);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "Candied.sln")) ||
                    Directory.Exists(Path.Combine(dir.FullName, ".git")))
                {
                    return dir.FullName;
                }

                dir = dir.Parent;
            }
        }
        catch
        {
            // Ignore traversal errors
        }

        return null;
    }

    private string ResolveWorkspacePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return _storageLayout.WorkspaceRoot;
        }

        var trimmed = path.Trim();
        if (Path.IsPathRooted(trimmed))
        {
            return Path.GetFullPath(trimmed);
        }

        return Path.GetFullPath(Path.Combine(_storageLayout.WorkspaceRoot, trimmed));
    }

    private (string? BaseUrl, string Token, string? ErrorMessage, string? ErrorDetail, string? Suggestion) ResolveBridgeConnection(int? customPort = null, string? customToken = null)
    {
        int port = customPort
            ?? (int.TryParse(Environment.GetEnvironmentVariable("CANDIED_PORT"), out var p) ? p : 0);
        string token = !string.IsNullOrWhiteSpace(customToken)
            ? customToken
            : (Environment.GetEnvironmentVariable("CANDIED_TOKEN") ?? string.Empty);

        if (port <= 0 && _studioClient != null)
        {
            try
            {
                var session = _studioClient.GetCurrentSessionAsync().GetAwaiter().GetResult();
                if (session?.BridgePort != null && !string.IsNullOrWhiteSpace(session.BridgeToken))
                {
                    port = session.BridgePort.Value;
                    token = session.BridgeToken;
                }
            }
            catch { }
        }

        if (port <= 0)
        {
            return (null, token, "gioco non connesso", "Nessuna porta specificata, CANDIED_PORT non impostata e nessuna sessione Studio attiva.", "Avvia il gioco con 'game-launch' o specifica i parametri port e token.");
        }

        if (string.IsNullOrWhiteSpace(token))
        {
            return (null, token, "gioco non connesso", "Token di sicurezza mancante (CANDIED_TOKEN non impostato).", "Verifica che il gioco sia stato avviato tramite Studio o fornisci il parametro token.");
        }

        return ($"http://127.0.0.1:{port}", token, null, null, null);
    }

    private static string SerializeMissingClientError()
    {
        return JsonSerializer.Serialize(new
        {
            success = false,
            error = "IVisionBridgeClient is not configured in this environment.",
            detail = "Missing client dependency."
        }, JsonOptions);
    }

    private static string SerializeConnectionError(string error, string? detail, string? suggestion)
    {
        return JsonSerializer.Serialize(new
        {
            success = false,
            error = error,
            detail = detail ?? string.Empty,
            suggestion = suggestion ?? string.Empty
        }, JsonOptions);
    }

    private static string GetStudioLogPath()
    {
        string? overrideDir = Environment.GetEnvironmentVariable("CANDIED_USER_CONFIG_DIR");
        string baseDir;
        if (!string.IsNullOrWhiteSpace(overrideDir))
        {
            baseDir = overrideDir;
        }
        else
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (string.IsNullOrWhiteSpace(appData))
            {
                appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
            }
            baseDir = Path.Combine(appData, "Candied");
        }
        return Path.Combine(baseDir, "logs", "studio.log");
    }

    private async Task<string> ResolveGameNameAsync(string gameDirectory, CancellationToken ct)
    {
        var detectResult = await _gameDetector.DetectGameAsync(gameDirectory, ct).ConfigureAwait(false);
        return detectResult.IsSuccess
            ? detectResult.Value.GameName
            : Path.GetFileName(gameDirectory.TrimEnd('/', '\\'));
    }
}
