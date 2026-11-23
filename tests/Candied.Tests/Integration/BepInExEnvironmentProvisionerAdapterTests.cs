using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using Candied.Core.Models;
using Candied.Primitives;
using Candied.Workspace.Adapters;
using Xunit;

namespace Candied.Tests.Integration;

public class BepInExEnvironmentProvisionerAdapterTests : IDisposable
{
    private readonly string _testRoot;
    private readonly string _gameRoot;
    private readonly string _workspaceRoot;
    private readonly CandiedStorageLayout _storageLayout;

    public BepInExEnvironmentProvisionerAdapterTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), "Candied_Provisioning_Test_" + Guid.NewGuid().ToString("N"));
        _gameRoot = Path.Combine(_testRoot, "TargetGame");
        _workspaceRoot = Path.Combine(_testRoot, "Workspace");

        Directory.CreateDirectory(_gameRoot);
        Directory.CreateDirectory(_workspaceRoot);

        _storageLayout = new CandiedStorageLayout(_workspaceRoot);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testRoot))
            {
                Directory.Delete(_testRoot, true);
            }
        }
        catch
        {
            // Non-critical test directory cleanup
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task Provision_And_Rollback_Environment_Operates_Cleanly_With_Manifest()
    {
        // 1. Prepare fake local cached BepInEx zip
        var cacheDir = _storageLayout.BepInExCacheDir;
        Directory.CreateDirectory(cacheDir);
        var zipPath = Path.Combine(cacheDir, "BepInEx_x64_5.4.23.2.zip");

        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var winhttpEntry = zip.CreateEntry("winhttp.dll");
            using (var writer = new StreamWriter(winhttpEntry.Open()))
            {
                writer.Write("fake winhttp proxy bytes");
            }

            var doorstopEntry = zip.CreateEntry("doorstop_config.ini");
            using (var writer = new StreamWriter(doorstopEntry.Open()))
            {
                writer.Write("[General]\ntarget_assembly=BepInEx/core/BepInEx.Preloader.dll");
            }

            var coreEntry = zip.CreateEntry("BepInEx/core/BepInEx.dll");
            using (var writer = new StreamWriter(coreEntry.Open()))
            {
                writer.Write("fake bepinex core bytes");
            }
        }

        var gameEnv = new GameEnvironment(
            Id: new GameId("TestGame"),
            GameName: "TestGame",
            GameRootPath: _gameRoot,
            ManagedPath: Path.Combine(_gameRoot, "Managed"),
            EngineType: UnityEngineType.Mono,
            Architecture: "x64",
            TargetFramework: "net472",
            BepInExInfo: new BepInExInstallation(false, null, null),
            DiscoveredAssemblies: new List<DiscoveredAssembly>(),
            EnvironmentWarnings: new List<string>()
        );

        var adapter = new BepInExEnvironmentProvisionerAdapter();

        // 2. Provision environment
        var spec = new ProvisioningSpec(TargetVersion: "5.4.23.2", EnableConsoleLogging: true);
        var provisionResult = await adapter.ProvisionEnvironmentAsync(gameEnv, spec, _storageLayout);

        Assert.True(provisionResult.IsSuccess);
        Assert.Equal("BepInEx", provisionResult.Value.ModLoaderName);
        Assert.Equal("5.4.23.2", provisionResult.Value.Version);
        Assert.Equal("x64", provisionResult.Value.Architecture);
        Assert.True(provisionResult.Value.ConsoleLoggingConfigured);

        // Verify files in game root
        Assert.True(File.Exists(Path.Combine(_gameRoot, "winhttp.dll")));
        Assert.True(File.Exists(Path.Combine(_gameRoot, "doorstop_config.ini")));
        Assert.True(File.Exists(Path.Combine(_gameRoot, "BepInEx", "core", "BepInEx.dll")));
        Assert.True(File.Exists(Path.Combine(_gameRoot, "BepInEx", "config", "BepInEx.cfg")));
        Assert.True(File.Exists(Path.Combine(_gameRoot, ".candied_manifest.json")));

        // 3. Rollback environment
        var rollbackResult = await adapter.RollbackEnvironmentAsync(gameEnv, _storageLayout);

        Assert.True(rollbackResult.IsSuccess);
        Assert.True(rollbackResult.Value.FilesRemovedCount >= 4);

        // Verify all provisioned files are completely gone
        Assert.False(File.Exists(Path.Combine(_gameRoot, "winhttp.dll")));
        Assert.False(File.Exists(Path.Combine(_gameRoot, "doorstop_config.ini")));
        Assert.False(File.Exists(Path.Combine(_gameRoot, "BepInEx", "core", "BepInEx.dll")));
        Assert.False(File.Exists(Path.Combine(_gameRoot, ".candied_manifest.json")));
        Assert.False(Directory.Exists(Path.Combine(_gameRoot, "BepInEx")));
    }
}
