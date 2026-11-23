using Candied.Core.Errors;
using Candied.Core.Models;
using Candied.Core.Ports;
using Candied.Primitives;

namespace Candied.Core.UseCases;

public sealed class GetTypeDetailUseCase
{
    private readonly ISymbolIndex _symbolIndex;

    public GetTypeDetailUseCase(ISymbolIndex symbolIndex)
    {
        _symbolIndex = symbolIndex;
    }

    public Task<Result<TypeDefinitionDetail, DomainError>> ExecuteAsync(
        string fullTypeName,
        CancellationToken ct) =>
        ExecuteAsync(fullTypeName, null, ct);

    public async Task<Result<TypeDefinitionDetail, DomainError>> ExecuteAsync(
        string fullTypeName,
        string? databasePath = null,
        CancellationToken ct = default)
    {
        var result = await _symbolIndex.GetTypeDetailAsync(fullTypeName, databasePath, ct).ConfigureAwait(false);
        if (result.IsFailure)
        {
            return Result.Fail<TypeDefinitionDetail, DomainError>(result.Error);
        }

        return Result.Ok<TypeDefinitionDetail, DomainError>(result.Value);
    }
}
