using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Candied.Core.Models;
using Candied.Primitives;

namespace Candied.Core.Ports;

/// <summary>
/// Port managing user-registered games allowlist outside the repository.
/// </summary>
public interface IRegisteredGameRegistry
{
    Task<IReadOnlyList<RegisteredGameInfo>> ListRegisteredGamesAsync(CancellationToken ct = default);
    Task<Result<RegisteredGameInfo, string>> GetRegisteredGameAsync(string name, CancellationToken ct = default);
    Task<Result<RegisteredGameInfo, string>> RegisterGameAsync(string name, string executablePath, CancellationToken ct = default);
    Task<Result<bool, string>> UnregisterGameAsync(string name, CancellationToken ct = default);
    Task<Result<bool, string>> UpdateBridgeStatusAsync(string name, bool hasBridgeInstalled, CancellationToken ct = default);
}
