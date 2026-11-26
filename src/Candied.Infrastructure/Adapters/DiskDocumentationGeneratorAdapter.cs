using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Candied.Core.Errors;
using Candied.Core.Models;
using Candied.Core.Ports;
using Candied.Infrastructure.Internal;
using Candied.Primitives;

namespace Candied.Infrastructure.Adapters;

internal sealed class DiskDocumentationGeneratorAdapter : IDocumentationGenerator
{
    public async Task<Result<Unit, ScaffoldError>> GenerateKnowledgeHubAsync(
        TargetArchitectureOverview overview,
        string docsDirectoryPath,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(overview);

        if (string.IsNullOrWhiteSpace(docsDirectoryPath))
        {
            return Result.Fail<Unit, ScaffoldError>(new ScaffoldError("Docs directory path cannot be empty."));
        }

        try
        {
            Directory.CreateDirectory(docsDirectoryPath);

            string overviewMarkdown = KnowledgeHubRenderers.RenderOverview(overview);
            await File.WriteAllTextAsync(Path.Combine(docsDirectoryPath, "TARGET_OVERVIEW.md"), overviewMarkdown, ct).ConfigureAwait(false);

            string namespacesMarkdown = KnowledgeHubRenderers.RenderNamespaces(overview.Namespaces, overview.TargetName);
            await File.WriteAllTextAsync(Path.Combine(docsDirectoryPath, "NAMESPACES.md"), namespacesMarkdown, ct).ConfigureAwait(false);

            string symbolsJson = KnowledgeHubRenderers.RenderSymbolsJson(overview);
            await File.WriteAllTextAsync(Path.Combine(docsDirectoryPath, "SYMBOLS_INDEX.json"), symbolsJson, ct).ConfigureAwait(false);

            return Result.Ok<Unit, ScaffoldError>(new Unit());
        }
        catch (Exception ex)
        {
            return Result.Fail<Unit, ScaffoldError>(
                new ScaffoldError($"Failed to write knowledge hub documentation: {ex.Message}"));
        }
    }

    public Task<Result<Unit, ScaffoldError>> GenerateKnowledgeHubAsync(
        TargetDescriptor target,
        IReadOnlyList<SymbolSummary> symbols,
        string docsDirectoryPath,
        CancellationToken ct = default)
    {
        var overview = TargetArchitectureAnalyzer.Analyze(target, symbols);
        return GenerateKnowledgeHubAsync(overview, docsDirectoryPath, ct);
    }

    public async Task<Result<Unit, ScaffoldError>> SaveResearchNotesAsync(
        string targetName,
        string markdownContent,
        string docsDirectoryPath,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(docsDirectoryPath))
        {
            return Result.Fail<Unit, ScaffoldError>(new ScaffoldError("Docs directory path cannot be empty."));
        }

        try
        {
            Directory.CreateDirectory(docsDirectoryPath);
            string researchPath = Path.Combine(docsDirectoryPath, "RESEARCH.md");
            await File.WriteAllTextAsync(researchPath, markdownContent, ct).ConfigureAwait(false);
            return Result.Ok<Unit, ScaffoldError>(new Unit());
        }
        catch (Exception ex)
        {
            return Result.Fail<Unit, ScaffoldError>(
                new ScaffoldError($"Failed to write research notes: {ex.Message}"));
        }
    }
}
