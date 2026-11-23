using System.Threading;
using System.Threading.Tasks;
using Candied.Core.Errors;
using Candied.Core.Models;
using Candied.Primitives;

namespace Candied.Core.Ports;

public interface IProjectReferenceEditor
{
    Task<Result<AddReferenceResult, ScaffoldError>> AddManagedReferenceAsync(
        string projectFilePath,
        string assemblyName,
        bool publicize = false,
        string? gameDirectory = null,
        CancellationToken ct = default);
}
