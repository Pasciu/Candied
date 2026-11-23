using System;
using System.Threading;
using System.Threading.Tasks;
using Candied.Core.Errors;
using Candied.Core.Models;
using Candied.Core.Ports;
using Candied.Primitives;

namespace Candied.Core.UseCases;

/// <summary>
/// Use case for persisting agent research notes (recommended BepInEx version, runtime targets, known hooks)
/// into a permanent documentation artifact in the workspace.
/// </summary>
public sealed class SaveResearchNotesUseCase
{
    private readonly IDocumentationGenerator _documentationGenerator;

    public SaveResearchNotesUseCase(IDocumentationGenerator documentationGenerator)
    {
        _documentationGenerator = documentationGenerator;
    }

    public async Task<Result<Unit, DomainError>> ExecuteAsync(
        string gameName,
        string markdownContent,
        CandiedStorageLayout layout,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(gameName))
        {
            return Result.Fail<Unit, DomainError>(new ScaffoldError("Game name cannot be empty."));
        }

        if (string.IsNullOrWhiteSpace(markdownContent))
        {
            return Result.Fail<Unit, DomainError>(new ScaffoldError("Research notes markdown content cannot be empty."));
        }

        var docsDir = layout.GetDocsDir(gameName);
        var saveResult = await _documentationGenerator.SaveResearchNotesAsync(
            gameName,
            markdownContent,
            docsDir,
            ct).ConfigureAwait(false);

        if (saveResult.IsFailure)
        {
            return Result.Fail<Unit, DomainError>(saveResult.Error);
        }

        return Result.Ok<Unit, DomainError>(new Unit());
    }
}
