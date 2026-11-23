using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Candied.Core.Errors;
using Candied.Core.Models;
using Candied.Core.Ports;
using Candied.Primitives;

namespace Candied.Core.UseCases;

public sealed class DiscoverAssetsUseCase
{
    private readonly IAssetExtractor _assetExtractor;
    private readonly IAssetIndex? _assetIndex;

    public DiscoverAssetsUseCase(IAssetExtractor assetExtractor, IAssetIndex? assetIndex = null)
    {
        _assetExtractor = assetExtractor ?? throw new ArgumentNullException(nameof(assetExtractor));
        _assetIndex = assetIndex;
    }

    public async Task<Result<IReadOnlyList<DiscoveredAssetInfo>, AssetExtractionError>> ExecuteAsync(
        GameEnvironment game,
        AssetDiscoveryFilter? filter = null,
        string? databasePath = null,
        CancellationToken ct = default)
    {
        var page = await ExecutePageAsync(game, filter, databasePath, ct).ConfigureAwait(false);
        return page.IsSuccess
            ? Result.Ok<IReadOnlyList<DiscoveredAssetInfo>, AssetExtractionError>(page.Value.Assets)
            : Result.Fail<IReadOnlyList<DiscoveredAssetInfo>, AssetExtractionError>(page.Error);
    }

    public async Task<Result<AssetDiscoveryPage, AssetExtractionError>> ExecutePageAsync(
        GameEnvironment game,
        AssetDiscoveryFilter? filter = null,
        string? databasePath = null,
        CancellationToken ct = default)
    {
        if (game == null)
        {
            return Result.Fail<AssetDiscoveryPage, AssetExtractionError>(
                new AssetExtractionError("Game environment cannot be null."));
        }

        var assetFilter = filter ?? new AssetDiscoveryFilter();
        int safeOffset = Math.Max(0, assetFilter.Offset);
        int safeLimit = assetFilter.MaxResults <= 0 ? 50 : assetFilter.MaxResults;
        assetFilter = assetFilter with { Offset = safeOffset, MaxResults = safeLimit };

        // 1. If an asset index is available, try fast SQLite search
        if (_assetIndex != null)
        {
            var hasIndexed = await _assetIndex.HasIndexedAssetsAsync(databasePath, ct).ConfigureAwait(false);
            if (hasIndexed.IsSuccess && hasIndexed.Value)
            {
                var searchResult = await _assetIndex.SearchAssetsPageAsync(assetFilter, databasePath, ct).ConfigureAwait(false);
                if (searchResult.IsSuccess)
                {
                    return Result.Ok<AssetDiscoveryPage, AssetExtractionError>(searchResult.Value);
                }
            }

            // 2. If not indexed yet, perform full scan and populate index
            var fullScanFilter = new AssetDiscoveryFilter(MaxResults: int.MaxValue);
            var scanResult = await _assetExtractor.DiscoverAssetsAsync(game, fullScanFilter, ct).ConfigureAwait(false);
            if (scanResult.IsSuccess)
            {
                _ = await _assetIndex.IndexAssetsAsync(game, scanResult.Value, databasePath, ct).ConfigureAwait(false);

                // Return filtered page from scan
                var page = FilterAssetsInMemory(scanResult.Value, assetFilter);
                return Result.Ok<AssetDiscoveryPage, AssetExtractionError>(page);
            }
        }

        var directRes = await _assetExtractor.DiscoverAssetsAsync(game, assetFilter, ct).ConfigureAwait(false);
        return directRes.IsSuccess
            ? Result.Ok<AssetDiscoveryPage, AssetExtractionError>(new AssetDiscoveryPage(directRes.Value.Count, directRes.Value))
            : Result.Fail<AssetDiscoveryPage, AssetExtractionError>(directRes.Error);
    }

    private static AssetDiscoveryPage FilterAssetsInMemory(
        IReadOnlyList<DiscoveredAssetInfo> assets,
        AssetDiscoveryFilter filter)
    {
        var query = assets.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(filter.AssetType))
        {
            query = query.Where(a => string.Equals(a.AssetType, filter.AssetType, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(filter.ContainerName))
        {
            query = query.Where(a => a.SourceContainer.Contains(filter.ContainerName, StringComparison.OrdinalIgnoreCase));
        }

        if (filter.PathId.HasValue)
        {
            query = query.Where(a => a.PathId == filter.PathId.Value);
        }

        if (!string.IsNullOrWhiteSpace(filter.SearchPattern) && filter.SearchPattern != "*")
        {
            query = query.Where(a => a.Name.Contains(filter.SearchPattern, StringComparison.OrdinalIgnoreCase));
        }

        var matched = query.ToList();
        var pageItems = matched.Skip(Math.Max(0, filter.Offset)).Take(filter.MaxResults).ToList();
        return new AssetDiscoveryPage(matched.Count, pageItems);
    }
}
