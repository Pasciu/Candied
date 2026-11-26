using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.CSharp.ProjectDecompiler;
using ICSharpCode.Decompiler.Metadata;
using Candied.Core.Capabilities;
using Candied.Core.Errors;
using Candied.Core.Models;
using Candied.Core.Ports;
using Candied.Primitives;

namespace Candied.Platforms.Unity.Mono.Capabilities;

internal sealed class ILSpyCodeKnowledgeCapability : ICodeKnowledgeCapability
{
    private sealed record DecompileCacheEntry(
        string ModuleName,
        string ModulePath,
        long SizeBytes,
        long LastWriteTimeUtcTicks,
        int SourceFileCount
    );

    public async Task<Result<CodeExtractionOutput, CodeExtractionError>> ExtractAndIndexModuleAsync(
        TargetDescriptor target,
        string moduleName,
        CandiedStorageLayout storageLayout,
        ISymbolIndex symbolIndex,
        IProgress<CodeExtractionProgress>? progress = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(storageLayout);
        ArgumentNullException.ThrowIfNull(symbolIndex);

        if (string.IsNullOrWhiteSpace(moduleName))
        {
            return Result.Fail<CodeExtractionOutput, CodeExtractionError>(
                new CodeExtractionError("Module name cannot be empty."));
        }

        string cleanRequested = moduleName.Trim();
        if (cleanRequested.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            cleanRequested = cleanRequested[..^4];
        }

        var matchedModule = target.CodeModules.FirstOrDefault(m =>
            string.Equals(Path.GetFileNameWithoutExtension(m.Name), cleanRequested, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(m.Name, moduleName.Trim(), StringComparison.OrdinalIgnoreCase));

        string? assemblyPath = matchedModule?.FullPath;
        if (string.IsNullOrEmpty(assemblyPath) && target.Metadata.TryGetValue("managedPath", out var managedPath) && Directory.Exists(managedPath))
        {
            string candidate = Path.Combine(managedPath, cleanRequested + ".dll");
            if (File.Exists(candidate))
            {
                assemblyPath = candidate;
            }
        }

        if (string.IsNullOrEmpty(assemblyPath) || !File.Exists(assemblyPath))
        {
            return Result.Fail<CodeExtractionOutput, CodeExtractionError>(
                new CodeExtractionError($"Managed code module '{moduleName}' was not found in target '{target.Name}'.", cleanRequested));
        }

        string canonicalName = Path.GetFileNameWithoutExtension(assemblyPath);
        string outputSourceDirectory = storageLayout.GetExtractedCodeDir(target.Name, canonicalName);
        string cacheFilePath = Path.Combine(outputSourceDirectory, $".{canonicalName}.cache");
        var fileInfo = new FileInfo(assemblyPath);

        try
        {
            int csCount = 0;
            bool cacheHit = false;

            if (File.Exists(cacheFilePath) && Directory.Exists(outputSourceDirectory))
            {
                try
                {
                    string cacheJson = await File.ReadAllTextAsync(cacheFilePath, ct).ConfigureAwait(false);
                    var cacheData = JsonSerializer.Deserialize<DecompileCacheEntry>(cacheJson);
                    if (cacheData != null &&
                        cacheData.SizeBytes == fileInfo.Length &&
                        cacheData.LastWriteTimeUtcTicks == fileInfo.LastWriteTimeUtc.Ticks)
                    {
                        var existingCs = Directory.GetFiles(outputSourceDirectory, "*.cs", SearchOption.AllDirectories);
                        if (existingCs.Length >= cacheData.SourceFileCount && cacheData.SourceFileCount > 0)
                        {
                            csCount = cacheData.SourceFileCount;
                            cacheHit = true;
                            progress?.Report(new CodeExtractionProgress(80, $"Cache hit for {canonicalName}"));
                        }
                    }
                }
                catch
                {
                }
            }

            if (!cacheHit)
            {
                Directory.CreateDirectory(outputSourceDirectory);
                string assemblyDir = Path.GetDirectoryName(assemblyPath) ?? string.Empty;
                var resolver = new UniversalAssemblyResolver(assemblyPath, false, null);
                if (!string.IsNullOrEmpty(assemblyDir))
                {
                    resolver.AddSearchDirectory(assemblyDir);
                }

                var settings = new DecompilerSettings(ICSharpCode.Decompiler.CSharp.LanguageVersion.Latest)
                {
                    ThrowOnAssemblyResolveErrors = false
                };

                var decompiler = new WholeProjectDecompiler(settings, resolver, null, null, null);
                progress?.Report(new CodeExtractionProgress(15, $"Decompiling {canonicalName}..."));

                await Task.Run(() =>
                {
                    using var peFile = new PEFile(assemblyPath);
                    decompiler.DecompileProject(peFile, outputSourceDirectory);
                }, ct).ConfigureAwait(false);

                csCount = Directory.GetFiles(outputSourceDirectory, "*.cs", SearchOption.AllDirectories).Length;

                try
                {
                    var entry = new DecompileCacheEntry(canonicalName, assemblyPath, fileInfo.Length, fileInfo.LastWriteTimeUtc.Ticks, csCount);
                    await File.WriteAllTextAsync(cacheFilePath, JsonSerializer.Serialize(entry), ct).ConfigureAwait(false);
                }
                catch
                {
                }
            }

            string dbPath = storageLayout.GetDatabasePath(target.Name);
            var indexRes = await symbolIndex.IndexCSharpSourceTreeAsync(outputSourceDirectory, canonicalName, dbPath, ct).ConfigureAwait(false);
            if (indexRes.IsFailure)
            {
                return Result.Fail<CodeExtractionOutput, CodeExtractionError>(
                    new CodeExtractionError($"Extracted '{canonicalName}' ({csCount} files), but symbol indexing failed: {indexRes.Error.Message}", canonicalName));
            }

            progress?.Report(new CodeExtractionProgress(100, $"Indexed {canonicalName} ({csCount} files)"));
            return Result.Ok<CodeExtractionOutput, CodeExtractionError>(
                new CodeExtractionOutput(canonicalName, outputSourceDirectory, csCount));
        }
        catch (Exception ex)
        {
            return Result.Fail<CodeExtractionOutput, CodeExtractionError>(
                new CodeExtractionError($"Decompilation error for '{canonicalName}': {ex.Message}", canonicalName));
        }
    }
}
