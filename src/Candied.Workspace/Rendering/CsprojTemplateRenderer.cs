using System.Globalization;
using System.Text;
using Candied.Core.Models;

namespace Candied.Workspace.Rendering;

internal static class CsprojTemplateRenderer
{
    public static string Render(ModScaffoldSpec spec)
    {
        var assemblyPaths = new List<string>();
        if (spec.TargetGame.GameAssemblyPaths != null && spec.TargetGame.GameAssemblyPaths.Count > 0)
        {
            assemblyPaths.AddRange(spec.TargetGame.GameAssemblyPaths);
        }
        else if (!string.IsNullOrWhiteSpace(spec.TargetGame.ManagedPath) && Directory.Exists(spec.TargetGame.ManagedPath))
        {
            assemblyPaths.AddRange(Directory.GetFiles(spec.TargetGame.ManagedPath, "*.dll", SearchOption.TopDirectoryOnly));
        }

        // Ensure core UnityEngine assemblies from ManagedPath are present for mod references
        if (!string.IsNullOrWhiteSpace(spec.TargetGame.ManagedPath) && Directory.Exists(spec.TargetGame.ManagedPath))
        {
            var unityEngineDll = Path.Combine(spec.TargetGame.ManagedPath, "UnityEngine.dll");
            if (File.Exists(unityEngineDll) && !assemblyPaths.Contains(unityEngineDll, StringComparer.OrdinalIgnoreCase))
            {
                assemblyPaths.Add(unityEngineDll);
            }

            var unityCoreModuleDll = Path.Combine(spec.TargetGame.ManagedPath, "UnityEngine.CoreModule.dll");
            if (File.Exists(unityCoreModuleDll) && !assemblyPaths.Contains(unityCoreModuleDll, StringComparer.OrdinalIgnoreCase))
            {
                assemblyPaths.Add(unityCoreModuleDll);
            }

            if (spec.IncludeAssetPipeline)
            {
                var imageConversionDll = Path.Combine(spec.TargetGame.ManagedPath, "UnityEngine.ImageConversionModule.dll");
                if (File.Exists(imageConversionDll) && !assemblyPaths.Contains(imageConversionDll, StringComparer.OrdinalIgnoreCase))
                {
                    assemblyPaths.Add(imageConversionDll);
                }
            }
        }

        bool hasAssemblyCSharpOnDisk = assemblyPaths.Any(p =>
            Path.GetFileNameWithoutExtension(p).Equals("Assembly-CSharp", StringComparison.OrdinalIgnoreCase)) ||
            (!string.IsNullOrWhiteSpace(spec.TargetGame.ManagedPath) &&
             File.Exists(Path.Combine(spec.TargetGame.ManagedPath, "Assembly-CSharp.dll")));

        var candidatePublicized = new List<string>();
        if (spec.PublicizedAssemblyNames != null && spec.PublicizedAssemblyNames.Count > 0)
        {
            candidatePublicized.AddRange(spec.PublicizedAssemblyNames);
        }

        // If candidate list is empty OR only has "Assembly-CSharp" when Assembly-CSharp.dll does not exist:
        if (candidatePublicized.Count == 0 ||
            (candidatePublicized.Count == 1 &&
             candidatePublicized[0].Equals("Assembly-CSharp", StringComparison.OrdinalIgnoreCase) &&
             !hasAssemblyCSharpOnDisk))
        {
            candidatePublicized.Clear();
            if (hasAssemblyCSharpOnDisk)
            {
                candidatePublicized.Add("Assembly-CSharp");
            }
            else
            {
                // Default to detected game assemblies from GameAssemblyPaths
                foreach (var path in assemblyPaths)
                {
                    var name = Path.GetFileNameWithoutExtension(path);
                    if (!name.StartsWith("UnityEngine", StringComparison.OrdinalIgnoreCase) &&
                        !name.Equals("mscorlib", StringComparison.OrdinalIgnoreCase) &&
                        !name.StartsWith("System", StringComparison.OrdinalIgnoreCase))
                    {
                        candidatePublicized.Add(name);
                    }
                }

                // If no game assemblies were detected (e.g. empty mock test), retain Assembly-CSharp as placeholder
                if (candidatePublicized.Count == 0)
                {
                    candidatePublicized.Add("Assembly-CSharp");
                }
            }
        }

        var publicizedSet = new HashSet<string>(candidatePublicized, StringComparer.OrdinalIgnoreCase);

        var referencesSb = new StringBuilder();
        foreach (var path in assemblyPaths)
        {
            var asmName = Path.GetFileNameWithoutExtension(path);
            bool isPublicized = publicizedSet.Contains(asmName);

            if (isPublicized)
            {
                referencesSb.AppendLine(CultureInfo.InvariantCulture, $"    <Reference Include=\"{asmName}\">");
                referencesSb.AppendLine(CultureInfo.InvariantCulture, $"      <HintPath>{path}</HintPath>");
                referencesSb.AppendLine("      <Publicize>true</Publicize>");
                referencesSb.AppendLine("      <Private>false</Private>");
                referencesSb.AppendLine("    </Reference>");
            }
            else if (asmName.Equals("Assembly-CSharp", StringComparison.OrdinalIgnoreCase) ||
                     asmName.StartsWith("UnityEngine", StringComparison.OrdinalIgnoreCase))
            {
                referencesSb.AppendLine(CultureInfo.InvariantCulture, $"    <Reference Include=\"{asmName}\">");
                referencesSb.AppendLine(CultureInfo.InvariantCulture, $"      <HintPath>{path}</HintPath>");
                referencesSb.AppendLine("      <Private>false</Private>");
                referencesSb.AppendLine("    </Reference>");
            }
        }

        foreach (var pubName in publicizedSet)
        {
            if (!assemblyPaths.Any(p => Path.GetFileNameWithoutExtension(p).Equals(pubName, StringComparison.OrdinalIgnoreCase)))
            {
                var fallbackPath = !string.IsNullOrWhiteSpace(spec.TargetGame.ManagedPath)
                    ? Path.Combine(spec.TargetGame.ManagedPath, $"{pubName}.dll")
                    : $"{pubName}.dll";

                referencesSb.AppendLine(CultureInfo.InvariantCulture, $"    <Reference Include=\"{pubName}\">");
                referencesSb.AppendLine(CultureInfo.InvariantCulture, $"      <HintPath>{fallbackPath}</HintPath>");
                referencesSb.AppendLine("      <Publicize>true</Publicize>");
                referencesSb.AppendLine("      <Private>false</Private>");
                referencesSb.AppendLine("    </Reference>");
            }
        }

        var version = string.IsNullOrWhiteSpace(spec.Version) ? "1.0.0" : spec.Version;

        var looseAssetsXml = spec.IncludeAssetPipeline
            ? """

  <ItemGroup>
    <None Update="Assets\**" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
"""
            : string.Empty;

        var deployAssetsTargetXml = spec.IncludeAssetPipeline
            ? """

    <MakeDir Directories="$(ModPluginsDeployDir)Assets" Condition="Exists('$(MSBuildProjectDirectory)\Assets')" />
    <ItemGroup Condition="Exists('$(MSBuildProjectDirectory)\Assets')">
      <ModAssets Include="$(MSBuildProjectDirectory)\Assets\**\*.*" />
    </ItemGroup>
    <Copy SourceFiles="@(ModAssets)"
          DestinationFolder="$(ModPluginsDeployDir)Assets\%(RecursiveDir)"
          SkipUnchangedFiles="true"
          Condition="Exists('$(MSBuildProjectDirectory)\Assets')" />
"""
            : string.Empty;

        return $"""
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.1</TargetFramework>
    <AssemblyName>{spec.ModName}</AssemblyName>
    <Product>{spec.ModName}</Product>
    <Version>{version}</Version>
    <Nullable>enable</Nullable>
    <LangVersion>latest</LangVersion>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <NoWarn>$(NoWarn);CA1707;NU1900;NU1901;NU1902;NU1903;NU1904</NoWarn>
    <RestoreAdditionalProjectSources>
      https://nuget.bepinex.dev/v3/index.json
    </RestoreAdditionalProjectSources>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="BepInEx.Core" Version="5.4.21" />
    <PackageReference Include="BepInEx.AssemblyPublicizer.MSBuild" Version="0.4.3" PrivateAssets="all" />
  </ItemGroup>

  <ItemGroup>
{referencesSb.ToString().TrimEnd()}
  </ItemGroup>{looseAssetsXml}

  <PropertyGroup>
    <GameRootPath>{spec.TargetGame.GameRootPath}</GameRootPath>
    <ModPluginsDeployDir>$(GameRootPath)\BepInEx\plugins\{spec.ModName}\</ModPluginsDeployDir>
  </PropertyGroup>

  <Target Name="DeployToBepInEx" AfterTargets="Build" Condition="Exists('$(GameRootPath)')">
    <MakeDir Directories="$(ModPluginsDeployDir)" />
    <Copy SourceFiles="$(TargetPath)" DestinationFolder="$(ModPluginsDeployDir)" SkipUnchangedFiles="true" />{deployAssetsTargetXml}
  </Target>
</Project>
""";
    }
}
