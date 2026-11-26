using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Threading.Tasks;
using Candied.Agent;
using Candied.Core.Capabilities;
using Candied.Core.Models;
using Candied.Core.Ports;
using Candied.Infrastructure;
using Candied.Platforms.Unity.Mono;
using Candied.Primitives;
using Candied.Vision;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Candied.Tests.Integration;

public sealed class UnityMonoPlatformIntegrationTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ServiceProvider _serviceProvider;
    private readonly CandiedStorageLayout _layout;

    public UnityMonoPlatformIntegrationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "Candied_UnityMonoInt_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _layout = new CandiedStorageLayout(_tempDir);

        var services = new ServiceCollection();
        services.AddCandiedCoreAndInfrastructure(_tempDir);
        services.AddVisionInfrastructure();
        services.AddUnityMonoPlatform();
        services.AddTransient<AgentWorkbenchService>();
        _serviceProvider = services.BuildServiceProvider();
    }

    public void Dispose()
    {
        _serviceProvider.Dispose();
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch
        {
        }
    }

    [Fact]
    public async Task ProvisionAndRollback_WithCachedBepInExZip_TracksAndRemovesManifestFilesCleanly()
    {
        string gameRoot = Path.Combine(_tempDir, "ProvisionGame");
        string managedDir = Path.Combine(gameRoot, "ProvisionGame_Data", "Managed");
        Directory.CreateDirectory(managedDir);
        await File.WriteAllBytesAsync(Path.Combine(managedDir, "Assembly-CSharp.dll"), await File.ReadAllBytesAsync(typeof(Result<,>).Assembly.Location));

        // Seed local BepInEx cache archive so test runs 100% offline
        string cacheDir = _layout.GetLoaderCacheDir("bepinex-mono");
        Directory.CreateDirectory(cacheDir);
        string zipPath = Path.Combine(cacheDir, "BepInEx_win_x64_5.4.23.2.zip");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            using (var w1 = new StreamWriter(zip.CreateEntry("winhttp.dll").Open()))
            {
                await w1.WriteAsync("fake winhttp");
            }

            using (var w2 = new StreamWriter(zip.CreateEntry("doorstop_config.ini").Open()))
            {
                await w2.WriteAsync("[General]\ntarget_assembly=BepInEx/core/BepInEx.Preloader.dll");
            }

            using (var w3 = new StreamWriter(zip.CreateEntry("BepInEx/core/BepInEx.dll").Open()))
            {
                await w3.WriteAsync("fake core");
            }
        }

        var registry = _serviceProvider.GetRequiredService<ITargetPlatformRegistry>();
        var inspectRes = await registry.InspectTargetAsync(gameRoot, null, _layout);
        Assert.True(inspectRes.IsSuccess);

        var platform = registry.GetPlatformById("unity.mono")!;
        var provRes = await platform.EnvironmentSetup!.ProvisionAsync(inspectRes.Value, new ProvisioningSpec(TargetVersion: "5.4.23.2"), _layout);
        Assert.True(provRes.IsSuccess, provRes.IsFailure ? provRes.Error.Message : string.Empty);
        Assert.True(File.Exists(Path.Combine(gameRoot, "winhttp.dll")));
        Assert.True(File.Exists(Path.Combine(gameRoot, "BepInEx", "config", "BepInEx.cfg")));
        Assert.True(File.Exists(Path.Combine(gameRoot, ".candied_manifest.json")));

        var rbRes = await platform.EnvironmentSetup.RollbackAsync(inspectRes.Value, _layout);
        Assert.True(rbRes.IsSuccess);
        Assert.False(File.Exists(Path.Combine(gameRoot, "winhttp.dll")));
        Assert.False(File.Exists(Path.Combine(gameRoot, ".candied_manifest.json")));
    }

    [Fact]
    public async Task BuildMod_And_UnityAddReferenceTool_CompileAndMutateCsprojCleanly()
    {
        string projDir = Path.Combine(_tempDir, "ValidMod");
        string gameRoot = Path.Combine(_tempDir, "FakeUnityGame");
        string managedDir = Path.Combine(gameRoot, "FakeUnityGame_Data", "Managed");
        Directory.CreateDirectory(projDir);
        Directory.CreateDirectory(managedDir);

        await File.WriteAllBytesAsync(Path.Combine(managedDir, "UnityEngine.AudioModule.dll"), [0x4D, 0x5A]);

        string csprojPath = Path.Combine(projDir, "ValidMod.csproj");
        string csprojXml = $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net9.0</TargetFramework>
                <Nullable>enable</Nullable>
                <GameRootPath>{gameRoot}</GameRootPath>
              </PropertyGroup>
            </Project>
            """;
        await File.WriteAllTextAsync(csprojPath, csprojXml);
        await File.WriteAllTextAsync(Path.Combine(projDir, "Sample.cs"), "namespace ValidMod; public class Sample { public int Add(int a, int b) => a + b; }");

        var workbench = _serviceProvider.GetRequiredService<AgentWorkbenchService>();

        // 1. Build succeeds
        string buildJson = await workbench.BuildModAsync(csprojPath);
        using var buildDoc = JsonDocument.Parse(buildJson);
        Assert.True(buildDoc.RootElement.GetProperty("success").GetBoolean());
        Assert.True(buildDoc.RootElement.GetProperty("report").GetProperty("isSuccess").GetBoolean());

        // 2. Dynamic platform tool 'unity-add-reference' adds AudioModule reference
        string refJson = await workbench.ExecutePlatformToolAsync(
            "unity-add-reference",
            new Dictionary<string, string?>
            {
                ["projectFilePath"] = csprojPath,
                ["moduleName"] = "UnityEngine.AudioModule",
                ["publicize"] = "true"
            });

        using var refDoc = JsonDocument.Parse(refJson);
        Assert.True(refDoc.RootElement.GetProperty("success").GetBoolean());
        string updatedXml = await File.ReadAllTextAsync(csprojPath);
        Assert.Contains("UnityEngine.AudioModule", updatedXml);
        Assert.Contains("<Publicize>true</Publicize>", updatedXml);
    }

    [Fact]
    public async Task ReadLogs_FiltersByMinimumSeverityAccurately()
    {
        string logFile = Path.Combine(_tempDir, "LogOutput.log");
        await File.WriteAllLinesAsync(logFile,
        [
            "[Info   :   BepInEx] Patcher loaded",
            "[Warning:  SpeedMod] Deprecated method",
            "[Error  :  SpeedMod] Patch exception"
        ]);

        var workbench = _serviceProvider.GetRequiredService<AgentWorkbenchService>();
        string json = await workbench.ReadLogsAsync(logFile, maxLines: 20, minLevel: LogLevel.Warning);

        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(2, doc.RootElement.GetProperty("count").GetInt32());
    }

    [Fact]
    public async Task ProvisionAndRollback_RejectZipSlipAndManifestPathTraversal()
    {
        string gameRoot = Path.Combine(_tempDir, "SecGame");
        string managedDir = Path.Combine(gameRoot, "SecGame_Data", "Managed");
        Directory.CreateDirectory(managedDir);
        await File.WriteAllBytesAsync(Path.Combine(managedDir, "Assembly-CSharp.dll"), [0x4D, 0x5A]);

        string outsideCanary = Path.Combine(_tempDir, "outside_canary.txt");
        await File.WriteAllTextAsync(outsideCanary, "must-survive");

        // 1. Zip-Slip attempt in cached archive must be rejected
        string cacheDir = _layout.GetLoaderCacheDir("bepinex-mono");
        Directory.CreateDirectory(cacheDir);
        string zipPath = Path.Combine(cacheDir, "BepInEx_win_x64_9.9.9.zip");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            using var w = new StreamWriter(zip.CreateEntry("../outside_escaped.txt").Open());
            await w.WriteAsync("malicious");
        }

        var registry = _serviceProvider.GetRequiredService<ITargetPlatformRegistry>();
        var inspectRes = await registry.InspectTargetAsync(gameRoot, null, _layout);
        Assert.True(inspectRes.IsSuccess);

        var platform = registry.GetPlatformById("unity.mono")!;
        var provRes = await platform.EnvironmentSetup!.ProvisionAsync(inspectRes.Value, new ProvisioningSpec(TargetVersion: "9.9.9"), _layout);
        Assert.True(provRes.IsFailure);
        Assert.False(File.Exists(Path.Combine(_tempDir, "outside_escaped.txt")));

        // 2. Tampered manifest with '../outside_canary.txt' must never delete files outside gameRoot
        string tamperedManifest = """
            {
              "targetName": "SecGame",
              "platformId": "unity.mono",
              "modLoaderVersion": "5.4.23.2",
              "architecture": "x64",
              "installedAtUtc": "2026-11-26T00:00:00Z",
              "installedRelativePaths": ["../outside_canary.txt"]
            }
            """;
        await File.WriteAllTextAsync(Path.Combine(gameRoot, ".candied_manifest.json"), tamperedManifest);

        var rbRes = await platform.EnvironmentSetup.RollbackAsync(inspectRes.Value, _layout);
        Assert.True(rbRes.IsSuccess);
        Assert.True(File.Exists(outsideCanary));
    }
}
