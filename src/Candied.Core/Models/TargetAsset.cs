using System.Collections.Generic;

namespace Candied.Core.Models;

public sealed record DiscoveredAssetInfo(
    string Name,
    string AssetType,
    string SourceContainer,
    string SourceContainerPath,
    long AssetKey,
    int Width,
    int Height,
    long ByteSize = 0,
    string? Extension = null
);

public sealed record ExtractedAssetInfo(
    string Name,
    string AssetType,
    string OutputFilePath,
    long FileSizeBytes,
    int Width,
    int Height,
    long AssetKey = 0,
    string? SourceContainer = null
);

public sealed record AssetDiscoveryFilter(
    string? SearchPattern = null,
    string? AssetType = null,
    int MaxResults = 50,
    string? ContainerName = null,
    long? AssetKey = null,
    int Offset = 0
);

public sealed record AssetDiscoveryPage(
    int TotalMatches,
    IReadOnlyList<DiscoveredAssetInfo> Assets
);

public sealed record AssetExtractionFilter(
    string? SearchPattern = null,
    string? AssetType = null,
    int MaxResults = 50,
    string? ContainerName = null,
    long? AssetKey = null,
    bool DisambiguateCollisions = true
);
