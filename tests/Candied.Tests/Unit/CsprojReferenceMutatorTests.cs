using Candied.Core.Ports;
using Candied.Core.UseCases;
using Candied.Workspace;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Candied.Tests.Unit;

public class CsprojReferenceMutatorTests : IDisposable
{
    private readonly string _tempDir;
    private readonly AddProjectReferenceUseCase _useCase;

    public CsprojReferenceMutatorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "Candied_RefMutator_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);

        var services = new ServiceCollection();
        services.AddWorkspaceInfrastructure();
        services.AddTransient<AddProjectReferenceUseCase>();
        var sp = services.BuildServiceProvider();
        _useCase = sp.GetRequiredService<AddProjectReferenceUseCase>();
    }

    [Fact]
    public async Task AddProjectReference_InsertsReferenceAndDetectsDuplicate()
    {
        var gameRoot = Path.Combine(_tempDir, "SampleGame");
        var managedDir = Path.Combine(gameRoot, "SampleGame_Data", "Managed");
        Directory.CreateDirectory(managedDir);

        var existingDll = Path.Combine(managedDir, "Assembly-CSharp.dll");
        var animDll = Path.Combine(managedDir, "UnityEngine.AnimationModule.dll");
        await File.WriteAllBytesAsync(existingDll, [0x4D, 0x5A]);
        await File.WriteAllBytesAsync(animDll, [0x4D, 0x5A]);

        var csprojPath = Path.Combine(_tempDir, "SampleMod.csproj");
        var initialCsproj = $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>netstandard2.1</TargetFramework>
              </PropertyGroup>
              <ItemGroup>
                <Reference Include="Assembly-CSharp">
                  <HintPath>{existingDll}</HintPath>
                  <Private>false</Private>
                </Reference>
              </ItemGroup>
            </Project>
            """;
        await File.WriteAllTextAsync(csprojPath, initialCsproj);

        // Act 1: add reference omitting gameDirectory (inferred from existing HintPath)
        var res1 = await _useCase.ExecuteAsync(csprojPath, "UnityEngine.AnimationModule", publicize: true);
        Assert.True(res1.IsSuccess);
        Assert.False(res1.Value.AlreadyReferenced);
        Assert.True(res1.Value.Publicized);
        Assert.Equal(animDll, res1.Value.DllPath);

        var updatedXml = await File.ReadAllTextAsync(csprojPath);
        Assert.Contains("UnityEngine.AnimationModule", updatedXml);
        Assert.Contains("<Publicize>true</Publicize>", updatedXml);
        Assert.Contains("<Private>false</Private>", updatedXml);

        // Act 2: adding the same assembly again is idempotent
        var res2 = await _useCase.ExecuteAsync(csprojPath, "UnityEngine.AnimationModule.dll", publicize: false);
        Assert.True(res2.IsSuccess);
        Assert.True(res2.Value.AlreadyReferenced);
    }

    [Fact]
    public async Task AddProjectReference_WhenDllMissing_ReturnsHelpfulErrorWithSuggestions()
    {
        var gameRoot = Path.Combine(_tempDir, "SampleGame2");
        var managedDir = Path.Combine(gameRoot, "SampleGame2_Data", "Managed");
        Directory.CreateDirectory(managedDir);
        await File.WriteAllBytesAsync(Path.Combine(managedDir, "UnityEngine.AnimationModule.dll"), [0x4D, 0x5A]);

        var csprojPath = Path.Combine(_tempDir, "SampleMod2.csproj");
        await File.WriteAllTextAsync(csprojPath, "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>");

        var res = await _useCase.ExecuteAsync(csprojPath, "Animation", publicize: false, gameDirectory: gameRoot);
        Assert.True(res.IsFailure);
        Assert.Contains("UnityEngine.AnimationModule", res.Error.Message);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true);
        }
        catch { }
        GC.SuppressFinalize(this);
    }
}
