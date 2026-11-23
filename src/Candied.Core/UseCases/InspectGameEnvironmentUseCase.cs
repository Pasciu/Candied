using System.Threading;
using System.Threading.Tasks;
using Candied.Core.Errors;
using Candied.Core.Models;
using Candied.Core.Ports;
using Candied.Primitives;

namespace Candied.Core.UseCases;

/// <summary>
/// Use case for performing a non-destructive audit of a Unity game installation.
/// Discovers runtime architecture, BepInEx installation status, .NET target framework,
/// and categorizes discovered assemblies without performing any decompilation.
/// </summary>
public sealed class InspectGameEnvironmentUseCase
{
    private readonly IGameDetector _gameDetector;

    public InspectGameEnvironmentUseCase(IGameDetector gameDetector)
    {
        _gameDetector = gameDetector;
    }

    public async Task<Result<GameEnvironment, DomainError>> ExecuteAsync(
        string gameDirectoryPath,
        CancellationToken ct = default)
    {
        var detectResult = await _gameDetector.DetectGameAsync(gameDirectoryPath, ct).ConfigureAwait(false);
        if (detectResult.IsFailure)
        {
            return Result.Fail<GameEnvironment, DomainError>(detectResult.Error);
        }

        return Result.Ok<GameEnvironment, DomainError>(detectResult.Value);
    }
}
