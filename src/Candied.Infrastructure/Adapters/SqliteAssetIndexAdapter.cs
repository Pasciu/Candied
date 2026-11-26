using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Candied.Core.Errors;
using Candied.Core.Models;
using Candied.Core.Ports;
using Candied.Primitives;

namespace Candied.Infrastructure.Adapters;

internal sealed class SqliteAssetIndexAdapter : IAssetIndex
{
    private readonly string? _overrideConnectionString;

    public SqliteAssetIndexAdapter(string? connectionString = null)
    {
        _overrideConnectionString = connectionString;
    }

    private string ResolveConnectionString(string? databasePath = null, string? targetName = null)
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
        else if (!string.IsNullOrWhiteSpace(targetName))
        {
            dbPath = Path.Combine(Directory.GetCurrentDirectory(), ".candied", "games", targetName, "index.db");
        }
        else
        {
            dbPath = Path.Combine(Directory.GetCurrentDirectory(), ".candied", "index.db");
        }

        string? dir = Path.GetDirectoryName(dbPath);
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
                AssetKey INTEGER NOT NULL,
                ContainerPath TEXT NOT NULL,
                ContainerName TEXT NOT NULL,
                Name TEXT NOT NULL,
                AssetType TEXT NOT NULL,
                Width INTEGER NOT NULL,
                Height INTEGER NOT NULL,
                ByteSize INTEGER NOT NULL,
                Extension TEXT,
                PRIMARY KEY (ContainerPath, AssetKey)
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
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<Result<int, IndexError>> IndexAssetsAsync(
        TargetDescriptor target,
        IReadOnlyList<DiscoveredAssetInfo> assets,
        string? databasePath = null,
        CancellationToken ct = default)
    {
        try
        {
            string connStr = ResolveConnectionString(databasePath, target?.Name);
            await using var conn = new SqliteConnection(connStr);
            await conn.OpenAsync(ct).ConfigureAwait(false);
            await InitializeSchemaAsync(conn, ct).ConfigureAwait(false);

            await using var tx = (SqliteTransaction)await conn.BeginTransactionAsync(ct).ConfigureAwait(false);

            const string insertSql = """
                INSERT INTO DiscoveredAssets (AssetKey, ContainerPath, ContainerName, Name, AssetType, Width, Height, ByteSize, Extension)
                VALUES (@AssetKey, @ContainerPath, @ContainerName, @Name, @AssetType, @Width, @Height, @ByteSize, @Extension)
                ON CONFLICT(ContainerPath, AssetKey) DO UPDATE SET
                    Name = excluded.Name,
                    AssetType = excluded.AssetType,
                    ContainerName = excluded.ContainerName,
                    Width = excluded.Width,
                    Height = excluded.Height,
                    ByteSize = excluded.ByteSize,
                    Extension = excluded.Extension;
            """;

            await using var cmd = new SqliteCommand(insertSql, conn, tx);
            cmd.Parameters.Add("@AssetKey", SqliteType.Integer);
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

                cmd.Parameters["@AssetKey"].Value = asset.AssetKey;
                cmd.Parameters["@ContainerPath"].Value = asset.SourceContainerPath;
                cmd.Parameters["@ContainerName"].Value = asset.SourceContainer;
                cmd.Parameters["@Name"].Value = asset.Name;
                cmd.Parameters["@AssetType"].Value = asset.AssetType;
                cmd.Parameters["@Width"].Value = asset.Width;
                cmd.Parameters["@Height"].Value = asset.Height;
                cmd.Parameters["@ByteSize"].Value = asset.ByteSize;
                cmd.Parameters["@Extension"].Value = (object?)asset.Extension ?? DBNull.Value;

                await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                count++;
            }

            await tx.CommitAsync(ct).ConfigureAwait(false);
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
            string connStr = ResolveConnectionString(databasePath);
            await using var conn = new SqliteConnection(connStr);
            await conn.OpenAsync(ct).ConfigureAwait(false);
            await InitializeSchemaAsync(conn, ct).ConfigureAwait(false);

            var conditions = new List<string>();
            var paramValues = new List<(string Name, object Value)>();

            if (!string.IsNullOrWhiteSpace(filter.AssetType) &&
                !string.Equals(filter.AssetType, "All", StringComparison.OrdinalIgnoreCase))
            {
                conditions.Add("LOWER(AssetType) = LOWER(@AssetType)");
                paramValues.Add(("@AssetType", filter.AssetType));
            }

            if (!string.IsNullOrWhiteSpace(filter.ContainerName))
            {
                conditions.Add("ContainerName LIKE '%' || @ContainerName || '%'");
                paramValues.Add(("@ContainerName", filter.ContainerName));
            }

            if (filter.AssetKey.HasValue)
            {
                conditions.Add("AssetKey = @AssetKey");
                paramValues.Add(("@AssetKey", filter.AssetKey.Value));
            }

            if (!string.IsNullOrWhiteSpace(filter.SearchPattern) && filter.SearchPattern != "*")
            {
                conditions.Add("Name LIKE '%' || @SearchPattern || '%'");
                paramValues.Add(("@SearchPattern", filter.SearchPattern));
            }

            string whereClause = conditions.Count > 0 ? "WHERE " + string.Join(" AND ", conditions) : string.Empty;

            int totalMatches;
            await using (var countCmd = new SqliteCommand($"SELECT COUNT(1) FROM DiscoveredAssets {whereClause};", conn))
            {
                foreach (var (name, value) in paramValues)
                {
                    countCmd.Parameters.AddWithValue(name, value);
                }

                totalMatches = Convert.ToInt32(await countCmd.ExecuteScalarAsync(ct).ConfigureAwait(false), CultureInfo.InvariantCulture);
            }

            string sql = $"""
                SELECT AssetKey, ContainerPath, ContainerName, Name, AssetType, Width, Height, ByteSize, Extension
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
            await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                list.Add(new DiscoveredAssetInfo(
                    Name: reader.GetString(3),
                    AssetType: reader.GetString(4),
                    SourceContainer: reader.GetString(2),
                    SourceContainerPath: reader.GetString(1),
                    AssetKey: reader.GetInt64(0),
                    Width: reader.GetInt32(5),
                    Height: reader.GetInt32(6),
                    ByteSize: reader.GetInt64(7),
                    Extension: reader.IsDBNull(8) ? null : reader.GetString(8)));
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
            string connStr = ResolveConnectionString(databasePath);
            await using var conn = new SqliteConnection(connStr);
            await conn.OpenAsync(ct).ConfigureAwait(false);

            const string checkTableSql = "SELECT COUNT(1) FROM sqlite_master WHERE type='table' AND name='DiscoveredAssets';";
            await using var checkCmd = new SqliteCommand(checkTableSql, conn);
            bool tableExists = Convert.ToInt32(await checkCmd.ExecuteScalarAsync(ct).ConfigureAwait(false), CultureInfo.InvariantCulture) > 0;
            if (!tableExists)
            {
                return Result.Ok<bool, IndexError>(false);
            }

            const string countSql = "SELECT COUNT(1) FROM DiscoveredAssets LIMIT 1;";
            await using var countCmd = new SqliteCommand(countSql, conn);
            int count = Convert.ToInt32(await countCmd.ExecuteScalarAsync(ct).ConfigureAwait(false), CultureInfo.InvariantCulture);
            return Result.Ok<bool, IndexError>(count > 0);
        }
        catch (Exception ex)
        {
            return Result.Fail<bool, IndexError>(new IndexError($"Failed to check asset index status: {ex.Message}"));
        }
    }

    public async Task<Result<int, IndexError>> ClearIndexAsync(
        string? databasePath = null,
        CancellationToken ct = default)
    {
        try
        {
            string connStr = ResolveConnectionString(databasePath);
            await using var conn = new SqliteConnection(connStr);
            await conn.OpenAsync(ct).ConfigureAwait(false);
            await InitializeSchemaAsync(conn, ct).ConfigureAwait(false);

            await using var cmd = new SqliteCommand("DELETE FROM DiscoveredAssets;", conn);
            int deleted = await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            return Result.Ok<int, IndexError>(deleted);
        }
        catch (Exception ex)
        {
            return Result.Fail<int, IndexError>(new IndexError($"Failed to clear asset index: {ex.Message}"));
        }
    }
}
