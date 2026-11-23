using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Candied.Core.Errors;
using Candied.Core.Models;
using Candied.Core.Ports;
using Candied.Primitives;

namespace Candied.Core.UseCases;

public sealed class ExtractAssetsUseCase
{
    private readonly IAssetExtractor _assetExtractor;
    private readonly CandiedStorageLayout _storageLayout;

    public ExtractAssetsUseCase(
        IAssetExtractor assetExtractor,
        CandiedStorageLayout storageLayout)
    {
        _assetExtractor = assetExtractor ?? throw new ArgumentNullException(nameof(assetExtractor));
        _storageLayout = storageLayout ?? throw new ArgumentNullException(nameof(storageLayout));
    }

    public async Task<Result<IReadOnlyList<ExtractedAssetInfo>, AssetExtractionError>> ExecuteAsync(
        GameEnvironment game,
        AssetExtractionFilter? filter = null,
        string? targetOutputDirectory = null,
        CancellationToken ct = default)
    {
        if (game == null)
        {
            return Result.Fail<IReadOnlyList<ExtractedAssetInfo>, AssetExtractionError>(
                new AssetExtractionError("Game environment cannot be null."));
        }

        var assetFilter = filter ?? new AssetExtractionFilter();
        var outputDir = string.IsNullOrWhiteSpace(targetOutputDirectory)
            ? _storageLayout.GetAssetsDir(game.GameName)
            : targetOutputDirectory;

        return await _assetExtractor.ExtractAssetsAsync(game, assetFilter, outputDir, ct).ConfigureAwait(false);
    }
}
