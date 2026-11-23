using Candied.Core.Models;
using Candied.Workspace.Adapters;
using Xunit;

namespace Candied.Tests.Integration;

public sealed class DotNetBuildRunnerIntegrationTests : IDisposable
{
    private readonly string _tempDir;

    public DotNetBuildRunnerIntegrationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "Candied_BuildTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch
        {
            // Ignore cleanup failure
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task BuildModAsync_Successfully_Builds_Valid_Project()
    {
        // Arrange
        var projDir = Path.Combine(_tempDir, "ValidProj");
        Directory.CreateDirectory(projDir);

        var csprojPath = Path.Combine(projDir, "ValidProj.csproj");
        var csprojXml = """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <Nullable>enable</Nullable>
  </PropertyGroup>
</Project>
""";
        await File.WriteAllTextAsync(csprojPath, csprojXml);

        var sourcePath = Path.Combine(projDir, "TestClass.cs");
        var sourceCode = """
namespace ValidProj;

public class TestClass
{
    public int Add(int a, int b) => a + b;
}
""";
        await File.WriteAllTextAsync(sourcePath, sourceCode);

        var runner = new DotNetBuildRunnerAdapter();

        // Act
        var result = await runner.BuildModAsync(csprojPath);

        // Assert
        Assert.True(result.IsSuccess);
        var report = result.Value;
        Assert.True(report.IsSuccess);
        Assert.Equal(0, report.ExitCode);
        Assert.DoesNotContain(report.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public async Task BuildModAsync_Successfully_Builds_With_Relative_Path()
    {
        // Arrange
        var projDir = Path.Combine(_tempDir, "RelativeProj");
        Directory.CreateDirectory(projDir);

        var csprojPath = Path.Combine(projDir, "RelativeProj.csproj");
        var csprojXml = """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <Nullable>enable</Nullable>
  </PropertyGroup>
</Project>
""";
        await File.WriteAllTextAsync(csprojPath, csprojXml);

        var sourcePath = Path.Combine(projDir, "Hello.cs");
        var sourceCode = """
namespace RelativeProj;

public static class Hello
{
    public static string Greet() => "World";
}
""";
        await File.WriteAllTextAsync(sourcePath, sourceCode);

        var relativePath = Path.GetRelativePath(Directory.GetCurrentDirectory(), csprojPath);
        var runner = new DotNetBuildRunnerAdapter();

        // Act
        var result = await runner.BuildModAsync(relativePath);

        // Assert
        Assert.True(result.IsSuccess);
        var report = result.Value;
        Assert.True(report.IsSuccess);
        Assert.Equal(0, report.ExitCode);
        Assert.DoesNotContain(report.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public async Task BuildModAsync_Captures_Diagnostics_When_Compilation_Fails()
    {
        // Arrange
        var projDir = Path.Combine(_tempDir, "InvalidProj");
        Directory.CreateDirectory(projDir);

        var csprojPath = Path.Combine(projDir, "InvalidProj.csproj");
        var csprojXml = """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <Nullable>enable</Nullable>
  </PropertyGroup>
</Project>
""";
        await File.WriteAllTextAsync(csprojPath, csprojXml);

        var sourcePath = Path.Combine(projDir, "BrokenClass.cs");
        var sourceCode = """
namespace InvalidProj;

public class BrokenClass
{
    public void Run()
    {
        UndefinedMethodCall();
    }
}
""";
        await File.WriteAllTextAsync(sourcePath, sourceCode);

        var runner = new DotNetBuildRunnerAdapter();

        // Act
        var result = await runner.BuildModAsync(csprojPath);

        // Assert
        Assert.True(result.IsSuccess);
        var report = result.Value;
        Assert.False(report.IsSuccess);
        Assert.NotEqual(0, report.ExitCode);

        var error = Assert.Single(report.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Equal("CS0103", error.Code);
        Assert.Equal(7, error.LineNumber);
        Assert.Contains("UndefinedMethodCall", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BuildModAsync_Deploys_Loose_Assets_To_Plugin_Directory()
    {
        // Arrange
        var projDir = Path.Combine(_tempDir, "AssetProj");
        var assetsDir = Path.Combine(projDir, "Assets");
        var fakeGameDir = Path.Combine(_tempDir, "FakeGame");
        var pluginsDir = Path.Combine(fakeGameDir, "BepInEx", "plugins", "AssetProj");

        Directory.CreateDirectory(projDir);
        Directory.CreateDirectory(assetsDir);
        Directory.CreateDirectory(fakeGameDir);

        var assetFilePath = Path.Combine(assetsDir, "custom_texture.png");
        await File.WriteAllBytesAsync(assetFilePath, new byte[] { 0x89, 0x50, 0x4E, 0x47 });

        var subAssetsDir = Path.Combine(assetsDir, "Icons");
        Directory.CreateDirectory(subAssetsDir);
        var subAssetFilePath = Path.Combine(subAssetsDir, "icon.png");
        await File.WriteAllBytesAsync(subAssetFilePath, new byte[] { 0x89, 0x50, 0x4E, 0x47 });

        var csprojPath = Path.Combine(projDir, "AssetProj.csproj");
        var csprojXml = $"""
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <AssemblyName>AssetProj</AssemblyName>
    <Nullable>enable</Nullable>
    <GameRootPath>{fakeGameDir}</GameRootPath>
    <ModPluginsDeployDir>$(GameRootPath)\BepInEx\plugins\AssetProj\</ModPluginsDeployDir>
  </PropertyGroup>

  <ItemGroup>
    <None Update="Assets\**" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>

  <Target Name="DeployToBepInEx" AfterTargets="Build" Condition="Exists('$(GameRootPath)')">
    <MakeDir Directories="$(ModPluginsDeployDir)" />
    <Copy SourceFiles="$(TargetPath)" DestinationFolder="$(ModPluginsDeployDir)" SkipUnchangedFiles="true" />
    <MakeDir Directories="$(ModPluginsDeployDir)Assets" Condition="Exists('$(MSBuildProjectDirectory)\Assets')" />
    <ItemGroup Condition="Exists('$(MSBuildProjectDirectory)\Assets')">
      <ModAssets Include="$(MSBuildProjectDirectory)\Assets\**\*.*" />
    </ItemGroup>
    <Copy SourceFiles="@(ModAssets)"
          DestinationFolder="$(ModPluginsDeployDir)Assets\%(RecursiveDir)"
          SkipUnchangedFiles="true"
          Condition="Exists('$(MSBuildProjectDirectory)\Assets')" />
  </Target>
</Project>
""";
        await File.WriteAllTextAsync(csprojPath, csprojXml);

        var sourcePath = Path.Combine(projDir, "Plugin.cs");
        var sourceCode = """
namespace AssetProj;

public class Plugin
{
    public string Name => "AssetProj";
}
""";
        await File.WriteAllTextAsync(sourcePath, sourceCode);

        var runner = new DotNetBuildRunnerAdapter();

        // Act
        var result = await runner.BuildModAsync(csprojPath);

        // Assert
        Assert.True(result.IsSuccess);
        var report = result.Value;
        Assert.True(report.IsSuccess, report.RawOutput);

        var deployedDll = Path.Combine(pluginsDir, "AssetProj.dll");
        var deployedAsset = Path.Combine(pluginsDir, "Assets", "custom_texture.png");
        var deployedSubAsset = Path.Combine(pluginsDir, "Assets", "Icons", "icon.png");

        Assert.True(File.Exists(deployedDll), $"DLL not deployed to {deployedDll}");
        Assert.True(File.Exists(deployedAsset), $"Asset not deployed to {deployedAsset}");
        Assert.True(File.Exists(deployedSubAsset), $"Sub-asset not deployed to {deployedSubAsset}");
    }
}
