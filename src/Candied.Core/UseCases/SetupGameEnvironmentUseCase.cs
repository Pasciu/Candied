using System.Threading;
using System.Threading.Tasks;
using Candied.Core.Errors;
using Candied.Core.Models;
using Candied.Core.Ports;
using Candied.Primitives;

namespace Candied.Core.UseCases;

/// <summary>
/// Use case for automatically setting up the target game modding environment:
/// audits the game, selects the appropriate BepInEx package, provisions the files,
/// configures logging, and generates an installed file manifest for safety.
/// </summary>
public sealed class SetupGameEnvironmentUseCase
{
    private readonly IGameDetector _gameDetector;
    private readonly IEnvironmentProvisioner _provisioner;

    public SetupGameEnvironmentUseCase(
        IGameDetector gameDetector,
        IEnvironmentProvisioner provisioner)
    {
        _gameDetector = gameDetector;
        _provisioner = provisioner;
    }

    public async Task<Result<ProvisioningReport, DomainError>> ExecuteAsync(
        string gameDirectoryPath,
        ProvisioningSpec spec,
        CandiedStorageLayout storageLayout,
        CancellationToken ct = default)
    {
        var detectResult = await _gameDetector.DetectGameAsync(gameDirectoryPath, ct).ConfigureAwait(false);
        if (detectResult.IsFailure)
        {
            return Result.Fail<ProvisioningReport, DomainError>(detectResult.Error);
        }

        var provisionResult = await _provisioner.ProvisionEnvironmentAsync(
            detectResult.Value,
            spec,
            storageLayout,
            ct).ConfigureAwait(false);

        if (provisionResult.IsFailure)
        {
            return Result.Fail<ProvisioningReport, DomainError>(provisionResult.Error);
        }

        return Result.Ok<ProvisioningReport, DomainError>(provisionResult.Value);
    }
}
