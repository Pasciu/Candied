namespace Candied.Decompiler.Internal;

internal sealed record DecompileCacheData(
    string AssemblyName,
    string AssemblyPath,
    long AssemblySizeBytes,
    long AssemblyLastWriteTimeUtcTicks,
    int SourceFileCount);
