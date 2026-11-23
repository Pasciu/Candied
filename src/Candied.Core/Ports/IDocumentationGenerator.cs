using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Candied.Core.Errors;
using Candied.Core.Models;
using Candied.Primitives;

namespace Candied.Core.Ports;

/// <summary>
/// Port for generating human-readable and agent-readable documentation artifacts
/// (overview guide, namespace cheat-sheet, hierarchical symbols index, and research notes) in the workspace knowledge hub.
/// </summary>
public interface IDocumentationGenerator
{
    Task<Result<Unit, ScaffoldError>> GenerateKnowledgeHubAsync(
        GameArchitectureOverview overview,
        string docsDirectoryPath,
        CancellationToken ct = default);

    Task<Result<Unit, ScaffoldError>> GenerateKnowledgeHubAsync(
        GameEnvironment gameEnv,
        IReadOnlyList<SymbolSummary> symbols,
        string docsDirectoryPath,
        CancellationToken ct = default);

    Task<Result<Unit, ScaffoldError>> SaveResearchNotesAsync(
        string gameName,
        string markdownContent,
        string docsDirectoryPath,
        CancellationToken ct = default);
}
