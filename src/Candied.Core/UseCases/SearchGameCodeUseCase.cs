using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Candied.Core.Errors;
using Candied.Core.Models;
using Candied.Core.Ports;
using Candied.Primitives;

namespace Candied.Core.UseCases;

public sealed class SearchGameCodeUseCase
{
    private readonly ISymbolIndex _symbolIndex;

    public SearchGameCodeUseCase(ISymbolIndex symbolIndex)
    {
        _symbolIndex = symbolIndex ?? throw new ArgumentNullException(nameof(symbolIndex));
    }

    public async Task<Result<IReadOnlyList<CodeSearchMatch>, IndexError>> ExecuteAsync(
        CodeSearchQuery query,
        string? databasePath = null,
        CancellationToken ct = default)
    {
        if (query == null || string.IsNullOrWhiteSpace(query.SearchTerm))
        {
            return Result.Fail<IReadOnlyList<CodeSearchMatch>, IndexError>(
                new IndexError("Code search term cannot be empty."));
        }

        var normalized = query with
        {
            Limit = Math.Clamp(query.Limit <= 0 ? 25 : query.Limit, 1, 100)
        };

        return await _symbolIndex.SearchCodeAsync(normalized, databasePath, ct).ConfigureAwait(false);
    }
}
