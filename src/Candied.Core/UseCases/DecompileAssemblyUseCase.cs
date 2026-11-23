using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Candied.Core.Errors;
using Candied.Core.Models;
using Candied.Core.Ports;
using Candied.Primitives;

namespace Candied.Core.UseCases;

/// <summary>
/// Use case for decompiling a single targeted game assembly (.dll) and incrementally
/// updating the SQLite symbol index without blocking on unneeded assemblies.
/// </summary>
public sealed class DecompileAssemblyUseCase
{
    private readonly IGameDetector _gameDetector;
    private readonly IDecompilerEngine _decompilerEngine;
    private readonly ISymbolIndex _symbolIndex;
    private readonly IDocumentationGenerator? _documentationGenerator;

    public DecompileAssemblyUseCase(
        IGameDetector gameDetector,
        IDecompilerEngine decompilerEngine,
        ISymbolIndex symbolIndex,
        IDocumentationGenerator? documentationGenerator = null)
    {
        _gameDetector = gameDetector;
        _decompilerEngine = decompilerEngine;
        _symbolIndex = symbolIndex;
        _documentationGenerator = documentationGenerator;
    }

    public async Task<Result<DecompilationOutput, DomainError>> ExecuteAsync(
        string gameDirectoryPath,
        string assemblyName,
        CandiedStorageLayout layout,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(assemblyName))
        {
            return Result.Fail<DecompilationOutput, DomainError>(
                new DecompilerError("Assembly name cannot be empty.", string.Empty));
        }

        var detectResult = await _gameDetector.DetectGameAsync(gameDirectoryPath, ct).ConfigureAwait(false);
        if (detectResult.IsFailure)
        {
            return Result.Fail<DecompilationOutput, DomainError>(detectResult.Error);
        }

        var gameEnv = detectResult.Value;

        var normalizedTarget = assemblyName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
            ? assemblyName[..^4]
            : assemblyName;

        var targetAssembly = gameEnv.DiscoveredAssemblies.FirstOrDefault(a =>
        {
            var cleanName = a.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                ? a.Name[..^4]
                : a.Name;
            return string.Equals(cleanName, normalizedTarget, StringComparison.OrdinalIgnoreCase);
        });

        if (targetAssembly == null)
        {
            return Result.Fail<DecompilationOutput, DomainError>(
                new DecompilerError($"Assembly '{assemblyName}' was not found in game Managed assemblies.", assemblyName));
        }

        var decompiledOutputDir = layout.GetDecompiledDir(gameEnv.GameName, normalizedTarget);

        var decompileResult = await _decompilerEngine.DecompileAssemblyAsync(
            targetAssembly.FullPath,
            decompiledOutputDir,
            null,
            ct).ConfigureAwait(false);

        if (decompileResult.IsFailure)
        {
            return Result.Fail<DecompilationOutput, DomainError>(decompileResult.Error);
        }

        var databasePath = layout.GetDatabasePath(gameEnv.GameName);

        // Incrementally index the decompiled source tree for this specific assembly
        var indexResult = await _symbolIndex.IndexSourceTreeAsync(
            decompiledOutputDir,
            normalizedTarget,
            databasePath,
            ct).ConfigureAwait(false);

        if (indexResult.IsFailure)
        {
            return Result.Fail<DecompilationOutput, DomainError>(indexResult.Error);
        }

        // Automatically generate/update Knowledge Hub documentation in Zone 3
        if (_documentationGenerator != null)
        {
            var allSymbolsResult = await _symbolIndex.GetAllSymbolsAsync(databasePath, ct).ConfigureAwait(false);
            if (allSymbolsResult.IsSuccess)
            {
                var docsDir = layout.GetDocsDir(gameEnv.GameName);
                await _documentationGenerator.GenerateKnowledgeHubAsync(
                    gameEnv,
                    allSymbolsResult.Value,
                    docsDir,
                    ct).ConfigureAwait(false);
            }
        }

        return Result.Ok<DecompilationOutput, DomainError>(decompileResult.Value);
    }
}
