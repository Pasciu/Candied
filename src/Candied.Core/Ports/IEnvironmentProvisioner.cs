using System.Threading;
using System.Threading.Tasks;
using Candied.Core.Errors;
using Candied.Core.Models;
using Candied.Primitives;

namespace Candied.Core.Ports;

/// <summary>
/// Port for provisioning and configuring the game modding environment (BepInEx installation,
/// configuration file generation, file manifest tracking, and rollback).
/// </summary>
public interface IEnvironmentProvisioner
{
    Task<Result<ProvisioningReport, ProvisioningError>> ProvisionEnvironmentAsync(
        GameEnvironment gameEnv,
        ProvisioningSpec spec,
        CandiedStorageLayout storageLayout,
        CancellationToken ct = default);

    Task<Result<RollbackReport, ProvisioningError>> RollbackEnvironmentAsync(
        GameEnvironment gameEnv,
        CandiedStorageLayout storageLayout,
        CancellationToken ct = default);
}
