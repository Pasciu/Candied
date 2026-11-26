using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Candied.Vision.Hosting;
using Candied.Vision.Models;
using Candied.Vision.Ports;

namespace Candied.Vision.Adapters;

internal sealed class JsonRecentTargetsStoreAdapter : IRecentTargetsStore
{
    private const int MaxEntries = 20;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _filePath;

    public JsonRecentTargetsStoreAdapter(string? customFilePath = null)
    {
        _filePath = customFilePath ?? VisionConfigPaths.GetRecentTargetsPath();
    }

    public async Task<IReadOnlyList<RecentTargetEntry>> ListRecentAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_filePath))
        {
            return Array.Empty<RecentTargetEntry>();
        }

        try
        {
            string json = await File.ReadAllTextAsync(_filePath, ct).ConfigureAwait(false);
            var list = JsonSerializer.Deserialize<List<RecentTargetEntry>>(json, JsonOptions);
            return list != null
                ? list.OrderByDescending(e => e.LastUsedAtUtc).ToList()
                : Array.Empty<RecentTargetEntry>();
        }
        catch
        {
            return Array.Empty<RecentTargetEntry>();
        }
    }

    public async Task RecordRecentAsync(RecentTargetEntry entry, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var existing = (await ListRecentAsync(ct).ConfigureAwait(false)).ToList();
        existing.RemoveAll(e =>
            string.Equals(e.TargetPath, entry.TargetPath, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(e.RunnerPath ?? string.Empty, entry.RunnerPath ?? string.Empty, StringComparison.OrdinalIgnoreCase));

        existing.Insert(0, entry);
        if (existing.Count > MaxEntries)
        {
            existing = existing.Take(MaxEntries).ToList();
        }

        string? dir = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        string json = JsonSerializer.Serialize(existing, JsonOptions);
        string temp = _filePath + ".tmp";
        await File.WriteAllTextAsync(temp, json, ct).ConfigureAwait(false);
        File.Move(temp, _filePath, overwrite: true);
    }
}
