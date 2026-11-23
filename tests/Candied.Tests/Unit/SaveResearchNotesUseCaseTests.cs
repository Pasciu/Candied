using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Candied.Core.Errors;
using Candied.Core.Models;
using Candied.Core.Ports;
using Candied.Core.UseCases;
using Candied.Primitives;
using Xunit;

namespace Candied.Tests.Unit;

public class SaveResearchNotesUseCaseTests
{
    private sealed class FakeDocGenerator : IDocumentationGenerator
    {
        public bool SaveCalled { get; private set; }
        public string? ReceivedGameName { get; private set; }
        public string? ReceivedNotes { get; private set; }
        public string? ReceivedDocsPath { get; private set; }

        public Task<Result<Candied.Primitives.Unit, ScaffoldError>> GenerateKnowledgeHubAsync(GameArchitectureOverview overview, string docsDirectoryPath, CancellationToken ct = default) =>
            Task.FromResult(Result.Ok<Candied.Primitives.Unit, ScaffoldError>(new Candied.Primitives.Unit()));

        public Task<Result<Candied.Primitives.Unit, ScaffoldError>> GenerateKnowledgeHubAsync(GameEnvironment gameEnv, IReadOnlyList<SymbolSummary> symbols, string docsDirectoryPath, CancellationToken ct = default) =>
            Task.FromResult(Result.Ok<Candied.Primitives.Unit, ScaffoldError>(new Candied.Primitives.Unit()));

        public Task<Result<Candied.Primitives.Unit, ScaffoldError>> SaveResearchNotesAsync(string gameName, string markdownContent, string docsDirectoryPath, CancellationToken ct = default)
        {
            SaveCalled = true;
            ReceivedGameName = gameName;
            ReceivedNotes = markdownContent;
            ReceivedDocsPath = docsDirectoryPath;
            return Task.FromResult(Result.Ok<Candied.Primitives.Unit, ScaffoldError>(new Candied.Primitives.Unit()));
        }
    }

    [Fact]
    public async Task ExecuteAsync_Persists_Research_Notes_To_Docs_Directory()
    {
        var docGen = new FakeDocGenerator();
        var useCase = new SaveResearchNotesUseCase(docGen);
        var layout = new CandiedStorageLayout("C:/TestWorkspace");

        var result = await useCase.ExecuteAsync("PotionCraft", "# Notes on BepInEx 5\nUse Mono x64", layout);

        Assert.True(result.IsSuccess);
        Assert.True(docGen.SaveCalled);
        Assert.Equal("PotionCraft", docGen.ReceivedGameName);
        Assert.Equal("# Notes on BepInEx 5\nUse Mono x64", docGen.ReceivedNotes);
        Assert.Equal("C:/TestWorkspace/.candied/games/PotionCraft/docs", docGen.ReceivedDocsPath);
    }

    [Fact]
    public async Task ExecuteAsync_Fails_When_GameName_Is_Empty()
    {
        var docGen = new FakeDocGenerator();
        var useCase = new SaveResearchNotesUseCase(docGen);
        var layout = new CandiedStorageLayout("C:/TestWorkspace");

        var result = await useCase.ExecuteAsync("", "# Notes", layout);

        Assert.True(result.IsFailure);
        Assert.Contains("Game name cannot be empty", result.Error.Message);
        Assert.False(docGen.SaveCalled);
    }
}
