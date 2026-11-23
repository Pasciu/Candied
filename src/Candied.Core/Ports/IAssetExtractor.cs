using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Candied.Core.Errors;
using Candied.Core.Models;
using Candied.Primitives;

namespace Candied.Core.Ports;

public interface IAssetExtractor
{
    Task<Result<IReadOnlyList<DiscoveredAssetInfo>, AssetExtractionError>> DiscoverAssetsAsync(
        GameEnvironment game,
        AssetDiscoveryFilter filter,
        CancellationToken ct = default);

    Task<Result<IReadOnlyList<ExtractedAssetInfo>, AssetExtractionError>> ExtractAssetsAsync(
        GameEnvironment game,
        AssetExtractionFilter filter,
        string outputDirectory,
        CancellationToken ct = default);
}
