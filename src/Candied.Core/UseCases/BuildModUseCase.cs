using Candied.Core.Errors;
using Candied.Core.Models;
using Candied.Core.Ports;
using Candied.Primitives;

namespace Candied.Core.UseCases;

public sealed class BuildModUseCase
{
    private readonly IBuildRunner _buildRunner;

    public BuildModUseCase(IBuildRunner buildRunner)
    {
        _buildRunner = buildRunner;
    }

    public async Task<Result<BuildReport, DomainError>> ExecuteAsync(
        string projectFilePath,
        string configuration = "Debug",
        CancellationToken ct = default)
    {
        var result = await _buildRunner.BuildModAsync(projectFilePath, configuration, ct).ConfigureAwait(false);
        if (result.IsFailure)
        {
            return Result.Fail<BuildReport, DomainError>(result.Error);
        }

        return Result.Ok<BuildReport, DomainError>(result.Value);
    }
}
