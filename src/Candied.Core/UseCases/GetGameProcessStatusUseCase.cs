using System;
using System.Threading;
using System.Threading.Tasks;
using Candied.Core.Errors;
using Candied.Core.Models;
using Candied.Core.Ports;
using Candied.Primitives;

namespace Candied.Core.UseCases;

public sealed class GetGameProcessStatusUseCase
{
    private readonly IGameProcessManager _processManager;

    public GetGameProcessStatusUseCase(IGameProcessManager processManager)
    {
        _processManager = processManager ?? throw new ArgumentNullException(nameof(processManager));
    }

    public async Task<Result<GameProcessInfo?, DomainError>> ExecuteAsync(
        GameEnvironment game,
        CancellationToken ct = default)
    {
        if (game == null)
        {
            return Result.Fail<GameProcessInfo?, DomainError>(
                new ProcessManagementError("Game environment cannot be null."));
        }

        var processResult = await _processManager.GetRunningGameAsync(game, ct).ConfigureAwait(false);
        if (processResult.IsFailure)
        {
            return Result.Fail<GameProcessInfo?, DomainError>(processResult.Error);
        }

        return Result.Ok<GameProcessInfo?, DomainError>(processResult.Value);
    }
}
