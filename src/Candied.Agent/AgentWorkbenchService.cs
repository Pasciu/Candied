using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Candied.Core.Analysis;
using Candied.Core.Capabilities;
using Candied.Core.Errors;
using Candied.Core.Models;
using Candied.Core.Ports;
using Candied.Core.UseCases;
using Candied.Primitives;
using Candied.Vision.Analysis;
using Candied.Vision.Controls;
using Candied.Vision.Hosting;
using Candied.Vision.Models;
using Candied.Vision.Ports;
using Candied.Vision.Protocol;

namespace Candied.Agent;

public sealed class AgentWorkbenchService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        WriteIndented = false
    };

    private static readonly string[] BaseWindowCapabilityList =
    [
        VisionConstants.CapScreenshot,
        VisionConstants.CapPointer,
        VisionConstants.CapKeyboard,
        VisionConstants.CapGamepad
    ];

    private static readonly string[] BuiltInFallbackActionList =
    [
        "Confirm",
        "Cancel",
        "Pause",
        "MoveUp",
        "MoveDown",
        "MoveLeft",
        "MoveRight"
    ];

    private readonly InspectTargetUseCase? _inspectTargetUseCase;
    private readonly SetupTargetEnvironmentUseCase? _setupTargetUseCase;
    private readonly RollbackTargetEnvironmentUseCase? _rollbackTargetUseCase;
    private readonly ExtractCodeModuleUseCase? _extractCodeUseCase;
    private readonly SearchSymbolsUseCase? _searchSymbolsUseCase;
    private readonly SearchCodeUseCase? _searchCodeUseCase;
    private readonly GetTypeDetailUseCase? _getTypeDetailUseCase;
    private readonly SaveResearchNotesUseCase? _saveResearchNotesUseCase;
    private readonly IndexAssetsUseCase? _indexAssetsUseCase;
    private readonly DiscoverAssetsUseCase? _discoverAssetsUseCase;
    private readonly ExtractAssetsUseCase? _extractAssetsUseCase;
    private readonly ScaffoldModUseCase? _scaffoldModUseCase;
    private readonly BuildModUseCase? _buildModUseCase;
    private readonly ReadRuntimeLogsUseCase? _readLogsUseCase;
    private readonly ManageTargetProcessUseCase? _manageProcessUseCase;
    private readonly ITargetPlatformRegistry? _platformRegistry;
    private readonly CandiedStorageLayout _storageLayout;
    private readonly IStudioHostClient? _studioClient;
    private readonly IVisionSensorClient? _visionSensorClient;
    private readonly IGameControlProfileStore? _controlProfileStore;
    private readonly IRecentTargetsStore? _recentTargetsStore;
    private readonly IReadOnlyList<IPlatformToolProvider> _platformToolProviders;
    private readonly IReadOnlyList<IPlatformGuideProvider> _platformGuideProviders;

    private string? _activeTargetPath;
    private string? _activeRunnerPath;
    private TargetDescriptor? _activeTarget;
    private string? _activeDatabasePath;

    public AgentWorkbenchService(
        InspectTargetUseCase? inspectTargetUseCase = null,
        SetupTargetEnvironmentUseCase? setupTargetUseCase = null,
        RollbackTargetEnvironmentUseCase? rollbackTargetUseCase = null,
        ExtractCodeModuleUseCase? extractCodeUseCase = null,
        SearchSymbolsUseCase? searchSymbolsUseCase = null,
        SearchCodeUseCase? searchCodeUseCase = null,
        GetTypeDetailUseCase? getTypeDetailUseCase = null,
        SaveResearchNotesUseCase? saveResearchNotesUseCase = null,
        IndexAssetsUseCase? indexAssetsUseCase = null,
        DiscoverAssetsUseCase? discoverAssetsUseCase = null,
        ExtractAssetsUseCase? extractAssetsUseCase = null,
        ScaffoldModUseCase? scaffoldModUseCase = null,
        BuildModUseCase? buildModUseCase = null,
        ReadRuntimeLogsUseCase? readLogsUseCase = null,
        ManageTargetProcessUseCase? manageProcessUseCase = null,
        ITargetPlatformRegistry? platformRegistry = null,
        CandiedStorageLayout? storageLayout = null,
        IStudioHostClient? studioClient = null,
        IVisionSensorClient? visionSensorClient = null,
        IGameControlProfileStore? controlProfileStore = null,
        IRecentTargetsStore? recentTargetsStore = null,
        IEnumerable<IPlatformToolProvider>? platformToolProviders = null,
        IEnumerable<IPlatformGuideProvider>? platformGuideProviders = null)
    {
        _inspectTargetUseCase = inspectTargetUseCase;
        _setupTargetUseCase = setupTargetUseCase;
        _rollbackTargetUseCase = rollbackTargetUseCase;
        _extractCodeUseCase = extractCodeUseCase;
        _searchSymbolsUseCase = searchSymbolsUseCase;
        _searchCodeUseCase = searchCodeUseCase;
        _getTypeDetailUseCase = getTypeDetailUseCase;
        _saveResearchNotesUseCase = saveResearchNotesUseCase;
        _indexAssetsUseCase = indexAssetsUseCase;
        _discoverAssetsUseCase = discoverAssetsUseCase;
        _extractAssetsUseCase = extractAssetsUseCase;
        _scaffoldModUseCase = scaffoldModUseCase;
        _buildModUseCase = buildModUseCase;
        _readLogsUseCase = readLogsUseCase;
        _manageProcessUseCase = manageProcessUseCase;
        _platformRegistry = platformRegistry;
        _storageLayout = storageLayout ?? new CandiedStorageLayout(ResolveDefaultWorkspaceRoot());
        _studioClient = studioClient;
        _visionSensorClient = visionSensorClient;
        _controlProfileStore = controlProfileStore;
        _recentTargetsStore = recentTargetsStore;
        _platformToolProviders = platformToolProviders?.ToList() ?? [];
        _platformGuideProviders = platformGuideProviders?.ToList() ?? [];
    }

    public CandiedStorageLayout StorageLayout => _storageLayout;

    public IReadOnlyList<PlatformToolDefinition> GetPlatformTools() =>
        _platformToolProviders.SelectMany(p => p.GetTools()).ToList();

    public IReadOnlyList<PlatformModdingGuide> GetPlatformGuides() =>
        _platformGuideProviders.Select(p => p.GetGuide()).ToList();

    public async Task<string> InspectTargetAsync(
        string targetPath,
        string? runnerPath = null,
        CancellationToken ct = default)
    {
        var inspectRes = await ResolveAndInspectAsync(targetPath, runnerPath, ct).ConfigureAwait(false);
        if (inspectRes.IsFailure)
        {
            return SerializeDomainError(inspectRes.Error);
        }

        var target = inspectRes.Value;
        SetActiveTarget(target);

        var guide = _platformGuideProviders.FirstOrDefault(g =>
            string.Equals(g.PlatformId, target.PlatformId, StringComparison.OrdinalIgnoreCase))?.GetGuide();

        string nextStep;
        if (target.Capabilities.Contains(PlatformCapability.EnvironmentSetup) && !target.ModLoader.IsInstalled)
        {
            nextStep = "Mod loader is not installed. Call 'setup' to provision the runtime loader and Vision sensor.";
        }
        else if (target.Capabilities.Contains(PlatformCapability.CodeKnowledge) && target.CodeModules.Count > 0)
        {
            var primary = target.CodeModules.FirstOrDefault(m => m.Tier == CodeModuleTier.CoreLogic) ?? target.CodeModules[0];
            nextStep = $"Call 'extract-code(moduleName=\"{primary.Name}\")' to extract and index symbols into SQLite.";
        }
        else
        {
            nextStep = "Call 'game-launch' to host the window in Candied Studio and inspect it via 'screenshot' and 'send-input'.";
        }

        return JsonSerializer.Serialize(new
        {
            success = true,
            targetId = target.Id.Value,
            name = target.Name,
            targetPath = target.TargetPath,
            runnerPath = target.RunnerPath,
            platformId = target.PlatformId,
            platformDisplayName = target.PlatformDisplayName,
            architecture = target.Architecture,
            engineVersion = target.EngineVersion,
            runtimeFramework = target.RuntimeFramework,
            modLoader = target.ModLoader,
            capabilities = target.Capabilities.Select(c => c.ToString()),
            modules = target.CodeModules.Select(m => new
            {
                name = m.Name,
                fullPath = m.FullPath,
                sizeBytes = m.SizeBytes,
                tier = m.Tier.ToString(),
                reason = m.Reason,
                isExtracted = m.IsExtracted
            }),
            metadata = target.Metadata,
            warnings = target.Warnings,
            platformGuide = guide?.MarkdownContent,
            nextSuggestedStep = nextStep
        }, JsonOptions);
    }

    public async Task<string> SetupEnvironmentAsync(
        string? targetPath = null,
        string? runnerPath = null,
        bool forceReinstall = false,
        bool enableConsoleLogging = true,
        CancellationToken ct = default)
    {
        var loc = await ResolveTargetLocationAsync(targetPath, runnerPath, ct).ConfigureAwait(false);
        if (loc.IsFailure)
        {
            return SerializeDomainError(loc.Error);
        }

        if (_setupTargetUseCase == null)
        {
            return SerializeError("SetupTargetEnvironmentUseCase is not configured.");
        }

        var spec = new ProvisioningSpec(ForceReinstall: forceReinstall, EnableConsoleLogging: enableConsoleLogging);
        var res = await _setupTargetUseCase.ExecuteAsync(loc.Value.TargetPath, loc.Value.RunnerPath, spec, _storageLayout, ct).ConfigureAwait(false);
        if (res.IsFailure)
        {
            return SerializeDomainError(res.Error);
        }

        var refreshed = await ResolveAndInspectAsync(loc.Value.TargetPath, loc.Value.RunnerPath, ct).ConfigureAwait(false);
        if (refreshed.IsSuccess)
        {
            SetActiveTarget(refreshed.Value);
        }

        return JsonSerializer.Serialize(new
        {
            success = true,
            report = res.Value,
            nextSuggestedStep = "Environment provisioned. Call 'extract-code' to inspect logic or 'game-launch' to start live perception."
        }, JsonOptions);
    }

    public async Task<string> RollbackEnvironmentAsync(
        string? targetPath = null,
        string? runnerPath = null,
        CancellationToken ct = default)
    {
        var loc = await ResolveTargetLocationAsync(targetPath, runnerPath, ct).ConfigureAwait(false);
        if (loc.IsFailure)
        {
            return SerializeDomainError(loc.Error);
        }

        if (_rollbackTargetUseCase == null)
        {
            return SerializeError("RollbackTargetEnvironmentUseCase is not configured.");
        }

        var res = await _rollbackTargetUseCase.ExecuteAsync(loc.Value.TargetPath, loc.Value.RunnerPath, _storageLayout, ct).ConfigureAwait(false);
        if (res.IsFailure)
        {
            return SerializeDomainError(res.Error);
        }

        return JsonSerializer.Serialize(new
        {
            success = true,
            report = res.Value
        }, JsonOptions);
    }

    public async Task<string> ExtractCodeAsync(
        string moduleName,
        string? targetPath = null,
        string? runnerPath = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(moduleName))
        {
            return SerializeError("Module name cannot be empty.");
        }

        var loc = await ResolveTargetLocationAsync(targetPath, runnerPath, ct).ConfigureAwait(false);
        if (loc.IsFailure)
        {
            return SerializeDomainError(loc.Error);
        }

        if (_extractCodeUseCase == null)
        {
            return SerializeError("ExtractCodeModuleUseCase is not configured.");
        }

        string cleanModule = Path.GetFileNameWithoutExtension(moduleName.Trim());
        var res = await _extractCodeUseCase.ExecuteAsync(
            loc.Value.TargetPath,
            loc.Value.RunnerPath,
            cleanModule,
            _storageLayout,
            null,
            ct).ConfigureAwait(false);

        if (res.IsFailure)
        {
            return SerializeDomainError(res.Error);
        }

        var inspected = await ResolveAndInspectAsync(loc.Value.TargetPath, loc.Value.RunnerPath, ct).ConfigureAwait(false);
        string targetName = inspected.IsSuccess ? inspected.Value.Name : Path.GetFileNameWithoutExtension(loc.Value.TargetPath);
        if (inspected.IsSuccess)
        {
            SetActiveTarget(inspected.Value);
        }

        string dbPath = _storageLayout.GetDatabasePath(targetName);
        _activeDatabasePath = dbPath;

        return JsonSerializer.Serialize(new
        {
            success = true,
            moduleName = res.Value.ModuleName,
            sourceDirectory = res.Value.OutputDirectory,
            sourceFileCount = res.Value.SourceFileCount,
            artifacts = new
            {
                extractedDirectory = res.Value.OutputDirectory,
                databasePath = dbPath,
                docsDirectory = _storageLayout.GetDocsDir(targetName),
                overviewDoc = _storageLayout.GetOverviewDocPath(targetName),
                namespacesDoc = _storageLayout.GetNamespacesDocPath(targetName),
                symbolsIndexJson = _storageLayout.GetSymbolsIndexJsonPath(targetName)
            },
            nextSuggestedStep = $"Module '{res.Value.ModuleName}' extracted and indexed ({res.Value.SourceFileCount} files). Call 'query', 'search-code', or 'type' to inspect symbols."
        }, JsonOptions);
    }

    public async Task<string> QuerySymbolsAsync(
        string searchTerm,
        string? targetPath = null,
        string? runnerPath = null,
        string? kind = null,
        string? namespacePrefix = null,
        string? moduleFilter = null,
        int limit = 25,
        CancellationToken ct = default)
    {
        string? dbPath = await ResolveDatabasePathAsync(targetPath, runnerPath, ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(dbPath) || !File.Exists(dbPath))
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = "Symbol database not found or not yet indexed.",
                suggestion = "Run 'extract-code(moduleName)' on the primary code module first to populate the SQLite index."
            }, JsonOptions);
        }

        if (_searchSymbolsUseCase == null)
        {
            return SerializeError("SearchSymbolsUseCase is not configured.");
        }

        var query = BuildSymbolQuery(searchTerm, kind, namespacePrefix, moduleFilter, limit);
        var res = await _searchSymbolsUseCase.ExecuteAsync(query, dbPath, ct).ConfigureAwait(false);
        if (res.IsFailure)
        {
            return SerializeDomainError(res.Error);
        }

        return JsonSerializer.Serialize(new
        {
            success = true,
            count = res.Value.Count,
            filter = new { kind = kind ?? "all", namespacePrefix, moduleFilter, limit = query.Limit },
            symbols = res.Value,
            databasePath = dbPath,
            suggestion = res.Value.Count == 0
                ? $"No symbols matched '{searchTerm}'. If the symbol belongs to another module, run 'extract-code(moduleName)'."
                : null
        }, JsonOptions);
    }

    public async Task<string> SearchCodeAsync(
        string pattern,
        string? targetPath = null,
        string? runnerPath = null,
        string? namespacePrefix = null,
        string? moduleFilter = null,
        int maxResults = 25,
        CancellationToken ct = default)
    {
        string? dbPath = await ResolveDatabasePathAsync(targetPath, runnerPath, ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(dbPath) || !File.Exists(dbPath))
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = "Symbol database not found or not yet indexed.",
                suggestion = "Run 'extract-code(moduleName)' on the primary code module first to populate the SQLite FTS5 index."
            }, JsonOptions);
        }

        if (_searchCodeUseCase == null)
        {
            return SerializeError("SearchCodeUseCase is not configured.");
        }

        int clampedLimit = Math.Clamp(maxResults <= 0 ? 25 : maxResults, 1, 200);
        var query = new CodeSearchQuery(
            SearchTerm: pattern,
            NamespacePrefix: namespacePrefix,
            ModuleFilter: moduleFilter,
            Limit: clampedLimit);

        var res = await _searchCodeUseCase.ExecuteAsync(query, dbPath, ct).ConfigureAwait(false);
        if (res.IsFailure)
        {
            return SerializeDomainError(res.Error);
        }

        string? targetName = _activeTarget?.Name;
        var matches = res.Value.Select(m => new
        {
            fullTypeName = m.FullName,
            @namespace = m.Namespace,
            moduleName = m.ModuleName,
            lineNumber = m.LineNumber,
            snippet = m.Snippet,
            sourceFilePath = ResolveExtractedSourcePath(targetName, m.ModuleName, m.RelativeSourcePath)
        }).ToList();

        return JsonSerializer.Serialize(new
        {
            success = true,
            pattern,
            count = matches.Count,
            matches,
            databasePath = dbPath
        }, JsonOptions);
    }

    public async Task<string> GetTypeDetailAsync(
        string fullTypeName,
        string? targetPath = null,
        string? runnerPath = null,
        bool signaturesOnly = false,
        string? memberFilter = null,
        CancellationToken ct = default)
    {
        string? dbPath = await ResolveDatabasePathAsync(targetPath, runnerPath, ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(dbPath) || !File.Exists(dbPath))
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = "Symbol database not found or not yet indexed.",
                suggestion = "Run 'extract-code(moduleName)' first to index target types."
            }, JsonOptions);
        }

        if (_getTypeDetailUseCase == null)
        {
            return SerializeError("GetTypeDetailUseCase is not configured.");
        }

        var res = await _getTypeDetailUseCase.ExecuteAsync(fullTypeName, dbPath, ct).ConfigureAwait(false);
        if (res.IsFailure)
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = res.Error.Message,
                detail = res.Error.Detail,
                suggestion = $"Type '{fullTypeName}' was not found in the index. Run 'extract-code(moduleName)' on the module declaring it."
            }, JsonOptions);
        }

        var detail = res.Value;
        string? sourceFilePath = ResolveExtractedSourcePath(_activeTarget?.Name, detail.ModuleName, detail.RelativeSourcePath);

        IReadOnlyList<string> fieldSignatures = detail.FieldSignatures;
        IReadOnlyList<string> methodSignatures = detail.MethodSignatures;
        string? rawSourceCode = detail.RawSourceCode;

        if (!string.IsNullOrWhiteSpace(memberFilter))
        {
            string trimmed = memberFilter.Trim();
            fieldSignatures = fieldSignatures.Where(f => f.Contains(trimmed, StringComparison.OrdinalIgnoreCase)).ToList();
            methodSignatures = methodSignatures.Where(m => m.Contains(trimmed, StringComparison.OrdinalIgnoreCase)).ToList();

            if (!signaturesOnly && !string.IsNullOrEmpty(rawSourceCode))
            {
                rawSourceCode = TypeMemberSourceExtractor.ExtractMatchingMembers(rawSourceCode, trimmed);
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
                fieldSignatures,
                methodSignatures,
                rawSourceCode,
                moduleName = detail.ModuleName,
                relativeSourcePath = detail.RelativeSourcePath
            },
            sourceFilePath,
            signaturesOnly,
            memberFilter
        }, JsonOptions);
    }

    public async Task<string> SaveResearchNotesAsync(
        string notes,
        string? targetPath = null,
        string? runnerPath = null,
        CancellationToken ct = default)
    {
        var loc = await ResolveTargetLocationAsync(targetPath, runnerPath, ct).ConfigureAwait(false);
        if (loc.IsFailure)
        {
            return SerializeDomainError(loc.Error);
        }

        if (_saveResearchNotesUseCase == null)
        {
            return SerializeError("SaveResearchNotesUseCase is not configured.");
        }

        var res = await _saveResearchNotesUseCase.ExecuteAsync(
            loc.Value.TargetPath,
            loc.Value.RunnerPath,
            notes,
            _storageLayout,
            ct).ConfigureAwait(false);

        if (res.IsFailure)
        {
            return SerializeDomainError(res.Error);
        }

        return JsonSerializer.Serialize(new
        {
            success = true,
            savedPath = res.Value
        }, JsonOptions);
    }

    public async Task<string> IndexAssetsAsync(
        string? targetPath = null,
        string? runnerPath = null,
        bool forceReindex = false,
        CancellationToken ct = default)
    {
        var loc = await ResolveTargetLocationAsync(targetPath, runnerPath, ct).ConfigureAwait(false);
        if (loc.IsFailure)
        {
            return SerializeDomainError(loc.Error);
        }

        if (_indexAssetsUseCase == null)
        {
            return SerializeError("IndexAssetsUseCase is not configured.");
        }

        var res = await _indexAssetsUseCase.ExecuteAsync(
            loc.Value.TargetPath,
            loc.Value.RunnerPath,
            _storageLayout,
            forceReindex,
            ct).ConfigureAwait(false);

        if (res.IsFailure)
        {
            return SerializeDomainError(res.Error);
        }

        return JsonSerializer.Serialize(new
        {
            success = true,
            totalIndexed = res.Value,
            message = $"Indexed {res.Value} target assets into SQLite."
        }, JsonOptions);
    }

    public async Task<string> DiscoverAssetsAsync(
        string? searchPattern = null,
        string? assetType = null,
        int maxResults = 50,
        string? targetPath = null,
        string? runnerPath = null,
        string? containerName = null,
        string? assetKey = null,
        int offset = 0,
        CancellationToken ct = default)
    {
        var loc = await ResolveTargetLocationAsync(targetPath, runnerPath, ct).ConfigureAwait(false);
        if (loc.IsFailure)
        {
            return SerializeDomainError(loc.Error);
        }

        if (_discoverAssetsUseCase == null)
        {
            return SerializeError("DiscoverAssetsUseCase is not configured.");
        }

        int clampedMax = Math.Clamp(maxResults <= 0 ? 50 : maxResults, 1, 500);
        int clampedOffset = Math.Max(0, offset);

        long? parsedAssetKey = long.TryParse(assetKey, out long dak) ? dak : null;
        var filter = new AssetDiscoveryFilter(
            SearchPattern: searchPattern,
            AssetType: assetType,
            MaxResults: clampedMax,
            ContainerName: containerName,
            AssetKey: parsedAssetKey,
            Offset: clampedOffset);

        var res = await _discoverAssetsUseCase.ExecutePageAsync(
            loc.Value.TargetPath,
            loc.Value.RunnerPath,
            filter,
            _storageLayout,
            ct).ConfigureAwait(false);

        if (res.IsFailure)
        {
            return SerializeDomainError(res.Error);
        }

        var page = res.Value;
        var containers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var a in page.Assets)
        {
            if (!string.IsNullOrEmpty(a.SourceContainer) && !containers.ContainsKey(a.SourceContainer))
            {
                containers[a.SourceContainer] = a.SourceContainerPath;
            }
        }

        return JsonSerializer.Serialize(new
        {
            success = true,
            totalMatches = page.TotalMatches,
            returnedCount = page.Assets.Count,
            offset = clampedOffset,
            hasMore = clampedOffset + page.Assets.Count < page.TotalMatches,
            containers,
            assets = page.Assets.Select(a => new
            {
                name = a.Name,
                assetType = a.AssetType,
                container = a.SourceContainer,
                assetKey = a.AssetKey,
                width = a.Width,
                height = a.Height,
                byteSize = a.ByteSize,
                extension = a.Extension
            })
        }, JsonOptions);
    }

    public async Task<string> ExtractAssetsAsync(
        string? searchPattern = null,
        string? assetType = null,
        int maxResults = 50,
        string? outputDirectory = null,
        string? targetPath = null,
        string? runnerPath = null,
        string? containerName = null,
        string? assetKey = null,
        bool disambiguateCollisions = true,
        CancellationToken ct = default)
    {
        var loc = await ResolveTargetLocationAsync(targetPath, runnerPath, ct).ConfigureAwait(false);
        if (loc.IsFailure)
        {
            return SerializeDomainError(loc.Error);
        }

        if (_extractAssetsUseCase == null)
        {
            return SerializeError("ExtractAssetsUseCase is not configured.");
        }

        string? resolvedOut = !string.IsNullOrWhiteSpace(outputDirectory)
            ? ResolveWorkspacePath(outputDirectory)
            : null;

        long? parsedAssetKey = long.TryParse(assetKey, out long eak) ? eak : null;
        var filter = new AssetExtractionFilter(
            SearchPattern: searchPattern,
            AssetType: assetType,
            MaxResults: Math.Clamp(maxResults <= 0 ? 50 : maxResults, 1, 500),
            ContainerName: containerName,
            AssetKey: parsedAssetKey,
            DisambiguateCollisions: disambiguateCollisions);

        var res = await _extractAssetsUseCase.ExecuteAsync(
            loc.Value.TargetPath,
            loc.Value.RunnerPath,
            filter,
            _storageLayout,
            resolvedOut,
            ct).ConfigureAwait(false);

        if (res.IsFailure)
        {
            return SerializeDomainError(res.Error);
        }

        return JsonSerializer.Serialize(new
        {
            success = true,
            totalExtracted = res.Value.Count,
            extractedAssets = res.Value
        }, JsonOptions);
    }

    public async Task<string> ScaffoldModAsync(
        string modName,
        string? modId = null,
        string version = "1.0.0",
        string? outputDirectory = null,
        string? targetPath = null,
        string? runnerPath = null,
        bool includeAssetPipeline = false,
        IReadOnlyDictionary<string, string>? platformOptions = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(modName))
        {
            return SerializeError("Mod name cannot be empty.");
        }

        var loc = await ResolveTargetLocationAsync(targetPath, runnerPath, ct).ConfigureAwait(false);
        if (loc.IsFailure)
        {
            return SerializeDomainError(loc.Error);
        }

        var inspectRes = await ResolveAndInspectAsync(loc.Value.TargetPath, loc.Value.RunnerPath, ct).ConfigureAwait(false);
        if (inspectRes.IsFailure)
        {
            return SerializeDomainError(inspectRes.Error);
        }

        if (_scaffoldModUseCase == null)
        {
            return SerializeError("ScaffoldModUseCase is not configured.");
        }

        SetActiveTarget(inspectRes.Value);

        string resolvedModId = string.IsNullOrWhiteSpace(modId)
            ? $"com.candied.{CandiedStorageLayout.Sanitize(modName).ToLowerInvariant()}"
            : modId.Trim();

        string resolvedOutDir = !string.IsNullOrWhiteSpace(outputDirectory)
            ? ResolveWorkspacePath(outputDirectory)
            : _storageLayout.GetModProjectDir(modName);

        var spec = new ModScaffoldSpec(
            ModName: modName,
            ModId: new ModId(resolvedModId),
            Version: string.IsNullOrWhiteSpace(version) ? "1.0.0" : version,
            OutputDirectory: resolvedOutDir,
            Target: inspectRes.Value,
            IncludeAssetPipeline: includeAssetPipeline,
            PlatformOptions: platformOptions);

        var res = await _scaffoldModUseCase.ExecuteAsync(spec, ct).ConfigureAwait(false);
        if (res.IsFailure)
        {
            return SerializeDomainError(res.Error);
        }

        return JsonSerializer.Serialize(new
        {
            success = true,
            manifest = res.Value,
            nextSuggestedStep = $"Mod scaffolded at '{res.Value.ProjectFilePath}'. Implement your mod logic and call 'build'."
        }, JsonOptions);
    }

    public async Task<string> BuildModAsync(
        string projectFilePath,
        string configuration = "Debug",
        CancellationToken ct = default)
    {
        if (_buildModUseCase == null)
        {
            return SerializeError("BuildModUseCase is not configured.");
        }

        string resolvedPath = ResolveProjectPath(projectFilePath);
        var res = await _buildModUseCase.ExecuteAsync(resolvedPath, configuration, _activeTarget, ct).ConfigureAwait(false);
        if (res.IsFailure)
        {
            return SerializeDomainError(res.Error);
        }

        var hints = res.Value.ResolutionHints;
        string nextStep;
        if (res.Value.IsSuccess)
        {
            nextStep = "Build succeeded and artifacts deployed! Launch via 'game-launch' and check 'logs' or 'screenshot' to verify.";
        }
        else if (hints != null && hints.Count > 0)
        {
            nextStep = $"Build reported missing reference(s): {string.Join(", ", hints.Select(h => h.MissingItem))}. Suggested fix: {hints[0].SuggestedCommand}";
        }
        else
        {
            nextStep = $"Build failed with {res.Value.Diagnostics.Count} diagnostics. Fix the errors and run 'build' again.";
        }

        return JsonSerializer.Serialize(new
        {
            success = true,
            report = res.Value,
            resolutionHints = hints,
            nextSuggestedStep = nextStep
        }, JsonOptions);
    }

    public async Task<string> ReadLogsAsync(
        string? logFilePath = null,
        int maxLines = 200,
        LogLevel? minLevel = null,
        string? targetPath = null,
        string? runnerPath = null,
        CancellationToken ct = default)
    {
        if (_readLogsUseCase == null)
        {
            return SerializeError("ReadRuntimeLogsUseCase is not configured.");
        }

        TargetDescriptor? target = _activeTarget;
        if (!string.IsNullOrWhiteSpace(targetPath))
        {
            var inspectRes = await ResolveAndInspectAsync(targetPath, runnerPath, ct).ConfigureAwait(false);
            if (inspectRes.IsSuccess)
            {
                target = inspectRes.Value;
                SetActiveTarget(target);
            }
        }

        string? effectiveLogPath = logFilePath;
        if (string.IsNullOrWhiteSpace(effectiveLogPath) && target != null && _platformRegistry != null)
        {
            var platform = _platformRegistry.GetPlatformById(target.PlatformId);
            effectiveLogPath = platform?.RuntimeLogs?.ResolveDefaultLogPath(target);
        }

        if (string.IsNullOrWhiteSpace(effectiveLogPath))
        {
            return SerializeError("Log file path was not provided and could not be resolved from active target.");
        }

        var res = await _readLogsUseCase.ExecuteAsync(effectiveLogPath, maxLines, target, ct).ConfigureAwait(false);
        if (res.IsFailure)
        {
            return SerializeDomainError(res.Error);
        }

        IEnumerable<LogEntry> entries = res.Value;
        if (minLevel.HasValue)
        {
            entries = entries.Where(e => e.Level >= minLevel.Value);
        }

        var list = entries.ToList();
        return JsonSerializer.Serialize(new
        {
            success = true,
            logFilePath = effectiveLogPath,
            count = list.Count,
            entries = list
        }, JsonOptions);
    }

    public async Task<string> ExecutePlatformToolAsync(
        string toolName,
        IReadOnlyDictionary<string, string?> arguments,
        CancellationToken ct = default)
    {
        var provider = _platformToolProviders.FirstOrDefault(p =>
            p.GetTools().Any(t => string.Equals(t.Name, toolName, StringComparison.OrdinalIgnoreCase)));

        if (provider == null)
        {
            return SerializeError($"Unknown platform tool: '{toolName}'.");
        }

        var normalizedArgs = new Dictionary<string, string?>(arguments, StringComparer.OrdinalIgnoreCase);
        if (normalizedArgs.TryGetValue("projectFilePath", out var pfp) && !string.IsNullOrWhiteSpace(pfp))
        {
            normalizedArgs["projectFilePath"] = ResolveProjectPath(pfp);
        }

        var result = await provider.ExecuteToolAsync(toolName, normalizedArgs, _activeTarget, _storageLayout, ct).ConfigureAwait(false);
        return result.PayloadJson;
    }

    public async Task<string> GetTargetStatusAsync(
        string? targetPath = null,
        string? runnerPath = null,
        CancellationToken ct = default)
    {
        StudioStatusInfo? studioStatus = null;
        if (_studioClient != null)
        {
            var stRes = await _studioClient.GetStatusAsync(ct).ConfigureAwait(false);
            if (stRes.IsSuccess)
            {
                studioStatus = stRes.Value;
            }
        }

        TargetProcessInfo? standaloneProc = null;
        if (_manageProcessUseCase != null && (!string.IsNullOrWhiteSpace(targetPath) || !string.IsNullOrWhiteSpace(_activeTargetPath)))
        {
            string resolvedPath = !string.IsNullOrWhiteSpace(targetPath) ? targetPath : _activeTargetPath!;
            var procRes = await _manageProcessUseCase.GetStatusAsync(resolvedPath, runnerPath ?? _activeRunnerPath, _storageLayout, ct).ConfigureAwait(false);
            if (procRes.IsSuccess)
            {
                standaloneProc = procRes.Value;
            }
        }

        bool isRunning = (studioStatus?.IsRunning ?? false) || standaloneProc != null;
        var visionTier = studioStatus?.ActiveVisionTier ?? VisionTier.None;

        return JsonSerializer.Serialize(new
        {
            success = true,
            isRunning,
            visionTier = visionTier.ToString(),
            studio = studioStatus,
            standaloneProcess = standaloneProc
        }, JsonOptions);
    }

    public async Task<string> LaunchInStudioAsync(
        string? targetPath = null,
        string? runnerPath = null,
        string? extraArguments = null,
        string? waitForLog = null,
        int stabilityTimeoutSeconds = 30,
        CancellationToken ct = default)
    {
        if (_studioClient == null)
        {
            return SerializeError("Studio host client is not configured.");
        }

        var loc = await ResolveTargetLocationAsync(targetPath, runnerPath, ct).ConfigureAwait(false);
        if (loc.IsFailure)
        {
            return SerializeDomainError(loc.Error);
        }

        var specRes = WindowLaunchProfileBuilder.Build(loc.Value.TargetPath, loc.Value.RunnerPath, extraArguments);
        if (specRes.IsFailure)
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = specRes.Error,
                suggestion = "Provide a valid PC game executable/directory in 'targetPath', or a ROM path with 'runnerPath'."
            }, JsonOptions);
        }

        var launchSpec = specRes.Value;
        _activeTargetPath = launchSpec.TargetPath;
        _activeRunnerPath = launchSpec.RunnerPath;

        if (_platformRegistry != null)
        {
            var inspected = await _platformRegistry.InspectTargetAsync(launchSpec.TargetPath, launchSpec.RunnerPath, _storageLayout, ct).ConfigureAwait(false);
            if (inspected.IsSuccess)
            {
                SetActiveTarget(inspected.Value);
            }
        }

        var ensureRes = await _studioClient.EnsureStudioRunningAsync(ct).ConfigureAwait(false);
        if (ensureRes.IsFailure)
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = $"Failed to launch Candied Studio: {ensureRes.Error}"
            }, JsonOptions);
        }

        StudioStatusInfo activeStatus;
        var currentStatus = await _studioClient.GetStatusAsync(ct).ConfigureAwait(false);
        if (currentStatus.IsSuccess &&
            currentStatus.Value.IsRunning &&
            (string.Equals(currentStatus.Value.ActiveTargetPath, launchSpec.TargetPath, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(currentStatus.Value.ActiveTargetName, launchSpec.TargetName, StringComparison.OrdinalIgnoreCase)))
        {
            activeStatus = currentStatus.Value;
        }
        else
        {
            var launchRes = await _studioClient.LaunchTargetAsync(launchSpec, ct).ConfigureAwait(false);
            if (launchRes.IsFailure)
            {
                return JsonSerializer.Serialize(new
                {
                    success = false,
                    error = $"Studio target launch failed: {launchRes.Error}"
                }, JsonOptions);
            }

            activeStatus = launchRes.Value;
        }

        if (_recentTargetsStore != null)
        {
            await _recentTargetsStore.RecordRecentAsync(new RecentTargetEntry(
                launchSpec.TargetName,
                launchSpec.TargetPath,
                launchSpec.RunnerPath,
                launchSpec.ExtraArguments,
                DateTimeOffset.UtcNow), ct).ConfigureAwait(false);
        }

        var win = activeStatus.Window;
        if (win != null && (win.Handle == 0 || !win.IsVisible || !win.IsWithinMonitor))
        {
            string logPath = VisionConfigPaths.GetStudioLogPath();
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = "Candied Studio is running, but the main window is hidden, invalid, or outside monitor bounds.",
                logPath,
                suggestion = $"Inspect Studio diagnostics in '{logPath}'."
            }, JsonOptions);
        }

        // Stability loop supporting BOTH Tier 2 (SensorBridge) and Tier 1 (BaseWindow)
        const double stabilityDiffThreshold = 0.25;
        const int requiredStableWindows = 2;
        int timeoutMs = Math.Clamp(stabilityTimeoutSeconds, 2, 120) * 1000;
        var sw = Stopwatch.StartNew();
        int consecutiveStable = 0;
        string lastStatus = "Waiting for initial rendered frames...";
        string activeSceneName = "Unknown";
        int activeNodeCount = 0;
        VisionTier observedTier = activeStatus.ActiveVisionTier;
        int frameW = activeStatus.TargetWidth;
        int frameH = activeStatus.TargetHeight;

        while (sw.ElapsedMilliseconds < timeoutMs && !ct.IsCancellationRequested)
        {
            var frame1Res = await CaptureFrameInternalAsync(null, null, ct).ConfigureAwait(false);
            if (frame1Res.IsFailure || frame1Res.Value.IsUniform)
            {
                consecutiveStable = 0;
                lastStatus = frame1Res.IsFailure
                    ? $"Initial frame capture pending: {frame1Res.Error.Message}"
                    : "Uniform loading screen detected.";
                await Task.Delay(500, ct).ConfigureAwait(false);
                continue;
            }

            await Task.Delay(600, ct).ConfigureAwait(false);

            var frame2Res = await CaptureFrameInternalAsync(null, null, ct).ConfigureAwait(false);
            if (frame2Res.IsFailure || frame2Res.Value.IsUniform)
            {
                consecutiveStable = 0;
                continue;
            }

            observedTier = frame2Res.Value.SourceTier;
            frameW = frame2Res.Value.Width > 0 ? frame2Res.Value.Width : frameW;
            frameH = frame2Res.Value.Height > 0 ? frame2Res.Value.Height : frameH;

            bool sceneStable = true;
            var sensorConn = await TryResolveSensorConnectionAsync(null, null, ct).ConfigureAwait(false);
            if (sensorConn.BaseUrl != null && _visionSensorClient != null)
            {
                var sceneRes = await _visionSensorClient.GetSceneInfoAsync(sensorConn.BaseUrl, sensorConn.Token, ct).ConfigureAwait(false);
                if (sceneRes.IsSuccess)
                {
                    observedTier = VisionTier.SensorBridge;
                    activeSceneName = sceneRes.Value.ActiveSceneName;
                    activeNodeCount = sceneRes.Value.ActiveNodeCount;
                }
            }

            bool logMarkerFound = true;
            if (!string.IsNullOrWhiteSpace(waitForLog) && !string.IsNullOrWhiteSpace(launchSpec.LogFilePath))
            {
                logMarkerFound = ContainsLogMarker(launchSpec.LogFilePath, waitForLog.Trim());
                if (!logMarkerFound)
                {
                    lastStatus = $"Waiting for log marker '{waitForLog}'...";
                }
            }

            double diff = PurePngDecoder.CalculateDiffPercentage(frame1Res.Value.PngBytes, frame2Res.Value.PngBytes);
            if (diff < stabilityDiffThreshold && sceneStable && logMarkerFound)
            {
                consecutiveStable++;
                if (consecutiveStable >= requiredStableWindows)
                {
                    var refreshedStatus = await _studioClient.GetStatusAsync(ct).ConfigureAwait(false);
                    var finalWin = refreshedStatus.IsSuccess ? refreshedStatus.Value.Window : win;

                    return JsonSerializer.Serialize(new
                    {
                        success = true,
                        status = "Running",
                        targetName = launchSpec.TargetName,
                        targetPath = launchSpec.TargetPath,
                        runnerPath = launchSpec.RunnerPath,
                        visionTier = observedTier.ToString(),
                        activeSceneName,
                        activeNodeCount,
                        resolution = new { width = frameW, height = frameH },
                        window = finalWin != null ? new
                        {
                            mainWindowHandle = finalWin.Handle,
                            isWindowVisible = finalWin.IsVisible,
                            windowState = finalWin.WindowState,
                            isWithinMonitor = finalWin.IsWithinMonitor,
                            isResponding = finalWin.IsResponding
                        } : null,
                        waitOutcome = $"Stable ({consecutiveStable} consecutive windows, diff: {diff:F2}% < {stabilityDiffThreshold:F2}%)",
                        elapsedSeconds = sw.Elapsed.TotalSeconds,
                        nextSuggestedStep = "Use 'screenshot' to inspect the window and 'send-input' or 'trigger-action' to interact."
                    }, JsonOptions);
                }

                continue;
            }

            consecutiveStable = 0;
            lastStatus = $"Frame delta {diff:F2}% >= {stabilityDiffThreshold:F2}%.";
            await Task.Delay(400, ct).ConfigureAwait(false);
        }

        // Even if dynamic animation kept diff >= 0.25%, if window is running and non-uniform, return running status with note!
        return JsonSerializer.Serialize(new
        {
            success = true,
            status = "Running",
            targetName = launchSpec.TargetName,
            targetPath = launchSpec.TargetPath,
            runnerPath = launchSpec.RunnerPath,
            visionTier = observedTier.ToString(),
            activeSceneName,
            activeNodeCount,
            resolution = new { width = frameW, height = frameH },
            waitOutcome = $"Target running (dynamic animation or transition active after {sw.Elapsed.TotalSeconds:F1}s: {lastStatus})",
            nextSuggestedStep = "Capture a frame with 'screenshot' to inspect the current visual state."
        }, JsonOptions);
    }

    public async Task<string> CloseStudioTargetAsync(CancellationToken ct = default)
    {
        if (_studioClient == null)
        {
            return SerializeError("Studio host client is not configured.");
        }

        var res = await _studioClient.CloseTargetAsync(ct).ConfigureAwait(false);
        if (res.IsFailure)
        {
            return SerializeError(res.Error);
        }

        return JsonSerializer.Serialize(new
        {
            success = true,
            status = "Closed",
            message = "Target window closed in Candied Studio."
        }, JsonOptions);
    }

    public async Task<string> ListRecentTargetsAsync(CancellationToken ct = default)
    {
        if (_recentTargetsStore == null)
        {
            return JsonSerializer.Serialize(new { success = true, count = 0, targets = Array.Empty<object>() }, JsonOptions);
        }

        var items = await _recentTargetsStore.ListRecentAsync(ct).ConfigureAwait(false);
        return JsonSerializer.Serialize(new
        {
            success = true,
            count = items.Count,
            targets = items
        }, JsonOptions);
    }

    public async Task<string> GetCapabilitiesAsync(
        int? port = null,
        string? token = null,
        CancellationToken ct = default)
    {
        var sensorConn = await TryResolveSensorConnectionAsync(port, token, ct).ConfigureAwait(false);
        if (sensorConn.BaseUrl != null && _visionSensorClient != null)
        {
            var capRes = await _visionSensorClient.GetCapabilitiesAsync(sensorConn.BaseUrl, sensorConn.Token, ct).ConfigureAwait(false);
            if (capRes.IsSuccess)
            {
                return JsonSerializer.Serialize(new
                {
                    success = true,
                    visionTier = VisionTier.SensorBridge.ToString(),
                    capabilities = capRes.Value.Capabilities,
                    screenshotMethod = capRes.Value.ScreenshotMethod,
                    screenshotLimitations = capRes.Value.ScreenshotLimitations,
                    pointerMethod = capRes.Value.PointerMethod,
                    metadata = capRes.Value.Metadata
                }, JsonOptions);
            }
        }

        if (_studioClient != null)
        {
            var statusRes = await _studioClient.GetStatusAsync(ct).ConfigureAwait(false);
            if (statusRes.IsSuccess && statusRes.Value.IsRunning)
            {
                return JsonSerializer.Serialize(new
                {
                    success = true,
                    visionTier = VisionTier.BaseWindow.ToString(),
                    capabilities = BaseWindowCapabilityList,
                    screenshotMethod = "Win32.PrintWindow+BitBlt",
                    pointerMethod = "Win32.SendInput",
                    metadata = new Dictionary<string, string>
                    {
                        ["mode"] = "BaseWindow (Tier 1 Win32 fallback — no in-game sensor required)"
                    }
                }, JsonOptions);
            }
        }

        return JsonSerializer.Serialize(new
        {
            success = false,
            error = "No active target window or Vision sensor connection found.",
            suggestion = "Launch any game or emulator via 'game-launch(targetPath)' first."
        }, JsonOptions);
    }

    public async Task<string> CaptureScreenshotAsync(
        string? savePath = null,
        string? targetPath = null,
        int? port = null,
        string? token = null,
        CancellationToken ct = default)
    {
        var frameRes = await CaptureFrameInternalAsync(port, token, ct).ConfigureAwait(false);
        if (frameRes.IsFailure)
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = frameRes.Error.Message,
                code = frameRes.Error.Code,
                suggestion = frameRes.Error.Suggestion
            }, JsonOptions);
        }

        var frame = frameRes.Value;
        string outputFilePath = ResolveScreenshotSavePath(savePath, targetPath);
        string? parentDir = Path.GetDirectoryName(outputFilePath);
        if (!string.IsNullOrEmpty(parentDir))
        {
            Directory.CreateDirectory(parentDir);
        }

        await File.WriteAllBytesAsync(outputFilePath, frame.PngBytes, ct).ConfigureAwait(false);

        return JsonSerializer.Serialize(new
        {
            success = true,
            filePath = outputFilePath,
            width = frame.Width,
            height = frame.Height,
            isUniform = frame.IsUniform,
            visionTier = frame.SourceTier.ToString(),
            fileSizeBytes = frame.PngBytes.Length,
            nextSuggestedStep = "Inspect the saved PNG and interact via 'send-input' or 'trigger-action'."
        }, JsonOptions);
    }

    public async Task<string> SendInputAsync(
        VisionInputRequest request,
        int? port = null,
        string? token = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<VisionInputResponse, VisionError> dispatchRes;
        VisionTier usedTier = VisionTier.SensorBridge;

        var sensorConn = await TryResolveSensorConnectionAsync(port, token, ct).ConfigureAwait(false);
        if (sensorConn.BaseUrl != null && _visionSensorClient != null)
        {
            var sensorTry = await _visionSensorClient.SendInputAsync(sensorConn.BaseUrl, sensorConn.Token, request, ct).ConfigureAwait(false);
            if (sensorTry.IsSuccess || port.HasValue)
            {
                dispatchRes = sensorTry;
            }
            else if (_studioClient != null)
            {
                usedTier = VisionTier.BaseWindow;
                dispatchRes = await _studioClient.SendInputAsync(request, ct).ConfigureAwait(false);
            }
            else
            {
                dispatchRes = sensorTry;
            }
        }
        else if (_studioClient != null)
        {
            usedTier = VisionTier.BaseWindow;
            dispatchRes = await _studioClient.SendInputAsync(request, ct).ConfigureAwait(false);
        }
        else
        {
            return SerializeError("Neither Vision sensor nor Studio host client is connected.");
        }

        if (dispatchRes.IsFailure)
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = dispatchRes.Error.Message,
                code = dispatchRes.Error.Code,
                suggestion = dispatchRes.Error.Suggestion
            }, JsonOptions);
        }

        string? markedScreenshotPath = null;
        if (string.Equals(request.Channel, VisionConstants.ChannelPointer, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(request.Action, "click", StringComparison.OrdinalIgnoreCase) &&
            request.X.HasValue && request.Y.HasValue)
        {
            try
            {
                var followUp = await CaptureFrameInternalAsync(port, token, ct).ConfigureAwait(false);
                if (followUp.IsSuccess && followUp.Value.PngBytes.Length > 0)
                {
                    byte[] marked = PurePngDecoder.DrawClickMarker(
                        followUp.Value.PngBytes,
                        (int)Math.Round(request.X.Value),
                        (int)Math.Round(request.Y.Value));
                    markedScreenshotPath = ResolveScreenshotSavePath(null, null, prefix: "screenshot_click");
                    string? dir = Path.GetDirectoryName(markedScreenshotPath);
                    if (!string.IsNullOrEmpty(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }

                    await File.WriteAllBytesAsync(markedScreenshotPath, marked, ct).ConfigureAwait(false);
                }
            }
            catch
            {
            }
        }

        return JsonSerializer.Serialize(new
        {
            success = dispatchRes.Value.Success,
            visionTier = usedTier.ToString(),
            channel = dispatchRes.Value.Channel,
            action = dispatchRes.Value.Action,
            hitPointX = dispatchRes.Value.HitPointX,
            hitPointY = dispatchRes.Value.HitPointY,
            screenshotPath = markedScreenshotPath,
            message = dispatchRes.Value.Message
        }, JsonOptions);
    }

    public async Task<string> GetControlsAsync(
        string? targetPath = null,
        string? runnerPath = null,
        CancellationToken ct = default)
    {
        string targetName = await ResolveTargetNameForControlsAsync(targetPath, runnerPath, ct).ConfigureAwait(false);
        string profilePath = _storageLayout.GetControlProfilePath(targetName);

        GameControlProfile? profile = null;
        if (_controlProfileStore != null)
        {
            profile = await _controlProfileStore.LoadProfileAsync(profilePath, ct).ConfigureAwait(false);
        }

        return JsonSerializer.Serialize(new
        {
            success = true,
            targetName,
            profilePath,
            hasCustomProfile = profile != null,
            actions = profile?.Actions ?? Array.Empty<ControlActionEntry>(),
            builtInFallbacks = BuiltInFallbackActionList,
            nextSuggestedStep = "Call 'trigger-action(actionName)' to execute a semantic action or 'set-controls' to register discovered bindings."
        }, JsonOptions);
    }

    public async Task<string> SetControlsAsync(
        IReadOnlyList<ControlActionEntry> actions,
        bool overwriteUserBindings = false,
        string? targetPath = null,
        string? runnerPath = null,
        CancellationToken ct = default)
    {
        if (_controlProfileStore == null)
        {
            return SerializeError("IGameControlProfileStore is not configured.");
        }

        if (actions == null || actions.Count == 0)
        {
            return SerializeError("At least one control action entry is required.");
        }

        string targetName = await ResolveTargetNameForControlsAsync(targetPath, runnerPath, ct).ConfigureAwait(false);
        string profilePath = _storageLayout.GetControlProfilePath(targetName);

        var updated = await _controlProfileStore.MergeAndSaveActionsAsync(
            profilePath,
            targetName,
            targetName,
            actions,
            overwriteHigherPrioritySources: overwriteUserBindings,
            ct).ConfigureAwait(false);

        return JsonSerializer.Serialize(new
        {
            success = true,
            targetName,
            profilePath,
            totalActions = updated.Actions.Count,
            actions = updated.Actions
        }, JsonOptions);
    }

    public async Task<string> TriggerActionAsync(
        string actionName,
        string? targetPath = null,
        string? runnerPath = null,
        int? port = null,
        string? token = null,
        CancellationToken ct = default)
    {
        string targetName = await ResolveTargetNameForControlsAsync(targetPath, runnerPath, ct).ConfigureAwait(false);
        string profilePath = _storageLayout.GetControlProfilePath(targetName);

        GameControlProfile? profile = _controlProfileStore != null
            ? await _controlProfileStore.LoadProfileAsync(profilePath, ct).ConfigureAwait(false)
            : null;

        IReadOnlyCollection<string>? activeCaps = null;
        int screenW = 1280;
        int screenH = 720;

        var sensorConn = await TryResolveSensorConnectionAsync(port, token, ct).ConfigureAwait(false);
        if (sensorConn.BaseUrl != null && _visionSensorClient != null)
        {
            var capsRes = await _visionSensorClient.GetCapabilitiesAsync(sensorConn.BaseUrl, sensorConn.Token, ct).ConfigureAwait(false);
            if (capsRes.IsSuccess)
            {
                activeCaps = capsRes.Value.Capabilities;
            }

            var helloRes = await _visionSensorClient.GetHelloAsync(sensorConn.BaseUrl, sensorConn.Token, ct).ConfigureAwait(false);
            if (helloRes.IsSuccess && helloRes.Value.ScreenWidth > 0 && helloRes.Value.ScreenHeight > 0)
            {
                screenW = helloRes.Value.ScreenWidth;
                screenH = helloRes.Value.ScreenHeight;
            }
        }

        var resolvedReq = ControlActionResolver.ResolveAction(actionName, profile, activeCaps, screenW, screenH);
        if (resolvedReq.IsFailure)
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                code = resolvedReq.Error.Code,
                error = resolvedReq.Error.Message,
                suggestion = resolvedReq.Error.Suggestion
            }, JsonOptions);
        }

        return await SendInputAsync(resolvedReq.Value, port, token, ct).ConfigureAwait(false);
    }

    public async Task<string> WaitIdleAsync(
        int maxFrames = 10,
        int timeoutMs = 3000,
        int? port = null,
        string? token = null,
        CancellationToken ct = default)
    {
        var sensorConn = await TryResolveSensorConnectionAsync(port, token, ct).ConfigureAwait(false);
        if (sensorConn.BaseUrl != null && _visionSensorClient != null)
        {
            var res = await _visionSensorClient.WaitForIdleAsync(sensorConn.BaseUrl, sensorConn.Token, maxFrames, timeoutMs, ct).ConfigureAwait(false);
            if (res.IsSuccess)
            {
                return JsonSerializer.Serialize(new
                {
                    success = true,
                    visionTier = VisionTier.SensorBridge.ToString(),
                    idleReached = res.Value.IdleReached,
                    elapsedFrames = res.Value.ElapsedFrames,
                    elapsedMs = res.Value.ElapsedMs
                }, JsonOptions);
            }
        }

        // Tier 1 BaseWindow visual frame-diff idle wait
        var sw = Stopwatch.StartNew();
        int clampedTimeout = Math.Clamp(timeoutMs, 200, 15000);
        while (sw.ElapsedMilliseconds < clampedTimeout && !ct.IsCancellationRequested)
        {
            var f1 = await CaptureFrameInternalAsync(port, token, ct).ConfigureAwait(false);
            if (f1.IsFailure)
            {
                return JsonSerializer.Serialize(new
                {
                    success = false,
                    error = f1.Error.Message,
                    suggestion = f1.Error.Suggestion
                }, JsonOptions);
            }

            await Task.Delay(180, ct).ConfigureAwait(false);
            var f2 = await CaptureFrameInternalAsync(port, token, ct).ConfigureAwait(false);
            if (f2.IsSuccess && PurePngDecoder.CalculateDiffPercentage(f1.Value.PngBytes, f2.Value.PngBytes) < 0.25)
            {
                return JsonSerializer.Serialize(new
                {
                    success = true,
                    visionTier = VisionTier.BaseWindow.ToString(),
                    idleReached = true,
                    elapsedMs = sw.ElapsedMilliseconds
                }, JsonOptions);
            }
        }

        return JsonSerializer.Serialize(new
        {
            success = true,
            visionTier = VisionTier.BaseWindow.ToString(),
            idleReached = false,
            elapsedMs = sw.ElapsedMilliseconds
        }, JsonOptions);
    }

    public async Task<string> ListUiElementsAsync(
        int? port = null,
        string? token = null,
        CancellationToken ct = default)
    {
        var sensorConn = await TryResolveSensorConnectionAsync(port, token, ct).ConfigureAwait(false);
        if (sensorConn.BaseUrl == null || _visionSensorClient == null)
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = "Tier 2 Vision sensor is not connected.",
                suggestion = "UI tree enumeration requires a Tier 2 sensor. In Tier 1 BaseWindow mode, use 'screenshot' + 'send-input' or 'trigger-action'."
            }, JsonOptions);
        }

        var res = await _visionSensorClient.GetUiElementsAsync(sensorConn.BaseUrl, sensorConn.Token, ct).ConfigureAwait(false);
        if (res.IsFailure)
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = res.Error.Message,
                suggestion = res.Error.Suggestion
            }, JsonOptions);
        }

        return JsonSerializer.Serialize(new
        {
            success = true,
            count = res.Value.Count,
            elements = res.Value
        }, JsonOptions);
    }

    public async Task<string> GetSceneInfoAsync(
        int? port = null,
        string? token = null,
        CancellationToken ct = default)
    {
        var sensorConn = await TryResolveSensorConnectionAsync(port, token, ct).ConfigureAwait(false);
        if (sensorConn.BaseUrl == null || _visionSensorClient == null)
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = "Tier 2 Vision sensor is not connected.",
                suggestion = "Scene telemetry requires a Tier 2 sensor. Use 'screenshot' in Tier 1 BaseWindow mode."
            }, JsonOptions);
        }

        var res = await _visionSensorClient.GetSceneInfoAsync(sensorConn.BaseUrl, sensorConn.Token, ct).ConfigureAwait(false);
        if (res.IsFailure)
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = res.Error.Message,
                suggestion = res.Error.Suggestion
            }, JsonOptions);
        }

        return JsonSerializer.Serialize(new
        {
            success = true,
            scene = res.Value
        }, JsonOptions);
    }

    public async Task<string> InspectSceneAsync(
        string? nameFilter = null,
        bool includeInactive = false,
        bool visibleOnly = false,
        string? attributeFilter = null,
        int maxResults = 100,
        int? port = null,
        string? token = null,
        CancellationToken ct = default)
    {
        var sensorConn = await TryResolveSensorConnectionAsync(port, token, ct).ConfigureAwait(false);
        if (sensorConn.BaseUrl == null || _visionSensorClient == null)
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = "Tier 2 Vision sensor is not connected.",
                suggestion = "Scene graph inspection requires an active Tier 2 sensor."
            }, JsonOptions);
        }

        var query = new SceneGraphQuery(
            NameFilter: nameFilter,
            IncludeInactive: includeInactive,
            VisibleOnly: visibleOnly,
            AttributeFilter: attributeFilter,
            MaxResults: Math.Clamp(maxResults <= 0 ? 100 : maxResults, 1, 1000));

        var res = await _visionSensorClient.InspectSceneAsync(sensorConn.BaseUrl, sensorConn.Token, query, ct).ConfigureAwait(false);
        if (res.IsFailure)
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = res.Error.Message,
                suggestion = res.Error.Suggestion
            }, JsonOptions);
        }

        return JsonSerializer.Serialize(new
        {
            success = res.Value.Success,
            totalCameras = res.Value.TotalCameras,
            totalNodes = res.Value.TotalNodes,
            totalMatched = res.Value.TotalMatched,
            truncated = res.Value.Truncated,
            cameras = res.Value.Cameras,
            nodes = res.Value.Nodes,
            message = res.Value.Message
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
        var sensorConn = await TryResolveSensorConnectionAsync(port, token, ct).ConfigureAwait(false);
        if (sensorConn.BaseUrl == null || _visionSensorClient == null)
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = "Tier 2 Vision sensor is not connected.",
                suggestion = "Runtime asset inspection requires an active Tier 2 sensor."
            }, JsonOptions);
        }

        var query = new RuntimeAssetsQuery(
            NameFilter: nameFilter,
            AssetType: string.IsNullOrWhiteSpace(assetType) ? "All" : assetType.Trim(),
            MaxResults: Math.Clamp(maxResults <= 0 ? 50 : maxResults, 1, 500));

        var res = await _visionSensorClient.InspectRuntimeAssetsAsync(sensorConn.BaseUrl, sensorConn.Token, query, ct).ConfigureAwait(false);
        if (res.IsFailure)
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = res.Error.Message,
                suggestion = res.Error.Suggestion
            }, JsonOptions);
        }

        return JsonSerializer.Serialize(new
        {
            success = res.Value.Success,
            totalContainers = res.Value.TotalContainers,
            totalLoadedAssets = res.Value.TotalLoadedAssets,
            truncated = res.Value.Truncated,
            containers = res.Value.Containers,
            loadedAssets = res.Value.LoadedAssets,
            message = res.Value.Message
        }, JsonOptions);
    }

    private async Task<Result<VisionFrame, VisionError>> CaptureFrameInternalAsync(
        int? port,
        string? token,
        CancellationToken ct)
    {
        var sensorConn = await TryResolveSensorConnectionAsync(port, token, ct).ConfigureAwait(false);
        if (sensorConn.BaseUrl != null && _visionSensorClient != null)
        {
            var sensorFrame = await _visionSensorClient.CaptureScreenshotAsync(sensorConn.BaseUrl, sensorConn.Token, ct).ConfigureAwait(false);
            if (sensorFrame.IsSuccess || port.HasValue)
            {
                return sensorFrame;
            }
        }

        if (_studioClient != null)
        {
            return await _studioClient.CaptureScreenshotAsync(ct).ConfigureAwait(false);
        }

        return Result.Fail<VisionFrame, VisionError>(new VisionError(
            "NO_VISION_SOURCE",
            "Neither a Tier 2 Vision sensor nor Candied Studio is active.",
            "Launch a game or emulator via 'game-launch(targetPath)' first."));
    }

    private async Task<(string? BaseUrl, string Token)> TryResolveSensorConnectionAsync(
        int? explicitPort,
        string? explicitToken,
        CancellationToken ct)
    {
        if (explicitPort.HasValue && explicitPort.Value > 0 && !string.IsNullOrWhiteSpace(explicitToken))
        {
            return ($"http://127.0.0.1:{explicitPort.Value}", explicitToken);
        }

        if (_studioClient != null)
        {
            var session = await _studioClient.GetCurrentSessionAsync(ct).ConfigureAwait(false);
            if (session?.SensorPort != null && !string.IsNullOrWhiteSpace(session.SensorToken))
            {
                string url = $"http://127.0.0.1:{session.SensorPort.Value}";
                if (_visionSensorClient != null)
                {
                    var hello = await _visionSensorClient.GetHelloAsync(url, session.SensorToken, ct).ConfigureAwait(false);
                    if (hello.IsSuccess)
                    {
                        return (url, session.SensorToken);
                    }
                }
            }
        }

        if (int.TryParse(Environment.GetEnvironmentVariable("CANDIED_PORT"), out int envPort) && envPort > 0)
        {
            string? envToken = Environment.GetEnvironmentVariable("CANDIED_TOKEN");
            if (!string.IsNullOrWhiteSpace(envToken))
            {
                return ($"http://127.0.0.1:{envPort}", envToken);
            }
        }

        return (null, string.Empty);
    }

    private async Task<Result<TargetDescriptor, DomainError>> ResolveAndInspectAsync(
        string targetPath,
        string? runnerPath,
        CancellationToken ct)
    {
        if (_inspectTargetUseCase != null)
        {
            return await _inspectTargetUseCase.ExecuteAsync(targetPath, runnerPath, _storageLayout, ct).ConfigureAwait(false);
        }

        if (_platformRegistry != null)
        {
            var res = await _platformRegistry.InspectTargetAsync(targetPath, runnerPath, _storageLayout, ct).ConfigureAwait(false);
            return res.IsSuccess
                ? Result.Ok<TargetDescriptor, DomainError>(res.Value)
                : Result.Fail<TargetDescriptor, DomainError>(res.Error);
        }

        return Result.Fail<TargetDescriptor, DomainError>(new DetectionError("No target inspector or platform registry configured."));
    }

    private async Task<Result<(string TargetPath, string? RunnerPath), DomainError>> ResolveTargetLocationAsync(
        string? explicitTargetPath,
        string? explicitRunnerPath,
        CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(explicitTargetPath))
        {
            _activeTargetPath = explicitTargetPath;
            _activeRunnerPath = explicitRunnerPath;
            return Result.Ok<(string, string?), DomainError>((explicitTargetPath, explicitRunnerPath));
        }

        if (!string.IsNullOrWhiteSpace(_activeTargetPath))
        {
            return Result.Ok<(string, string?), DomainError>((_activeTargetPath, explicitRunnerPath ?? _activeRunnerPath));
        }

        if (_studioClient != null)
        {
            var st = await _studioClient.GetStatusAsync(ct).ConfigureAwait(false);
            if (st.IsSuccess && !string.IsNullOrWhiteSpace(st.Value.ActiveTargetPath))
            {
                _activeTargetPath = st.Value.ActiveTargetPath;
                _activeRunnerPath = st.Value.ActiveRunnerPath;
                return Result.Ok<(string, string?), DomainError>((_activeTargetPath, _activeRunnerPath));
            }
        }

        if (_recentTargetsStore != null)
        {
            var recent = await _recentTargetsStore.ListRecentAsync(ct).ConfigureAwait(false);
            if (recent.Count == 1)
            {
                _activeTargetPath = recent[0].TargetPath;
                _activeRunnerPath = recent[0].RunnerPath;
                return Result.Ok<(string, string?), DomainError>((_activeTargetPath, _activeRunnerPath));
            }
        }

        return Result.Fail<(string, string?), DomainError>(
            new DetectionError("No active target session. Provide 'targetPath' (and optional 'runnerPath')."));
    }

    private async Task<string?> ResolveDatabasePathAsync(
        string? targetPath,
        string? runnerPath,
        CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(targetPath))
        {
            if (targetPath.EndsWith(".db", StringComparison.OrdinalIgnoreCase) && File.Exists(targetPath))
            {
                return targetPath;
            }

            var inspected = await ResolveAndInspectAsync(targetPath, runnerPath, ct).ConfigureAwait(false);
            if (inspected.IsSuccess)
            {
                SetActiveTarget(inspected.Value);
                return _storageLayout.GetDatabasePath(inspected.Value.Name);
            }
        }

        return _activeDatabasePath;
    }

    private async Task<string> ResolveTargetNameForControlsAsync(
        string? targetPath,
        string? runnerPath,
        CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(targetPath))
        {
            var inspected = await ResolveAndInspectAsync(targetPath, runnerPath, ct).ConfigureAwait(false);
            if (inspected.IsSuccess)
            {
                SetActiveTarget(inspected.Value);
                return inspected.Value.Name;
            }

            return Path.GetFileNameWithoutExtension(targetPath.TrimEnd('/', '\\'));
        }

        if (_activeTarget != null)
        {
            return _activeTarget.Name;
        }

        if (_studioClient != null)
        {
            var st = await _studioClient.GetStatusAsync(ct).ConfigureAwait(false);
            if (st.IsSuccess && !string.IsNullOrWhiteSpace(st.Value.ActiveTargetName))
            {
                return st.Value.ActiveTargetName;
            }
        }

        return "Default";
    }

    private void SetActiveTarget(TargetDescriptor target)
    {
        _activeTarget = target;
        _activeTargetPath = target.TargetPath;
        _activeRunnerPath = target.RunnerPath;
        _activeDatabasePath = _storageLayout.GetDatabasePath(target.Name);
    }

    private string ResolveScreenshotSavePath(string? savePath, string? targetPath, string prefix = "screenshot")
    {
        if (!string.IsNullOrWhiteSpace(savePath))
        {
            return ResolveWorkspacePath(savePath);
        }

        string targetName = _activeTarget?.Name
            ?? (!string.IsNullOrWhiteSpace(targetPath) ? Path.GetFileNameWithoutExtension(targetPath.TrimEnd('/', '\\')) : "Default");
        string dir = _storageLayout.GetScreenshotsDir(targetName);
        return Path.Combine(dir, $"{prefix}_{DateTime.UtcNow:yyyyMMdd_HHmmssfff}.png");
    }

    private string? ResolveExtractedSourcePath(string? targetName, string? moduleName, string? relativeSourcePath)
    {
        if (string.IsNullOrWhiteSpace(targetName) ||
            string.IsNullOrWhiteSpace(moduleName) ||
            string.IsNullOrWhiteSpace(relativeSourcePath))
        {
            return null;
        }

        string normalizedRel = relativeSourcePath
            .Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar);
        return Path.Combine(_storageLayout.GetExtractedCodeDir(targetName, moduleName), normalizedRel);
    }

    private string ResolveProjectPath(string projectFilePath)
    {
        if (string.IsNullOrWhiteSpace(projectFilePath) || Path.IsPathRooted(projectFilePath))
        {
            return projectFilePath;
        }

        string candidate = ResolveWorkspacePath(projectFilePath);
        return File.Exists(candidate) || !File.Exists(Path.GetFullPath(projectFilePath))
            ? candidate
            : Path.GetFullPath(projectFilePath);
    }

    private string ResolveWorkspacePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return _storageLayout.WorkspaceRoot;
        }

        string trimmed = path.Trim();
        return Path.IsPathRooted(trimmed)
            ? Path.GetFullPath(trimmed)
            : Path.GetFullPath(Path.Combine(_storageLayout.WorkspaceRoot, trimmed));
    }

    public static string ResolveDefaultWorkspaceRoot(string? currentDirectory = null, string? baseDirectory = null)
    {
        string? envRoot = Environment.GetEnvironmentVariable("CANDIED_WORKSPACE_ROOT");
        if (!string.IsNullOrWhiteSpace(envRoot) && Directory.Exists(envRoot))
        {
            return Path.GetFullPath(envRoot);
        }

        string cwd = !string.IsNullOrWhiteSpace(currentDirectory) ? currentDirectory : Directory.GetCurrentDirectory();
        string appBase = !string.IsNullOrWhiteSpace(baseDirectory) ? baseDirectory : AppContext.BaseDirectory;

        return FindAncestorWithRepoMarker(cwd)
            ?? FindAncestorWithRepoMarker(appBase)
            ?? Path.GetFullPath(cwd);
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
        }

        return null;
    }

    private static SymbolQuery BuildSymbolQuery(
        string searchTerm,
        string? kind,
        string? namespacePrefix,
        string? moduleFilter,
        int limit)
    {
        int clampedLimit = Math.Clamp(limit <= 0 ? 25 : limit, 1, 200);
        string normalized = (kind ?? "all").Trim().ToLowerInvariant();

        return normalized switch
        {
            "types" or "type" => new SymbolQuery(searchTerm, SymbolKindFilter.TypeOnly, namespacePrefix, clampedLimit, ModuleFilter: moduleFilter),
            "members" or "member" => new SymbolQuery(searchTerm, SymbolKindFilter.MemberOnly, namespacePrefix, clampedLimit, ModuleFilter: moduleFilter),
            "class" => new SymbolQuery(searchTerm, SymbolKindFilter.TypeOnly, namespacePrefix, clampedLimit, ExactKind: SymbolKind.Class, ModuleFilter: moduleFilter),
            "struct" => new SymbolQuery(searchTerm, SymbolKindFilter.TypeOnly, namespacePrefix, clampedLimit, ExactKind: SymbolKind.Struct, ModuleFilter: moduleFilter),
            "interface" => new SymbolQuery(searchTerm, SymbolKindFilter.TypeOnly, namespacePrefix, clampedLimit, ExactKind: SymbolKind.Interface, ModuleFilter: moduleFilter),
            "enum" => new SymbolQuery(searchTerm, SymbolKindFilter.TypeOnly, namespacePrefix, clampedLimit, EnumOnly: true, ModuleFilter: moduleFilter),
            "method" => new SymbolQuery(searchTerm, SymbolKindFilter.MemberOnly, namespacePrefix, clampedLimit, ExactKind: SymbolKind.Method, ModuleFilter: moduleFilter),
            "field" => new SymbolQuery(searchTerm, SymbolKindFilter.MemberOnly, namespacePrefix, clampedLimit, ExactKind: SymbolKind.Field, ModuleFilter: moduleFilter),
            "property" => new SymbolQuery(searchTerm, SymbolKindFilter.MemberOnly, namespacePrefix, clampedLimit, ExactKind: SymbolKind.Property, ModuleFilter: moduleFilter),
            "event" => new SymbolQuery(searchTerm, SymbolKindFilter.MemberOnly, namespacePrefix, clampedLimit, ExactKind: SymbolKind.Event, ModuleFilter: moduleFilter),
            _ => new SymbolQuery(searchTerm, SymbolKindFilter.All, namespacePrefix, clampedLimit, ModuleFilter: moduleFilter)
        };
    }

    private static bool ContainsLogMarker(string logFilePath, string marker)
    {
        try
        {
            if (!File.Exists(logFilePath))
            {
                return false;
            }

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

    private static string SerializeDomainError(DomainError error) =>
        JsonSerializer.Serialize(new
        {
            success = false,
            error = error.Message,
            detail = error.Detail
        }, JsonOptions);

    private static string SerializeError(string message, string? suggestion = null) =>
        JsonSerializer.Serialize(new
        {
            success = false,
            error = message,
            suggestion
        }, JsonOptions);
}
