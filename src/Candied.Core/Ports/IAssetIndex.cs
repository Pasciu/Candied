using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Candied.Core.Errors;
using Candied.Core.Models;
using Candied.Primitives;

namespace Candied.Core.Ports;

public interface IAssetIndex
{
    Task<Result<int, IndexError>> IndexAssetsAsync(
        GameEnvironment game,
        IReadOnlyList<DiscoveredAssetInfo> assets,
        string? databasePath = null,
        CancellationToken ct = default);

    Task<Result<IReadOnlyList<DiscoveredAssetInfo>, IndexError>> SearchAssetsAsync(
        AssetDiscoveryFilter filter,
        string? databasePath = null,
        CancellationToken ct = default);

    Task<Result<AssetDiscoveryPage, IndexError>> SearchAssetsPageAsync(
        AssetDiscoveryFilter filter,
        string? databasePath = null,
        CancellationToken ct = default);

    Task<Result<bool, IndexError>> HasIndexedAssetsAsync(
        string? databasePath = null,
        CancellationToken ct = default);
}
