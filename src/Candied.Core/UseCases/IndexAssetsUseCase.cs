using System;
using System.Threading;
using System.Threading.Tasks;
using Candied.Core.Errors;
using Candied.Core.Models;
using Candied.Core.Ports;
using Candied.Primitives;

namespace Candied.Core.UseCases;

public sealed class IndexAssetsUseCase
{
    private readonly IAssetExtractor _extractor;
    private readonly IAssetIndex _index;

    public IndexAssetsUseCase(IAssetExtractor extractor, IAssetIndex index)
    {
        _extractor = extractor ?? throw new ArgumentNullException(nameof(extractor));
        _index = index ?? throw new ArgumentNullException(nameof(index));
    }

    public async Task<Result<int, DomainError>> ExecuteAsync(
        GameEnvironment game,
        string? databasePath = null,
        CancellationToken ct = default)
    {
        if (game == null)
        {
            return Result.Fail<int, DomainError>(new AssetExtractionError("Target game environment cannot be null."));
        }

        var filter = new AssetDiscoveryFilter(
            SearchPattern: null,
            AssetType: null,
            MaxResults: int.MaxValue);

        var discoverResult = await _extractor.DiscoverAssetsAsync(game, filter, ct).ConfigureAwait(false);
        if (discoverResult.IsFailure)
        {
            return Result.Fail<int, DomainError>(discoverResult.Error);
        }

        var indexResult = await _index.IndexAssetsAsync(game, discoverResult.Value, databasePath, ct).ConfigureAwait(false);
        if (indexResult.IsFailure)
        {
            return Result.Fail<int, DomainError>(indexResult.Error);
        }

        return Result.Ok<int, DomainError>(indexResult.Value);
    }
}
