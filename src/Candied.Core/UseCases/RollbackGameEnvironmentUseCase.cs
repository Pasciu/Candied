using System.Threading;
using System.Threading.Tasks;
using Candied.Core.Errors;
using Candied.Core.Models;
using Candied.Core.Ports;
using Candied.Primitives;

namespace Candied.Core.UseCases;

/// <summary>
/// Use case for restoring a game installation to its original state by removing
/// all files tracked in the Candied provisioning manifest.
/// </summary>
public sealed class RollbackGameEnvironmentUseCase
{
    private readonly IGameDetector _gameDetector;
    private readonly IEnvironmentProvisioner _provisioner;

    public RollbackGameEnvironmentUseCase(
        IGameDetector gameDetector,
        IEnvironmentProvisioner provisioner)
    {
        _gameDetector = gameDetector;
        _provisioner = provisioner;
    }

    public async Task<Result<RollbackReport, DomainError>> ExecuteAsync(
        string gameDirectoryPath,
        CandiedStorageLayout storageLayout,
        CancellationToken ct = default)
    {
        var detectResult = await _gameDetector.DetectGameAsync(gameDirectoryPath, ct).ConfigureAwait(false);
        if (detectResult.IsFailure)
        {
            return Result.Fail<RollbackReport, DomainError>(detectResult.Error);
        }

        var rollbackResult = await _provisioner.RollbackEnvironmentAsync(
            detectResult.Value,
            storageLayout,
            ct).ConfigureAwait(false);

        if (rollbackResult.IsFailure)
        {
            return Result.Fail<RollbackReport, DomainError>(rollbackResult.Error);
        }

        return Result.Ok<RollbackReport, DomainError>(rollbackResult.Value);
    }
}
