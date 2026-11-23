using System.Threading;
using System.Threading.Tasks;
using Candied.Core.Errors;
using Candied.Core.Models;
using Candied.Primitives;

namespace Candied.Core.Ports;

public interface IGameProcessManager
{
    Task<Result<GameProcessInfo?, ProcessManagementError>> GetRunningGameAsync(
        GameEnvironment game,
        CancellationToken ct = default);

    Task<Result<GameProcessInfo, ProcessManagementError>> LaunchGameAsync(
        GameEnvironment game,
        GameLaunchOptions options,
        CancellationToken ct = default);

    Task<Result<Unit, ProcessManagementError>> StopGameAsync(
        GameEnvironment game,
        CancellationToken ct = default);
}
