using System.Text;
using Candied.Core.Errors;
using Candied.Core.Models;
using Candied.Core.Ports;
using Candied.Primitives;
using Candied.Workspace.Rendering;

namespace Candied.Workspace.Adapters;

internal sealed class DiskProjectGeneratorAdapter : IProjectGenerator
{
    public async Task<Result<ModProjectManifest, ScaffoldError>> ScaffoldModProjectAsync(
        ModScaffoldSpec spec,
        CancellationToken ct = default)
    {
        if (spec is null)
        {
            return Result.Fail<ModProjectManifest, ScaffoldError>(
                new ScaffoldError("Mod scaffold specification cannot be null."));
        }

        if (string.IsNullOrWhiteSpace(spec.ModName))
        {
            return Result.Fail<ModProjectManifest, ScaffoldError>(
                new ScaffoldError("Mod name cannot be empty."));
        }

        if (string.IsNullOrWhiteSpace(spec.OutputDirectory))
        {
            return Result.Fail<ModProjectManifest, ScaffoldError>(
                new ScaffoldError("Output directory cannot be empty."));
        }

        if (string.IsNullOrWhiteSpace(spec.PluginGuid.Value))
        {
            return Result.Fail<ModProjectManifest, ScaffoldError>(
                new ScaffoldError("Plugin GUID cannot be empty."));
        }

        if (spec.TargetGame is null)
        {
            return Result.Fail<ModProjectManifest, ScaffoldError>(
                new ScaffoldError("Target game environment cannot be null."));
        }

        try
        {
            var modDir = Path.GetFileName(spec.OutputDirectory).Equals(spec.ModName, StringComparison.OrdinalIgnoreCase)
                ? spec.OutputDirectory
                : Path.Combine(spec.OutputDirectory, spec.ModName);

            var patchesDir = Path.Combine(modDir, "Patches");

            Directory.CreateDirectory(modDir);
            Directory.CreateDirectory(patchesDir);

            var projectFilePath = Path.Combine(modDir, $"{spec.ModName}.csproj");
            var mainPluginSourcePath = Path.Combine(modDir, "Plugin.cs");
            var rootReadmePath = Path.Combine(modDir, "README.md");
            var examplePatchPath = Path.Combine(patchesDir, "ExamplePatch.cs");
            var patchesReadmePath = Path.Combine(patchesDir, "README.md");
            var postBuildDeployPath = Path.Combine(spec.TargetGame.GameRootPath, "BepInEx", "plugins", spec.ModName);

            var csprojContent = CsprojTemplateRenderer.Render(spec);
            var pluginContent = PluginTemplateRenderer.Render(spec);
            var rootReadmeContent = ModRootReadmeTemplateRenderer.Render(spec);
            var patchContent = HarmonyPatchTemplateRenderer.Render(spec);
            var patchesReadmeContent = PatchesReadmeTemplateRenderer.Render(spec);

            await File.WriteAllTextAsync(projectFilePath, csprojContent, Encoding.UTF8, ct).ConfigureAwait(false);
            await File.WriteAllTextAsync(mainPluginSourcePath, pluginContent, Encoding.UTF8, ct).ConfigureAwait(false);
            await File.WriteAllTextAsync(rootReadmePath, rootReadmeContent, Encoding.UTF8, ct).ConfigureAwait(false);
            await File.WriteAllTextAsync(examplePatchPath, patchContent, Encoding.UTF8, ct).ConfigureAwait(false);
            await File.WriteAllTextAsync(patchesReadmePath, patchesReadmeContent, Encoding.UTF8, ct).ConfigureAwait(false);

            if (spec.IncludeAssetPipeline)
            {
                var assetsDir = Path.Combine(modDir, "Assets");
                Directory.CreateDirectory(assetsDir);

                var assetLoaderPath = Path.Combine(modDir, "AssetLoader.cs");
                var assetLoaderContent = AssetLoaderTemplateRenderer.Render(spec);
                var assetsReadmePath = Path.Combine(assetsDir, "README.md");
                var assetsReadmeContent = AssetsReadmeTemplateRenderer.Render(spec);

                await File.WriteAllTextAsync(assetLoaderPath, assetLoaderContent, Encoding.UTF8, ct).ConfigureAwait(false);
                await File.WriteAllTextAsync(assetsReadmePath, assetsReadmeContent, Encoding.UTF8, ct).ConfigureAwait(false);
            }

            var manifest = new ModProjectManifest(
                ProjectFilePath: projectFilePath,
                MainPluginSourcePath: mainPluginSourcePath,
                PostBuildDeployPath: postBuildDeployPath
            );

            return Result.Ok<ModProjectManifest, ScaffoldError>(manifest);
        }
        catch (OperationCanceledException)
        {
            return Result.Fail<ModProjectManifest, ScaffoldError>(
                new ScaffoldError("Scaffold operation was cancelled."));
        }
        catch (Exception ex)
        {
            return Result.Fail<ModProjectManifest, ScaffoldError>(
                new ScaffoldError($"Failed to scaffold mod project: {ex.Message}"));
        }
    }
}
