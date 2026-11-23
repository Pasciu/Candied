using Candied.Core.Errors;
using Candied.Primitives;

namespace Candied.Core.Ports;

public record DecompilationOutput(string AssemblyName, string OutputDirectory, int SourceFileCount);

public record DecompileProgress(int PercentComplete, string CurrentItem);

public interface IDecompilerEngine
{
    Task<Result<DecompilationOutput, DecompilerError>> DecompileAssemblyAsync(
        string assemblyPath,
        string outputSourceDirectory,
        IProgress<DecompileProgress>? progress = null,
        CancellationToken ct = default);
}
