using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Candied.Core.Errors;
using Candied.Core.Models;
using Candied.Core.Ports;
using Candied.Primitives;
using Candied.Workspace.Rendering;

namespace Candied.Workspace.Adapters;

/// <summary>
/// Infrastructure adapter that writes generated knowledge hub documents (overview, namespace map, symbols JSON)
/// to the workspace Zone 3 artifacts directory.
/// </summary>
internal sealed class DiskDocumentationGeneratorAdapter : IDocumentationGenerator
{
    public async Task<Result<Unit, ScaffoldError>> GenerateKnowledgeHubAsync(
        GameArchitectureOverview overview,
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

            // 1. GAME_OVERVIEW.md
            var overviewMarkdown = GameOverviewTemplateRenderer.Render(overview);
            var overviewPath = Path.Combine(docsDirectoryPath, "GAME_OVERVIEW.md");
            await File.WriteAllTextAsync(overviewPath, overviewMarkdown, ct).ConfigureAwait(false);

            // 2. NAMESPACES.md
            var namespacesMarkdown = NamespaceMapTemplateRenderer.Render(overview.Namespaces, overview.GameName);
            var namespacesPath = Path.Combine(docsDirectoryPath, "NAMESPACES.md");
            await File.WriteAllTextAsync(namespacesPath, namespacesMarkdown, ct).ConfigureAwait(false);

            // 3. SYMBOLS_INDEX.json
            var symbolsJson = SymbolIndexJsonRenderer.Render(overview);
            var symbolsJsonPath = Path.Combine(docsDirectoryPath, "SYMBOLS_INDEX.json");
            await File.WriteAllTextAsync(symbolsJsonPath, symbolsJson, ct).ConfigureAwait(false);

            return Result.Ok<Unit, ScaffoldError>(new Unit());
        }
        catch (Exception ex)
        {
            return Result.Fail<Unit, ScaffoldError>(
                new ScaffoldError($"Failed to write knowledge hub documentation: {ex.Message}"));
        }
    }

    public Task<Result<Unit, ScaffoldError>> GenerateKnowledgeHubAsync(
        GameEnvironment gameEnv,
        System.Collections.Generic.IReadOnlyList<SymbolSummary> symbols,
        string docsDirectoryPath,
        CancellationToken ct = default)
    {
        var overview = Analysis.GameArchitectureAnalyzer.Analyze(gameEnv, symbols);
        return GenerateKnowledgeHubAsync(overview, docsDirectoryPath, ct);
    }

    public async Task<Result<Unit, ScaffoldError>> SaveResearchNotesAsync(
        string gameName,
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
            var researchPath = Path.Combine(docsDirectoryPath, "RESEARCH.md");
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
