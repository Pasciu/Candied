using Candied.Core.Errors;
using Candied.Core.Models;
using Candied.Primitives;

namespace Candied.Core.Ports;

public interface IProjectGenerator
{
    Task<Result<ModProjectManifest, ScaffoldError>> ScaffoldModProjectAsync(
        ModScaffoldSpec spec,
        CancellationToken ct = default);
}
