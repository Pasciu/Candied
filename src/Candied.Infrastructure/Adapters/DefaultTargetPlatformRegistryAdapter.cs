using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Candied.Core.Capabilities;
using Candied.Core.Errors;
using Candied.Core.Models;
using Candied.Core.Ports;
using Candied.Infrastructure.Internal;
using Candied.Primitives;

namespace Candied.Infrastructure.Adapters;

internal sealed class DefaultTargetPlatformRegistryAdapter : ITargetPlatformRegistry
{
    private readonly List<ITargetPlatform> _platforms;
    private readonly GenericTargetPlatform _fallbackPlatform = new();

    public DefaultTargetPlatformRegistryAdapter(IEnumerable<ITargetPlatform> platforms)
    {
        ArgumentNullException.ThrowIfNull(platforms);
        _platforms = platforms
            .OrderByDescending(p => p.Priority)
            .ToList();

        if (!_platforms.Any(p => string.Equals(p.PlatformId, _fallbackPlatform.PlatformId, StringComparison.OrdinalIgnoreCase)))
        {
            _platforms.Add(_fallbackPlatform);
        }
    }

    public IReadOnlyList<ITargetPlatform> GetAllPlatforms() => _platforms;

    public ITargetPlatform? GetPlatformById(string platformId)
    {
        if (string.IsNullOrWhiteSpace(platformId))
        {
            return null;
        }

        return _platforms.FirstOrDefault(p =>
            string.Equals(p.PlatformId, platformId.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    public ITargetPlatform ResolvePlatform(string targetPath, string? runnerPath = null)
    {
        foreach (var platform in _platforms)
        {
            if (platform.CanHandle(targetPath, runnerPath))
            {
                return platform;
            }
        }

        return _fallbackPlatform;
    }

    public ITargetPlatform? ResolveModBuildPlatform(string projectOrManifestPath)
    {
        if (string.IsNullOrWhiteSpace(projectOrManifestPath))
        {
            return null;
        }

        foreach (var platform in _platforms)
        {
            if (platform.ModWorkspace != null && platform.ModWorkspace.CanBuildProject(projectOrManifestPath))
            {
                return platform;
            }
        }

        return null;
    }

    public Task<Result<TargetDescriptor, DetectionError>> InspectTargetAsync(
        string targetPath,
        string? runnerPath,
        CandiedStorageLayout storageLayout,
        CancellationToken ct = default)
    {
        var platform = ResolvePlatform(targetPath, runnerPath);
        return platform.InspectAsync(targetPath, runnerPath, storageLayout, ct);
    }
}
