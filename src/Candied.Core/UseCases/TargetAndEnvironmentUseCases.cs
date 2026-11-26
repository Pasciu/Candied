using System;
using System.Threading;
using System.Threading.Tasks;
using Candied.Core.Errors;
using Candied.Core.Models;
using Candied.Core.Ports;
using Candied.Primitives;

namespace Candied.Core.UseCases;

public sealed class InspectTargetUseCase(ITargetPlatformRegistry platformRegistry)
{
    public async Task<Result<TargetDescriptor, DomainError>> ExecuteAsync(
        string targetPath,
        string? runnerPath,
        CandiedStorageLayout storageLayout,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(targetPath))
        {
            return Result.Fail<TargetDescriptor, DomainError>(new DetectionError("Target path cannot be empty."));
        }

        var result = await platformRegistry.InspectTargetAsync(targetPath, runnerPath, storageLayout, ct).ConfigureAwait(false);
        return result.IsSuccess
            ? Result.Ok<TargetDescriptor, DomainError>(result.Value)
            : Result.Fail<TargetDescriptor, DomainError>(result.Error);
    }
}

public sealed class SetupTargetEnvironmentUseCase(ITargetPlatformRegistry platformRegistry)
{
    public async Task<Result<ProvisioningReport, DomainError>> ExecuteAsync(
        string targetPath,
        string? runnerPath,
        ProvisioningSpec spec,
        CandiedStorageLayout storageLayout,
        CancellationToken ct = default)
    {
        var inspectRes = await platformRegistry.InspectTargetAsync(targetPath, runnerPath, storageLayout, ct).ConfigureAwait(false);
        if (inspectRes.IsFailure)
        {
            return Result.Fail<ProvisioningReport, DomainError>(inspectRes.Error);
        }

        var target = inspectRes.Value;
        var platform = platformRegistry.GetPlatformById(target.PlatformId) ?? platformRegistry.ResolvePlatform(targetPath, runnerPath);
        if (platform.EnvironmentSetup == null)
        {
            return Result.Fail<ProvisioningReport, DomainError>(
                new UnsupportedCapabilityError(platform.PlatformId, nameof(PlatformCapability.EnvironmentSetup)));
        }

        var provRes = await platform.EnvironmentSetup.ProvisionAsync(target, spec, storageLayout, ct).ConfigureAwait(false);
        return provRes.IsSuccess
            ? Result.Ok<ProvisioningReport, DomainError>(provRes.Value)
            : Result.Fail<ProvisioningReport, DomainError>(provRes.Error);
    }
}

public sealed class RollbackTargetEnvironmentUseCase(ITargetPlatformRegistry platformRegistry)
{
    public async Task<Result<RollbackReport, DomainError>> ExecuteAsync(
        string targetPath,
        string? runnerPath,
        CandiedStorageLayout storageLayout,
        CancellationToken ct = default)
    {
        var inspectRes = await platformRegistry.InspectTargetAsync(targetPath, runnerPath, storageLayout, ct).ConfigureAwait(false);
        if (inspectRes.IsFailure)
        {
            return Result.Fail<RollbackReport, DomainError>(inspectRes.Error);
        }

        var target = inspectRes.Value;
        var platform = platformRegistry.GetPlatformById(target.PlatformId) ?? platformRegistry.ResolvePlatform(targetPath, runnerPath);
        if (platform.EnvironmentSetup == null)
        {
            return Result.Fail<RollbackReport, DomainError>(
                new UnsupportedCapabilityError(platform.PlatformId, nameof(PlatformCapability.EnvironmentSetup)));
        }

        var rbRes = await platform.EnvironmentSetup.RollbackAsync(target, storageLayout, ct).ConfigureAwait(false);
        return rbRes.IsSuccess
            ? Result.Ok<RollbackReport, DomainError>(rbRes.Value)
            : Result.Fail<RollbackReport, DomainError>(rbRes.Error);
    }
}

public sealed class SaveResearchNotesUseCase(
    ITargetPlatformRegistry platformRegistry,
    IDocumentationGenerator documentationGenerator)
{
    public async Task<Result<string, DomainError>> ExecuteAsync(
        string targetPath,
        string? runnerPath,
        string notesMarkdown,
        CandiedStorageLayout storageLayout,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(notesMarkdown))
        {
            return Result.Fail<string, DomainError>(new ScaffoldError("Research notes content cannot be empty."));
        }

        var inspectRes = await platformRegistry.InspectTargetAsync(targetPath, runnerPath, storageLayout, ct).ConfigureAwait(false);
        if (inspectRes.IsFailure)
        {
            return Result.Fail<string, DomainError>(inspectRes.Error);
        }

        var target = inspectRes.Value;
        string docsDir = storageLayout.GetDocsDir(target.Name);
        var saveRes = await documentationGenerator.SaveResearchNotesAsync(target.Name, notesMarkdown, docsDir, ct).ConfigureAwait(false);
        if (saveRes.IsFailure)
        {
            return Result.Fail<string, DomainError>(saveRes.Error);
        }

        return Result.Ok<string, DomainError>(storageLayout.GetResearchNotesPath(target.Name));
    }
}

public sealed class ManageTargetProcessUseCase(
    ITargetPlatformRegistry platformRegistry,
    ITargetProcessManager processManager)
{
    public async Task<Result<TargetProcessInfo?, DomainError>> GetStatusAsync(
        string targetPath,
        string? runnerPath,
        CandiedStorageLayout storageLayout,
        CancellationToken ct = default)
    {
        var inspectRes = await platformRegistry.InspectTargetAsync(targetPath, runnerPath, storageLayout, ct).ConfigureAwait(false);
        if (inspectRes.IsFailure)
        {
            return Result.Fail<TargetProcessInfo?, DomainError>(inspectRes.Error);
        }

        var statusRes = await processManager.GetRunningTargetAsync(inspectRes.Value, ct).ConfigureAwait(false);
        return statusRes.IsSuccess
            ? Result.Ok<TargetProcessInfo?, DomainError>(statusRes.Value)
            : Result.Fail<TargetProcessInfo?, DomainError>(statusRes.Error);
    }

    public async Task<Result<TargetProcessInfo, DomainError>> LaunchAsync(
        string targetPath,
        string? runnerPath,
        TargetLaunchOptions options,
        CandiedStorageLayout storageLayout,
        CancellationToken ct = default)
    {
        var inspectRes = await platformRegistry.InspectTargetAsync(targetPath, runnerPath, storageLayout, ct).ConfigureAwait(false);
        if (inspectRes.IsFailure)
        {
            return Result.Fail<TargetProcessInfo, DomainError>(inspectRes.Error);
        }

        var launchRes = await processManager.LaunchTargetAsync(inspectRes.Value, options, ct).ConfigureAwait(false);
        return launchRes.IsSuccess
            ? Result.Ok<TargetProcessInfo, DomainError>(launchRes.Value)
            : Result.Fail<TargetProcessInfo, DomainError>(launchRes.Error);
    }

    public async Task<Result<Unit, DomainError>> StopAsync(
        string targetPath,
        string? runnerPath,
        CandiedStorageLayout storageLayout,
        CancellationToken ct = default)
    {
        var inspectRes = await platformRegistry.InspectTargetAsync(targetPath, runnerPath, storageLayout, ct).ConfigureAwait(false);
        if (inspectRes.IsFailure)
        {
            return Result.Fail<Unit, DomainError>(inspectRes.Error);
        }

        var stopRes = await processManager.StopTargetAsync(inspectRes.Value, ct).ConfigureAwait(false);
        return stopRes.IsSuccess
            ? Result.Ok<Unit, DomainError>(stopRes.Value)
            : Result.Fail<Unit, DomainError>(stopRes.Error);
    }
}
