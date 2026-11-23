using Candied.Core.Errors;
using Candied.Core.Models;
using Candied.Primitives;

namespace Candied.Core.Ports;

public interface IBuildRunner
{
    Task<Result<BuildReport, BuildExecutionError>> BuildModAsync(
        string projectFilePath,
        string configuration = "Debug",
        CancellationToken ct = default);
}
