using System.IO;
using Candied.Primitives;

namespace Candied.Core.Models;

public enum UnityEngineType
{
    Mono,
    Il2Cpp
}

public enum AssemblyTier
{
    CoreGame,
    GameModular,
    AuxiliaryOrThirdParty
}

public record DiscoveredAssembly(
    string Name,
    string FullPath,
    long SizeBytes,
    AssemblyTier Tier,
    string Reason,
    bool IsDecompiled = false
);

public record BepInExInstallation(
    bool IsInstalled,
    string? DetectedVersion = null,
    string? BepInExRootPath = null
);

public record GameEnvironment(
    GameId Id,
    string GameName,
    string GameRootPath,
    string ManagedPath,
    UnityEngineType EngineType,
    string Architecture,
    string? TargetFramework,
    BepInExInstallation BepInExInfo,
    IReadOnlyList<DiscoveredAssembly> DiscoveredAssemblies,
    IReadOnlyList<string> EnvironmentWarnings
)
{
    public string? UnityVersion { get; init; }
    // Convenience constructor for backwards compatibility with tests and callers
    public GameEnvironment(
        GameId Id,
        string GameName,
        string GameRootPath,
        string ManagedPath,
        UnityEngineType EngineType,
        IReadOnlyList<string> GameAssemblyPaths,
        bool HasBepInExInstalled
    ) : this(
        Id,
        GameName,
        GameRootPath,
        ManagedPath,
        EngineType,
        "x64",
        null,
        new BepInExInstallation(HasBepInExInstalled, HasBepInExInstalled ? "5.4.21.0" : null, null),
        GameAssemblyPaths.Select(p => new DiscoveredAssembly(
            Path.GetFileName(p),
            p,
            0,
            p.Contains("Assembly-CSharp") ? AssemblyTier.CoreGame : AssemblyTier.GameModular,
            "Legacy constructor",
            false)).ToList(),
        Array.Empty<string>()
    )
    {
    }

    public IReadOnlyList<string> GameAssemblyPaths => DiscoveredAssemblies
        .Where(a => a.Tier != AssemblyTier.AuxiliaryOrThirdParty)
        .Select(a => a.FullPath)
        .ToList();

    public bool HasBepInExInstalled => BepInExInfo.IsInstalled;
}
