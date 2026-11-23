using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Candied.Core.Errors;
using Candied.Core.Models;
using Candied.Core.Ports;
using Candied.Primitives;
using Microsoft.Data.Sqlite;

namespace Candied.Decompiler.Adapters;

internal sealed class SqliteAssetIndexAdapter : IAssetIndex
{
    private readonly string? _overrideConnectionString;

    public SqliteAssetIndexAdapter(string? connectionString = null)
    {
        _overrideConnectionString = connectionString;
    }

    private string ResolveConnectionString(string? databasePath = null, string? gameName = null)
    {
        if (!string.IsNullOrEmpty(_overrideConnectionString))
        {
            return _overrideConnectionString;
        }

        string dbPath;
        if (!string.IsNullOrWhiteSpace(databasePath))
        {
            dbPath = databasePath;
        }
        else if (!string.IsNullOrWhiteSpace(gameName))
        {
            dbPath = Path.Combine(Directory.GetCurrentDirectory(), ".candied", "games", gameName, "index.db");
        }
        else
        {
            dbPath = Path.Combine(Directory.GetCurrentDirectory(), ".candied", "index.db");
        }

        var dir = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        return $"Data Source={dbPath}";
    }

    private static async Task InitializeSchemaAsync(SqliteConnection conn, CancellationToken ct)
    {
        const string ddl = """
            CREATE TABLE IF NOT EXISTS DiscoveredAssets (
                PathId INTEGER NOT NULL,
                ContainerPath TEXT NOT NULL,
                ContainerName TEXT NOT NULL,
                Name TEXT NOT NULL,
                AssetType TEXT NOT NULL,
                Width INTEGER NOT NULL,
                Height INTEGER NOT NULL,
                ByteSize INTEGER NOT NULL,
                Extension TEXT,
                PRIMARY KEY (ContainerPath, PathId)
            );

            CREATE INDEX IF NOT EXISTS IX_DiscoveredAssets_Name ON DiscoveredAssets(Name);
            CREATE INDEX IF NOT EXISTS IX_DiscoveredAssets_Type ON DiscoveredAssets(AssetType);
            CREATE INDEX IF NOT EXISTS IX_DiscoveredAssets_Container ON DiscoveredAssets(ContainerName);

            CREATE VIRTUAL TABLE IF NOT EXISTS FtsDiscoveredAssets USING fts5(
                Name,
                ContainerName,
                AssetType,
                content='DiscoveredAssets',
                content_rowid='rowid'
            );

            CREATE TRIGGER IF NOT EXISTS trg_assets_ai AFTER INSERT ON DiscoveredAssets BEGIN
                INSERT INTO FtsDiscoveredAssets(rowid, Name, ContainerName, AssetType)
                VALUES (new.rowid, new.Name, new.ContainerName, new.AssetType);
            END;

            CREATE TRIGGER IF NOT EXISTS trg_assets_ad AFTER DELETE ON DiscoveredAssets BEGIN
                INSERT INTO FtsDiscoveredAssets(FtsDiscoveredAssets, rowid, Name, ContainerName, AssetType)
                VALUES('delete', old.rowid, old.Name, old.ContainerName, old.AssetType);
            END;

            CREATE TRIGGER IF NOT EXISTS trg_assets_au AFTER UPDATE ON DiscoveredAssets BEGIN
                INSERT INTO FtsDiscoveredAssets(FtsDiscoveredAssets, rowid, Name, ContainerName, AssetType)
                VALUES('delete', old.rowid, old.Name, old.ContainerName, old.AssetType);
                INSERT INTO FtsDiscoveredAssets(rowid, Name, ContainerName, AssetType)
                VALUES (new.rowid, new.Name, new.ContainerName, new.AssetType);
            END;
        """;

        await using var cmd = new SqliteCommand(ddl, conn);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<Result<int, IndexError>> IndexAssetsAsync(
        GameEnvironment game,
        IReadOnlyList<DiscoveredAssetInfo> assets,
        string? databasePath = null,
        CancellationToken ct = default)
    {
        try
        {
            var connStr = ResolveConnectionString(databasePath, game?.GameName);
            await using var conn = new SqliteConnection(connStr);
            await conn.OpenAsync(ct);
            await InitializeSchemaAsync(conn, ct);

            await using var tx = (SqliteTransaction)await conn.BeginTransactionAsync(ct);

            const string insertSql = """
                INSERT INTO DiscoveredAssets (PathId, ContainerPath, ContainerName, Name, AssetType, Width, Height, ByteSize, Extension)
                VALUES (@PathId, @ContainerPath, @ContainerName, @Name, @AssetType, @Width, @Height, @ByteSize, @Extension)
                ON CONFLICT(ContainerPath, PathId) DO UPDATE SET
                    Name = excluded.Name,
                    AssetType = excluded.AssetType,
                    ContainerName = excluded.ContainerName,
                    Width = excluded.Width,
                    Height = excluded.Height,
                    ByteSize = excluded.ByteSize,
                    Extension = excluded.Extension;
            """;

            await using var cmd = new SqliteCommand(insertSql, conn, tx);
            cmd.Parameters.Add("@PathId", SqliteType.Integer);
            cmd.Parameters.Add("@ContainerPath", SqliteType.Text);
            cmd.Parameters.Add("@ContainerName", SqliteType.Text);
            cmd.Parameters.Add("@Name", SqliteType.Text);
            cmd.Parameters.Add("@AssetType", SqliteType.Text);
            cmd.Parameters.Add("@Width", SqliteType.Integer);
            cmd.Parameters.Add("@Height", SqliteType.Integer);
            cmd.Parameters.Add("@ByteSize", SqliteType.Integer);
            cmd.Parameters.Add("@Extension", SqliteType.Text);

            int count = 0;
            foreach (var asset in assets)
            {
                if (ct.IsCancellationRequested) break;

                cmd.Parameters["@PathId"].Value = asset.PathId;
                cmd.Parameters["@ContainerPath"].Value = asset.SourceContainerPath;
                cmd.Parameters["@ContainerName"].Value = asset.SourceContainer;
                cmd.Parameters["@Name"].Value = asset.Name;
                cmd.Parameters["@AssetType"].Value = asset.AssetType;
                cmd.Parameters["@Width"].Value = asset.Width;
                cmd.Parameters["@Height"].Value = asset.Height;
                cmd.Parameters["@ByteSize"].Value = asset.ByteSize;
                cmd.Parameters["@Extension"].Value = (object?)asset.Extension ?? DBNull.Value;

                await cmd.ExecuteNonQueryAsync(ct);
                count++;
            }

            await tx.CommitAsync(ct);
            return Result.Ok<int, IndexError>(count);
        }
        catch (Exception ex)
        {
            return Result.Fail<int, IndexError>(new IndexError($"Failed to index assets in SQLite: {ex.Message}"));
        }
    }

    public async Task<Result<IReadOnlyList<DiscoveredAssetInfo>, IndexError>> SearchAssetsAsync(
        AssetDiscoveryFilter filter,
        string? databasePath = null,
        CancellationToken ct = default)
    {
        var pageRes = await SearchAssetsPageAsync(filter, databasePath, ct).ConfigureAwait(false);
        return pageRes.IsSuccess
            ? Result.Ok<IReadOnlyList<DiscoveredAssetInfo>, IndexError>(pageRes.Value.Assets)
            : Result.Fail<IReadOnlyList<DiscoveredAssetInfo>, IndexError>(pageRes.Error);
    }

    public async Task<Result<AssetDiscoveryPage, IndexError>> SearchAssetsPageAsync(
        AssetDiscoveryFilter filter,
        string? databasePath = null,
        CancellationToken ct = default)
    {
        try
        {
            var connStr = ResolveConnectionString(databasePath);
            await using var conn = new SqliteConnection(connStr);
            await conn.OpenAsync(ct);
            await InitializeSchemaAsync(conn, ct);

            var conditions = new List<string>();
            var paramValues = new List<(string Name, object Value)>();

            if (!string.IsNullOrWhiteSpace(filter.AssetType))
            {
                conditions.Add("LOWER(AssetType) = LOWER(@AssetType)");
                paramValues.Add(("@AssetType", filter.AssetType));
            }

            if (!string.IsNullOrWhiteSpace(filter.ContainerName))
            {
                conditions.Add("ContainerName LIKE '%' || @ContainerName || '%'");
                paramValues.Add(("@ContainerName", filter.ContainerName));
            }

            if (filter.PathId.HasValue)
            {
                conditions.Add("PathId = @PathId");
                paramValues.Add(("@PathId", filter.PathId.Value));
            }

            if (!string.IsNullOrWhiteSpace(filter.SearchPattern) && filter.SearchPattern != "*")
            {
                conditions.Add("Name LIKE '%' || @SearchPattern || '%'");
                paramValues.Add(("@SearchPattern", filter.SearchPattern));
            }

            var whereClause = conditions.Count > 0 ? "WHERE " + string.Join(" AND ", conditions) : "";

            int totalMatches;
            await using (var countCmd = new SqliteCommand($"SELECT COUNT(1) FROM DiscoveredAssets {whereClause};", conn))
            {
                foreach (var (name, value) in paramValues)
                {
                    countCmd.Parameters.AddWithValue(name, value);
                }
                totalMatches = Convert.ToInt32(await countCmd.ExecuteScalarAsync(ct), System.Globalization.CultureInfo.InvariantCulture);
            }

            var sql = $"""
                SELECT PathId, ContainerPath, ContainerName, Name, AssetType, Width, Height, ByteSize, Extension
                FROM DiscoveredAssets
                {whereClause}
                ORDER BY Name ASC
                LIMIT @Limit OFFSET @Offset;
            """;

            await using var cmd = new SqliteCommand(sql, conn);
            foreach (var (name, value) in paramValues)
            {
                cmd.Parameters.AddWithValue(name, value);
            }
            cmd.Parameters.AddWithValue("@Limit", filter.MaxResults <= 0 ? 50 : filter.MaxResults);
            cmd.Parameters.AddWithValue("@Offset", Math.Max(0, filter.Offset));

            var list = new List<DiscoveredAssetInfo>();
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var pathId = reader.GetInt64(0);
                var containerPath = reader.GetString(1);
                var containerName = reader.GetString(2);
                var name = reader.GetString(3);
                var assetType = reader.GetString(4);
                var width = reader.GetInt32(5);
                var height = reader.GetInt32(6);
                var byteSize = reader.GetInt64(7);
                var extension = reader.IsDBNull(8) ? null : reader.GetString(8);

                list.Add(new DiscoveredAssetInfo(
                    Name: name,
                    AssetType: assetType,
                    SourceContainer: containerName,
                    SourceContainerPath: containerPath,
                    PathId: pathId,
                    Width: width,
                    Height: height,
                    ByteSize: byteSize,
                    Extension: extension));
            }

            return Result.Ok<AssetDiscoveryPage, IndexError>(new AssetDiscoveryPage(totalMatches, list));
        }
        catch (Exception ex)
        {
            return Result.Fail<AssetDiscoveryPage, IndexError>(
                new IndexError($"Failed to search assets from SQLite: {ex.Message}"));
        }
    }

    public async Task<Result<bool, IndexError>> HasIndexedAssetsAsync(
        string? databasePath = null,
        CancellationToken ct = default)
    {
        try
        {
            var connStr = ResolveConnectionString(databasePath);
            await using var conn = new SqliteConnection(connStr);
            await conn.OpenAsync(ct);

            // Check if table exists first
            const string checkTableSql = "SELECT COUNT(1) FROM sqlite_master WHERE type='table' AND name='DiscoveredAssets';";
            await using var checkCmd = new SqliteCommand(checkTableSql, conn);
            var tableExists = Convert.ToInt32(await checkCmd.ExecuteScalarAsync(ct), System.Globalization.CultureInfo.InvariantCulture) > 0;
            if (!tableExists)
            {
                return Result.Ok<bool, IndexError>(false);
            }

            const string countSql = "SELECT COUNT(1) FROM DiscoveredAssets LIMIT 1;";
            await using var countCmd = new SqliteCommand(countSql, conn);
            var count = Convert.ToInt32(await countCmd.ExecuteScalarAsync(ct), System.Globalization.CultureInfo.InvariantCulture);
            return Result.Ok<bool, IndexError>(count > 0);
        }
        catch (Exception ex)
        {
            return Result.Fail<bool, IndexError>(new IndexError($"Failed to check asset index status: {ex.Message}"));
        }
    }
}
