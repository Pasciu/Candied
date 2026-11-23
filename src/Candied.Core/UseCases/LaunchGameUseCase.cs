using System;
using System.Threading;
using System.Threading.Tasks;
using Candied.Core.Errors;
using Candied.Core.Models;
using Candied.Core.Ports;
using Candied.Primitives;

namespace Candied.Core.UseCases;

public sealed class LaunchGameUseCase
{
    private readonly IGameProcessManager _processManager;

    public LaunchGameUseCase(IGameProcessManager processManager)
    {
        _processManager = processManager ?? throw new ArgumentNullException(nameof(processManager));
    }

    public async Task<Result<GameProcessInfo, DomainError>> ExecuteAsync(
        GameEnvironment game,
        GameLaunchOptions? options = null,
        CancellationToken ct = default)
    {
        if (game == null)
        {
            return Result.Fail<GameProcessInfo, DomainError>(
                new ProcessManagementError("Game environment cannot be null."));
        }

        // Attach-first: check if already running
        var runningResult = await _processManager.GetRunningGameAsync(game, ct).ConfigureAwait(false);
        if (runningResult.IsSuccess && runningResult.Value != null)
        {
            return Result.Ok<GameProcessInfo, DomainError>(runningResult.Value);
        }

        var launchOptions = options ?? new GameLaunchOptions();
        var launchResult = await _processManager.LaunchGameAsync(game, launchOptions, ct).ConfigureAwait(false);
        if (launchResult.IsFailure)
        {
            return Result.Fail<GameProcessInfo, DomainError>(launchResult.Error);
        }

        return Result.Ok<GameProcessInfo, DomainError>(launchResult.Value);
    }
}
