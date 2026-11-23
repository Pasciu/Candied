using Candied.Core.Errors;
using Candied.Core.Models;
using Candied.Core.Ports;
using Candied.Primitives;

namespace Candied.Core.UseCases;

public sealed class ScaffoldModUseCase
{
    private readonly IProjectGenerator _projectGenerator;

    public ScaffoldModUseCase(IProjectGenerator projectGenerator)
    {
        _projectGenerator = projectGenerator;
    }

    public async Task<Result<ModProjectManifest, DomainError>> ExecuteAsync(
        ModScaffoldSpec spec,
        CancellationToken ct = default)
    {
        var result = await _projectGenerator.ScaffoldModProjectAsync(spec, ct).ConfigureAwait(false);
        if (result.IsFailure)
        {
            return Result.Fail<ModProjectManifest, DomainError>(result.Error);
        }

        return Result.Ok<ModProjectManifest, DomainError>(result.Value);
    }
}
