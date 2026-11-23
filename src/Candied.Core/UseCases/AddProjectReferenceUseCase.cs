using System;
using System.Threading;
using System.Threading.Tasks;
using Candied.Core.Errors;
using Candied.Core.Models;
using Candied.Core.Ports;
using Candied.Primitives;

namespace Candied.Core.UseCases;

public sealed class AddProjectReferenceUseCase
{
    private readonly IProjectReferenceEditor _referenceEditor;

    public AddProjectReferenceUseCase(IProjectReferenceEditor referenceEditor)
    {
        _referenceEditor = referenceEditor ?? throw new ArgumentNullException(nameof(referenceEditor));
    }

    public async Task<Result<AddReferenceResult, ScaffoldError>> ExecuteAsync(
        string projectFilePath,
        string assemblyName,
        bool publicize = false,
        string? gameDirectory = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(projectFilePath))
        {
            return Result.Fail<AddReferenceResult, ScaffoldError>(
                new ScaffoldError("Project file path cannot be empty."));
        }

        if (string.IsNullOrWhiteSpace(assemblyName))
        {
            return Result.Fail<AddReferenceResult, ScaffoldError>(
                new ScaffoldError("Assembly name cannot be empty."));
        }

        return await _referenceEditor.AddManagedReferenceAsync(
            projectFilePath,
            assemblyName,
            publicize,
            gameDirectory,
            ct).ConfigureAwait(false);
    }
}
