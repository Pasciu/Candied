using System;
using System.Threading;
using System.Threading.Tasks;
using Candied.Core.Errors;
using Candied.Core.Models;
using Candied.Core.Ports;
using Candied.Primitives;

namespace Candied.Core.UseCases;

public sealed class StopGameUseCase
{
    private readonly IGameProcessManager _processManager;

    public StopGameUseCase(IGameProcessManager processManager)
    {
        _processManager = processManager ?? throw new ArgumentNullException(nameof(processManager));
    }

    public async Task<Result<Unit, DomainError>> ExecuteAsync(
        GameEnvironment game,
        CancellationToken ct = default)
    {
        if (game == null)
        {
            return Result.Fail<Unit, DomainError>(
                new ProcessManagementError("Game environment cannot be null."));
        }

        var stopResult = await _processManager.StopGameAsync(game, ct).ConfigureAwait(false);
        if (stopResult.IsFailure)
        {
            return Result.Fail<Unit, DomainError>(stopResult.Error);
        }

        return Result.Ok<Unit, DomainError>(new Unit());
    }
}
