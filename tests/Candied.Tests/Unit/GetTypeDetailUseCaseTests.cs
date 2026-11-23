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

public class GetTypeDetailUseCaseTests
{
    private sealed class FakeSymbolIndexForDetail : ISymbolIndex
    {
        public bool ShouldFail { get; set; }
        public string? ReceivedDatabasePath { get; private set; }

        public Task<Result<Candied.Primitives.Unit, IndexError>> IndexSourceTreeAsync(
            string sourceDirectory,
            string? assemblyName = null,
            string? databasePath = null,
            CancellationToken ct = default) =>
            Task.FromResult(Result.Ok<Candied.Primitives.Unit, IndexError>(new Candied.Primitives.Unit()));

        public Task<Result<IReadOnlyList<SymbolSummary>, IndexError>> SearchSymbolsAsync(SymbolQuery query, string? databasePath = null, CancellationToken ct = default) =>
            Task.FromResult(Result.Ok<IReadOnlyList<SymbolSummary>, IndexError>(new List<SymbolSummary>()));

        public Task<Result<IReadOnlyList<SymbolSummary>, IndexError>> GetAllSymbolsAsync(string? databasePath = null, CancellationToken ct = default) =>
            Task.FromResult(Result.Ok<IReadOnlyList<SymbolSummary>, IndexError>(new List<SymbolSummary>()));

        public Task<Result<IReadOnlyList<CodeSearchMatch>, IndexError>> SearchCodeAsync(CodeSearchQuery query, string? databasePath = null, CancellationToken ct = default) =>
            Task.FromResult(Result.Ok<IReadOnlyList<CodeSearchMatch>, IndexError>(new List<CodeSearchMatch>()));

        public Task<Result<TypeDefinitionDetail, IndexError>> GetTypeDetailAsync(
            string fullTypeName,
            string? databasePath = null,
            CancellationToken ct = default)
        {
            ReceivedDatabasePath = databasePath;

            if (ShouldFail)
            {
                return Task.FromResult<Result<TypeDefinitionDetail, IndexError>>(
                    new IndexError($"Type not found: {fullTypeName}"));
            }

            var detail = new TypeDefinitionDetail(
                FullName: fullTypeName,
                Namespace: "Game.Namespace",
                BaseType: "UnityEngine.MonoBehaviour",
                Interfaces: new List<string> { "IPlayer" },
                FieldSignatures: new List<string> { "private int health;" },
                MethodSignatures: new List<string> { "public void TakeDamage(int amount)" },
                RawSourceCode: "public class Player : MonoBehaviour { private int health; }"
            );

            return Task.FromResult(Result.Ok<TypeDefinitionDetail, IndexError>(detail));
        }
    }

    [Fact]
    public async Task ExecuteAsync_Returns_Detail_And_Forwards_DatabasePath()
    {
        var fakeIndex = new FakeSymbolIndexForDetail();
        var useCase = new GetTypeDetailUseCase(fakeIndex);

        var result = await useCase.ExecuteAsync("Game.Namespace.Player", "C:/Workspace/.candied/games/Game/index.db");

        Assert.True(result.IsSuccess);
        Assert.Equal("Game.Namespace.Player", result.Value.FullName);
        Assert.Equal("C:/Workspace/.candied/games/Game/index.db", fakeIndex.ReceivedDatabasePath);
    }

    [Fact]
    public async Task ExecuteAsync_Returns_Failure_When_Index_Fails()
    {
        var fakeIndex = new FakeSymbolIndexForDetail { ShouldFail = true };
        var useCase = new GetTypeDetailUseCase(fakeIndex);

        var result = await useCase.ExecuteAsync("NonExistent");

        Assert.True(result.IsFailure);
        Assert.Contains("not found", result.Error.Message);
    }
}
