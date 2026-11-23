using Candied.Core.Errors;
using Candied.Core.Models;
using Candied.Primitives;

namespace Candied.Core.Ports;

public interface ISymbolIndex
{
    Task<Result<Unit, IndexError>> IndexSourceTreeAsync(
        string sourceDirectory,
        string? assemblyName = null,
        string? databasePath = null,
        CancellationToken ct = default);

    Task<Result<IReadOnlyList<SymbolSummary>, IndexError>> SearchSymbolsAsync(
        SymbolQuery query,
        string? databasePath = null,
        CancellationToken ct = default);

    Task<Result<IReadOnlyList<SymbolSummary>, IndexError>> GetAllSymbolsAsync(
        string? databasePath = null,
        CancellationToken ct = default);

    Task<Result<TypeDefinitionDetail, IndexError>> GetTypeDetailAsync(
        string fullTypeName,
        string? databasePath = null,
        CancellationToken ct = default);

    Task<Result<IReadOnlyList<CodeSearchMatch>, IndexError>> SearchCodeAsync(
        CodeSearchQuery query,
        string? databasePath = null,
        CancellationToken ct = default);
}
