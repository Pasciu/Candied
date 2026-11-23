using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Candied.Core.Errors;
using Candied.Core.Models;
using Candied.Core.Ports;
using Candied.Primitives;

namespace Candied.Core.UseCases;

/// <summary>
/// Use case for querying symbol signatures (classes, methods, fields) against the active SQLite index.
/// </summary>
public sealed class SearchGameSymbolsUseCase
{
    private readonly ISymbolIndex _symbolIndex;

    public SearchGameSymbolsUseCase(ISymbolIndex symbolIndex)
    {
        _symbolIndex = symbolIndex;
    }

    public async Task<Result<IReadOnlyList<SymbolSummary>, DomainError>> ExecuteAsync(
        SymbolQuery query,
        string? databasePath = null,
        CancellationToken ct = default)
    {
        var searchResult = await _symbolIndex.SearchSymbolsAsync(query, databasePath, ct).ConfigureAwait(false);
        if (searchResult.IsFailure)
        {
            return Result.Fail<IReadOnlyList<SymbolSummary>, DomainError>(searchResult.Error);
        }

        return Result.Ok<IReadOnlyList<SymbolSummary>, DomainError>(searchResult.Value);
    }
}
