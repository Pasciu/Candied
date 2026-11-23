using System.Text.Json;
using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.CSharp.ProjectDecompiler;
using ICSharpCode.Decompiler.Metadata;
using Candied.Core.Errors;
using Candied.Core.Models;
using Candied.Core.Ports;
using Candied.Decompiler.Internal;
using Candied.Primitives;

namespace Candied.Decompiler.Adapters;

internal sealed class ILSpyDecompilerEngineAdapter : IDecompilerEngine
{
    public async Task<Result<DecompilationOutput, DecompilerError>> DecompileAssemblyAsync(
        string assemblyPath,
        string outputSourceDirectory,
        IProgress<DecompileProgress>? progress = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(assemblyPath) || !File.Exists(assemblyPath))
        {
            return Result.Fail<DecompilationOutput, DecompilerError>(
                new DecompilerError($"Assembly file not found: '{assemblyPath}'", Path.GetFileName(assemblyPath)));
        }

        var assemblyName = Path.GetFileNameWithoutExtension(assemblyPath);
        var assemblyFileInfo = new FileInfo(assemblyPath);
        var cacheFilePath = Path.Combine(outputSourceDirectory, $".{assemblyName}.cache");

        try
        {
            if (File.Exists(cacheFilePath))
            {
                try
                {
                    var cacheJson = await File.ReadAllTextAsync(cacheFilePath, ct);
                    var cacheData = JsonSerializer.Deserialize<DecompileCacheData>(cacheJson);
                    if (cacheData != null &&
                        cacheData.AssemblySizeBytes == assemblyFileInfo.Length &&
                        cacheData.AssemblyLastWriteTimeUtcTicks == assemblyFileInfo.LastWriteTimeUtc.Ticks &&
                        Directory.Exists(outputSourceDirectory))
                    {
                        var existingCsFiles = Directory.GetFiles(outputSourceDirectory, "*.cs", SearchOption.AllDirectories);
                        if (existingCsFiles.Length >= cacheData.SourceFileCount && cacheData.SourceFileCount > 0)
                        {
                            progress?.Report(new DecompileProgress(100, $"Decompilation cache hit for {assemblyName}"));
                            return Result.Ok<DecompilationOutput, DecompilerError>(
                                new DecompilationOutput(assemblyName, outputSourceDirectory, cacheData.SourceFileCount));
                        }
                    }
                }
                catch
                {
                    // Cache read failure, proceed with full decompile
                }
            }

            if (!Directory.Exists(outputSourceDirectory))
            {
                Directory.CreateDirectory(outputSourceDirectory);
            }

            var assemblyDir = Path.GetDirectoryName(assemblyPath) ?? string.Empty;
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

            progress?.Report(new DecompileProgress(10, $"Loading {assemblyName}..."));

            await Task.Run(() =>
            {
                using var peFile = new PEFile(assemblyPath);
                decompiler.DecompileProject(peFile, outputSourceDirectory);
            }, ct);

            progress?.Report(new DecompileProgress(100, $"Decompiled {assemblyName} successfully"));

            var csCount = Directory.GetFiles(outputSourceDirectory, "*.cs", SearchOption.AllDirectories).Length;

            try
            {
                var cacheData = new DecompileCacheData(
                    assemblyName,
                    assemblyPath,
                    assemblyFileInfo.Length,
                    assemblyFileInfo.LastWriteTimeUtc.Ticks,
                    csCount
                );
                await File.WriteAllTextAsync(cacheFilePath, JsonSerializer.Serialize(cacheData), ct);
            }
            catch
            {
                // Ignore cache write errors
            }

            var output = new DecompilationOutput(
                AssemblyName: assemblyName,
                OutputDirectory: outputSourceDirectory,
                SourceFileCount: csCount
            );

            return Result.Ok<DecompilationOutput, DecompilerError>(output);
        }
        catch (Exception ex)
        {
            return Result.Fail<DecompilationOutput, DecompilerError>(
                new DecompilerError($"Decompilation error for '{assemblyName}': {ex.Message}", assemblyName));
        }
    }
}
