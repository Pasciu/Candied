using Candied.Core.Errors;
using Candied.Core.Models;
using Candied.Primitives;

namespace Candied.Core.Ports;

public interface IGameDetector
{
    Task<Result<GameEnvironment, DetectionError>> DetectGameAsync(
        string gameDirectoryPath,
        CancellationToken ct = default);
}
