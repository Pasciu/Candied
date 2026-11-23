using System;
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

public class DecompileAssemblyUseCaseTests
{
    private sealed class FakeGameDetector : IGameDetector
    {
        public Task<Result<GameEnvironment, DetectionError>> DetectGameAsync(string gameDirectoryPath, CancellationToken ct = default)
        {
            var env = new GameEnvironment(
                Id: new GameId("PotionCraft"),
                GameName: "PotionCraft",
                GameRootPath: gameDirectoryPath,
                ManagedPath: $"{gameDirectoryPath}/Managed",
                EngineType: UnityEngineType.Mono,
                Architecture: "x64",
                TargetFramework: "net472",
                BepInExInfo: new BepInExInstallation(true, "5.4.21.0", $"{gameDirectoryPath}/BepInEx"),
                DiscoveredAssemblies: new List<DiscoveredAssembly>
                {
                    new("PotionCraft.Core.dll", $"{gameDirectoryPath}/Managed/PotionCraft.Core.dll", 5000, AssemblyTier.CoreGame, "Core", false),
                    new("Assembly-CSharp.dll", $"{gameDirectoryPath}/Managed/Assembly-CSharp.dll", 8000, AssemblyTier.CoreGame, "Unity gameplay", false)
                },
                EnvironmentWarnings: new List<string>()
            );

            return Task.FromResult(Result.Ok<GameEnvironment, DetectionError>(env));
        }
    }

    private sealed class FakeDecompilerEngine : IDecompilerEngine
    {
        public string? DecompiledPath { get; private set; }
        public bool ShouldFail { get; set; }

        public Task<Result<DecompilationOutput, DecompilerError>> DecompileAssemblyAsync(
            string assemblyPath,
            string outputSourceDirectory,
            IProgress<DecompileProgress>? progress = null,
            CancellationToken ct = default)
        {
            if (ShouldFail)
            {
                return Task.FromResult(Result.Fail<DecompilationOutput, DecompilerError>(
                    new DecompilerError("ILSpy decompilation failed", "PotionCraft.Core")));
            }

            DecompiledPath = assemblyPath;
            return Task.FromResult(Result.Ok<DecompilationOutput, DecompilerError>(
                new DecompilationOutput("PotionCraft.Core", outputSourceDirectory, 42)));
        }
    }

    private sealed class FakeSymbolIndex : ISymbolIndex
    {
        public bool IndexCalled { get; private set; }

        public Task<Result<Candied.Primitives.Unit, IndexError>> IndexSourceTreeAsync(
            string sourceDirectory,
            string? assemblyName = null,
            string? databasePath = null,
            CancellationToken ct = default)
        {
            IndexCalled = true;
            return Task.FromResult(Result.Ok<Candied.Primitives.Unit, IndexError>(new Candied.Primitives.Unit()));
        }

        public Task<Result<IReadOnlyList<SymbolSummary>, IndexError>> SearchSymbolsAsync(SymbolQuery query, string? databasePath = null, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<Result<IReadOnlyList<SymbolSummary>, IndexError>> GetAllSymbolsAsync(string? databasePath = null, CancellationToken ct = default) =>
            Task.FromResult(Result.Ok<IReadOnlyList<SymbolSummary>, IndexError>([]));

        public Task<Result<TypeDefinitionDetail, IndexError>> GetTypeDetailAsync(string fullTypeName, string? databasePath = null, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<Result<IReadOnlyList<CodeSearchMatch>, IndexError>> SearchCodeAsync(CodeSearchQuery query, string? databasePath = null, CancellationToken ct = default) =>
            Task.FromResult(Result.Ok<IReadOnlyList<CodeSearchMatch>, IndexError>([]));
    }

    [Fact]
    public async Task ExecuteAsync_Decompiles_And_Indexes_Targeted_Assembly()
    {
        var detector = new FakeGameDetector();
        var decompiler = new FakeDecompilerEngine();
        var indexer = new FakeSymbolIndex();
        var layout = new CandiedStorageLayout("C:/Workspace");

        var useCase = new DecompileAssemblyUseCase(detector, decompiler, indexer);

        var result = await useCase.ExecuteAsync("C:/Games/PotionCraft", "PotionCraft.Core", layout);

        Assert.True(result.IsSuccess);
        Assert.Equal("PotionCraft.Core", result.Value.AssemblyName);
        Assert.Equal(42, result.Value.SourceFileCount);
        Assert.Equal("C:/Games/PotionCraft/Managed/PotionCraft.Core.dll", decompiler.DecompiledPath);
        Assert.True(indexer.IndexCalled);
    }

    [Fact]
    public async Task ExecuteAsync_Fails_When_Assembly_Not_Found()
    {
        var detector = new FakeGameDetector();
        var decompiler = new FakeDecompilerEngine();
        var indexer = new FakeSymbolIndex();
        var layout = new CandiedStorageLayout("C:/Workspace");

        var useCase = new DecompileAssemblyUseCase(detector, decompiler, indexer);

        var result = await useCase.ExecuteAsync("C:/Games/PotionCraft", "NonExistentAssembly", layout);

        Assert.True(result.IsFailure);
        Assert.Contains("was not found", result.Error.Message);
        Assert.Null(decompiler.DecompiledPath);
        Assert.False(indexer.IndexCalled);
    }
}
