using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Candied.Core.Errors;
using Candied.Core.Models;
using Candied.Core.Ports;
using Candied.Primitives;

namespace Candied.Core.UseCases;

public sealed class ExtractCodeModuleUseCase(
    ITargetPlatformRegistry platformRegistry,
    ISymbolIndex symbolIndex,
    IDocumentationGenerator? documentationGenerator = null)
{
    public async Task<Result<CodeExtractionOutput, DomainError>> ExecuteAsync(
        string targetPath,
        string? runnerPath,
        string moduleName,
        CandiedStorageLayout storageLayout,
        IProgress<CodeExtractionProgress>? progress = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(moduleName))
        {
            return Result.Fail<CodeExtractionOutput, DomainError>(new CodeExtractionError("Module name cannot be empty."));
        }

        var inspectRes = await platformRegistry.InspectTargetAsync(targetPath, runnerPath, storageLayout, ct).ConfigureAwait(false);
        if (inspectRes.IsFailure)
        {
            return Result.Fail<CodeExtractionOutput, DomainError>(inspectRes.Error);
        }

        var target = inspectRes.Value;
        var platform = platformRegistry.GetPlatformById(target.PlatformId) ?? platformRegistry.ResolvePlatform(targetPath, runnerPath);
        if (platform.CodeKnowledge == null)
        {
            return Result.Fail<CodeExtractionOutput, DomainError>(
                new UnsupportedCapabilityError(platform.PlatformId, nameof(PlatformCapability.CodeKnowledge)));
        }

        var extractRes = await platform.CodeKnowledge.ExtractAndIndexModuleAsync(
            target,
            moduleName,
            storageLayout,
            symbolIndex,
            progress,
            ct).ConfigureAwait(false);

        if (extractRes.IsFailure)
        {
            return Result.Fail<CodeExtractionOutput, DomainError>(extractRes.Error);
        }

        if (documentationGenerator != null)
        {
            string dbPath = storageLayout.GetDatabasePath(target.Name);
            var allSymbolsRes = await symbolIndex.GetAllSymbolsAsync(dbPath, ct).ConfigureAwait(false);
            if (allSymbolsRes.IsSuccess)
            {
                string docsDir = storageLayout.GetDocsDir(target.Name);
                await documentationGenerator.GenerateKnowledgeHubAsync(target, allSymbolsRes.Value, docsDir, ct).ConfigureAwait(false);
            }
        }

        return Result.Ok<CodeExtractionOutput, DomainError>(extractRes.Value);
    }
}

public sealed class SearchSymbolsUseCase(ISymbolIndex symbolIndex)
{
    public async Task<Result<IReadOnlyList<SymbolSummary>, DomainError>> ExecuteAsync(
        SymbolQuery query,
        string? databasePath = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var res = await symbolIndex.SearchSymbolsAsync(query, databasePath, ct).ConfigureAwait(false);
        return res.IsSuccess
            ? Result.Ok<IReadOnlyList<SymbolSummary>, DomainError>(res.Value)
            : Result.Fail<IReadOnlyList<SymbolSummary>, DomainError>(res.Error);
    }
}

public sealed class SearchCodeUseCase(ISymbolIndex symbolIndex)
{
    public async Task<Result<IReadOnlyList<CodeSearchMatch>, DomainError>> ExecuteAsync(
        CodeSearchQuery query,
        string? databasePath = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (string.IsNullOrWhiteSpace(query.SearchTerm))
        {
            return Result.Fail<IReadOnlyList<CodeSearchMatch>, DomainError>(new IndexError("Search pattern cannot be empty."));
        }

        var res = await symbolIndex.SearchCodeAsync(query, databasePath, ct).ConfigureAwait(false);
        return res.IsSuccess
            ? Result.Ok<IReadOnlyList<CodeSearchMatch>, DomainError>(res.Value)
            : Result.Fail<IReadOnlyList<CodeSearchMatch>, DomainError>(res.Error);
    }
}

public sealed class GetTypeDetailUseCase(ISymbolIndex symbolIndex)
{
    public async Task<Result<TypeDefinitionDetail, DomainError>> ExecuteAsync(
        string fullTypeName,
        string? databasePath = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(fullTypeName))
        {
            return Result.Fail<TypeDefinitionDetail, DomainError>(new IndexError("Full type name cannot be empty."));
        }

        var res = await symbolIndex.GetTypeDetailAsync(fullTypeName, databasePath, ct).ConfigureAwait(false);
        return res.IsSuccess
            ? Result.Ok<TypeDefinitionDetail, DomainError>(res.Value)
            : Result.Fail<TypeDefinitionDetail, DomainError>(res.Error);
    }
}
