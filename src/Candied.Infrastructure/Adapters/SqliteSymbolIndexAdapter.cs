using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Candied.Core.Errors;
using Candied.Core.Models;
using Candied.Core.Ports;
using Candied.Infrastructure.Internal;
using Candied.Primitives;

namespace Candied.Infrastructure.Adapters;

internal sealed class SqliteSymbolIndexAdapter : ISymbolIndex
{
    private readonly string? _overrideConnectionString;
    private string? _lastIndexedDatabasePath;

    public SqliteSymbolIndexAdapter(string? connectionString = null)
    {
        _overrideConnectionString = connectionString;
    }

    private string ResolveConnectionString(string? databasePath = null, string? sourceDirectory = null)
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
        else if (!string.IsNullOrEmpty(sourceDirectory))
        {
            string cleanDir = sourceDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string dirName = Path.GetFileName(cleanDir);
            string? parentDir = Directory.GetParent(cleanDir)?.FullName;
            string? parentDirName = parentDir != null
                ? Path.GetFileName(parentDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                : null;

            if (string.Equals(dirName, "extracted", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(dirName, "decompiled", StringComparison.OrdinalIgnoreCase))
            {
                string targetDir = Directory.GetParent(cleanDir)?.FullName ?? cleanDir;
                dbPath = Path.Combine(targetDir, "index.db");
            }
            else if (string.Equals(parentDirName, "extracted", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(parentDirName, "decompiled", StringComparison.OrdinalIgnoreCase))
            {
                string targetDir = Directory.GetParent(parentDir!)?.FullName ?? parentDir!;
                dbPath = Path.Combine(targetDir, "index.db");
            }
            else
            {
                dbPath = Path.Combine(cleanDir, "index.db");
            }
        }
        else if (!string.IsNullOrEmpty(_lastIndexedDatabasePath))
        {
            dbPath = _lastIndexedDatabasePath;
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

    public async Task<Result<Unit, IndexError>> IndexCSharpSourceTreeAsync(
        string sourceDirectory,
        string? moduleName = null,
        string? databasePath = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(sourceDirectory) || !Directory.Exists(sourceDirectory))
        {
            return Result.Fail<Unit, IndexError>(new IndexError($"Source directory not found: '{sourceDirectory}'"));
        }

        try
        {
            string connStr = ResolveConnectionString(databasePath, sourceDirectory);
            RecordLastDatabasePath(databasePath, connStr);

            await using var connection = new SqliteConnection(connStr);
            await connection.OpenAsync(ct).ConfigureAwait(false);
            await InitializeSchemaAsync(connection, ct).ConfigureAwait(false);

            var csFiles = Directory.GetFiles(sourceDirectory, "*.cs", SearchOption.AllDirectories);
            if (csFiles.Length == 0)
            {
                return Result.Ok<Unit, IndexError>(new Unit());
            }

            long latestWriteTimeTicks = 0;
            foreach (var file in csFiles)
            {
                long ticks = File.GetLastWriteTimeUtc(file).Ticks;
                if (ticks > latestWriteTimeTicks)
                {
                    latestWriteTimeTicks = ticks;
                }
            }

            string effectiveModule = ResolveModuleName(sourceDirectory, moduleName);

            await using (var checkCmd = connection.CreateCommand())
            {
                checkCmd.CommandText = @"
                    SELECT source_file_count, latest_file_write_time_ticks
                    FROM indexed_modules
                    WHERE module_name = @module
                    LIMIT 1;";
                checkCmd.Parameters.AddWithValue("@module", effectiveModule);

                await using var reader = await checkCmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
                if (await reader.ReadAsync(ct).ConfigureAwait(false))
                {
                    int recordedCount = reader.GetInt32(0);
                    long recordedTicks = reader.GetInt64(1);

                    if (recordedCount == csFiles.Length && recordedTicks == latestWriteTimeTicks)
                    {
                        await reader.CloseAsync().ConfigureAwait(false);
                        await using var verifyCmd = connection.CreateCommand();
                        verifyCmd.CommandText = "SELECT 1 FROM symbols WHERE module_name = @module LIMIT 1;";
                        verifyCmd.Parameters.AddWithValue("@module", effectiveModule);
                        bool hasSymbols = await verifyCmd.ExecuteScalarAsync(ct).ConfigureAwait(false) != null;
                        if (hasSymbols)
                        {
                            return Result.Ok<Unit, IndexError>(new Unit());
                        }
                    }
                }
            }

            var allSymbols = new List<SymbolSummary>();
            var allDetails = new List<TypeDefinitionDetail>();

            foreach (var file in csFiles)
            {
                string relativePath = Path.GetRelativePath(sourceDirectory, file);
                string code = await File.ReadAllTextAsync(file, ct).ConfigureAwait(false);
                var (symbols, typeDetails) = RoslynSymbolExtractor.ExtractFromFile(code, relativePath, effectiveModule);
                allSymbols.AddRange(symbols);
                allDetails.AddRange(typeDetails);
            }

            await WriteModuleSymbolsToDbAsync(
                connection,
                effectiveModule,
                sourceDirectory,
                csFiles.Length,
                latestWriteTimeTicks,
                allSymbols,
                allDetails,
                ct).ConfigureAwait(false);

            return Result.Ok<Unit, IndexError>(new Unit());
        }
        catch (Exception ex)
        {
            return Result.Fail<Unit, IndexError>(new IndexError($"Failed to index C# source tree: {ex.Message}"));
        }
    }

    public async Task<Result<Unit, IndexError>> IndexExtractedSymbolsAsync(
        string moduleName,
        IReadOnlyList<SymbolSummary> symbols,
        IReadOnlyList<TypeDefinitionDetail> typeDetails,
        string? databasePath = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(moduleName))
        {
            return Result.Fail<Unit, IndexError>(new IndexError("Module name cannot be empty."));
        }

        try
        {
            string connStr = ResolveConnectionString(databasePath);
            RecordLastDatabasePath(databasePath, connStr);

            await using var connection = new SqliteConnection(connStr);
            await connection.OpenAsync(ct).ConfigureAwait(false);
            await InitializeSchemaAsync(connection, ct).ConfigureAwait(false);

            await WriteModuleSymbolsToDbAsync(
                connection,
                moduleName.Trim(),
                string.Empty,
                typeDetails.Count,
                DateTime.UtcNow.Ticks,
                symbols,
                typeDetails,
                ct).ConfigureAwait(false);

            return Result.Ok<Unit, IndexError>(new Unit());
        }
        catch (Exception ex)
        {
            return Result.Fail<Unit, IndexError>(new IndexError($"Failed to index extracted symbols: {ex.Message}"));
        }
    }

    public async Task<Result<IReadOnlyList<SymbolSummary>, IndexError>> SearchSymbolsAsync(
        SymbolQuery query,
        string? databasePath = null,
        CancellationToken ct = default)
    {
        try
        {
            string connStr = ResolveConnectionString(databasePath);
            await using var connection = new SqliteConnection(connStr);
            await connection.OpenAsync(ct).ConfigureAwait(false);
            await InitializeSchemaAsync(connection, ct).ConfigureAwait(false);

            await using var cmd = connection.CreateCommand();
            var queryConditions = new List<string>
            {
                "(full_name LIKE @term OR signature LIKE @term)"
            };
            cmd.Parameters.AddWithValue("@term", $"%{query.SearchTerm}%");

            if (!string.IsNullOrEmpty(query.NamespacePrefix))
            {
                queryConditions.Add("namespace LIKE @ns");
                cmd.Parameters.AddWithValue("@ns", $"{query.NamespacePrefix}%");
            }

            if (!string.IsNullOrEmpty(query.ModuleFilter))
            {
                queryConditions.Add("LOWER(module_name) = LOWER(@modFilter)");
                cmd.Parameters.AddWithValue("@modFilter", query.ModuleFilter.Trim());
            }

            if (query.EnumOnly)
            {
                queryConditions.Add("kind = 0 AND signature LIKE '%enum %'");
            }
            else if (query.ExactKind.HasValue)
            {
                queryConditions.Add("kind = @exactKind");
                cmd.Parameters.AddWithValue("@exactKind", (int)query.ExactKind.Value);
            }
            else
            {
                switch (query.Filter)
                {
                    case SymbolKindFilter.TypeOnly:
                        queryConditions.Add("kind IN (0, 1, 2)");
                        break;
                    case SymbolKindFilter.MemberOnly:
                        queryConditions.Add("kind IN (3, 4, 5, 6)");
                        break;
                }
            }

            cmd.CommandText = $@"
                SELECT full_name, kind, namespace, module_name, signature, relative_source_path
                FROM symbols
                WHERE {string.Join(" AND ", queryConditions)}
                ORDER BY length(full_name) ASC
                LIMIT @limit;";

            cmd.Parameters.AddWithValue("@limit", query.Limit <= 0 ? 25 : query.Limit);

            var results = new List<SymbolSummary>();
            await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                results.Add(new SymbolSummary(
                    FullName: reader.GetString(0),
                    Kind: (SymbolKind)reader.GetInt32(1),
                    Namespace: reader.GetString(2),
                    ModuleName: reader.GetString(3),
                    Signature: reader.GetString(4),
                    RelativeSourcePath: reader.GetString(5)));
            }

            return Result.Ok<IReadOnlyList<SymbolSummary>, IndexError>(results);
        }
        catch (Exception ex)
        {
            return Result.Fail<IReadOnlyList<SymbolSummary>, IndexError>(new IndexError($"Symbol search failed: {ex.Message}"));
        }
    }

    public async Task<Result<IReadOnlyList<SymbolSummary>, IndexError>> GetAllSymbolsAsync(
        string? databasePath = null,
        CancellationToken ct = default)
    {
        try
        {
            string connStr = ResolveConnectionString(databasePath);
            await using var connection = new SqliteConnection(connStr);
            await connection.OpenAsync(ct).ConfigureAwait(false);
            await InitializeSchemaAsync(connection, ct).ConfigureAwait(false);

            await using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                SELECT full_name, kind, namespace, module_name, signature, relative_source_path
                FROM symbols
                ORDER BY full_name ASC;";

            var results = new List<SymbolSummary>();
            await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                results.Add(new SymbolSummary(
                    FullName: reader.GetString(0),
                    Kind: (SymbolKind)reader.GetInt32(1),
                    Namespace: reader.GetString(2),
                    ModuleName: reader.GetString(3),
                    Signature: reader.GetString(4),
                    RelativeSourcePath: reader.GetString(5)));
            }

            return Result.Ok<IReadOnlyList<SymbolSummary>, IndexError>(results);
        }
        catch (Exception ex)
        {
            return Result.Fail<IReadOnlyList<SymbolSummary>, IndexError>(new IndexError($"Failed to retrieve all symbols: {ex.Message}"));
        }
    }

    public async Task<Result<TypeDefinitionDetail, IndexError>> GetTypeDetailAsync(
        string fullTypeName,
        string? databasePath = null,
        CancellationToken ct = default)
    {
        try
        {
            string connStr = ResolveConnectionString(databasePath);
            await using var connection = new SqliteConnection(connStr);
            await connection.OpenAsync(ct).ConfigureAwait(false);
            await InitializeSchemaAsync(connection, ct).ConfigureAwait(false);

            await using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                SELECT full_name, namespace, base_type, interfaces_json, fields_json, methods_json, raw_source_code
                FROM type_details
                WHERE full_name = @full_name OR full_name LIKE @generic_prefix
                ORDER BY CASE WHEN full_name = @full_name THEN 0 ELSE 1 END, length(full_name) ASC
                LIMIT 1;";
            cmd.Parameters.AddWithValue("@full_name", fullTypeName);
            cmd.Parameters.AddWithValue("@generic_prefix", fullTypeName + "<%");

            await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (!await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                return Result.Fail<TypeDefinitionDetail, IndexError>(new IndexError($"Type not found: '{fullTypeName}'"));
            }

            string fullName = reader.GetString(0);
            string ns = reader.GetString(1);
            string? baseType = reader.IsDBNull(2) ? null : reader.GetString(2);
            string interfacesJson = reader.GetString(3);
            string fieldsJson = reader.GetString(4);
            string methodsJson = reader.GetString(5);
            string rawSource = reader.GetString(6);

            var interfaces = JsonSerializer.Deserialize<List<string>>(interfacesJson) ?? [];
            var fields = JsonSerializer.Deserialize<List<string>>(fieldsJson) ?? [];
            var methods = JsonSerializer.Deserialize<List<string>>(methodsJson) ?? [];

            await reader.CloseAsync().ConfigureAwait(false);

            string? moduleName = null;
            string? relativeSourcePath = null;
            await using (var symCmd = connection.CreateCommand())
            {
                symCmd.CommandText = @"
                    SELECT module_name, relative_source_path
                    FROM symbols
                    WHERE full_name = @full_name
                    LIMIT 1;";
                symCmd.Parameters.AddWithValue("@full_name", fullName);
                await using var symReader = await symCmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
                if (await symReader.ReadAsync(ct).ConfigureAwait(false))
                {
                    moduleName = symReader.GetString(0);
                    relativeSourcePath = symReader.GetString(1);
                }
            }

            return Result.Ok<TypeDefinitionDetail, IndexError>(new TypeDefinitionDetail(
                FullName: fullName,
                Namespace: ns,
                BaseType: baseType,
                Interfaces: interfaces,
                FieldSignatures: fields,
                MethodSignatures: methods,
                RawSourceCode: rawSource,
                ModuleName: moduleName,
                RelativeSourcePath: relativeSourcePath));
        }
        catch (Exception ex)
        {
            return Result.Fail<TypeDefinitionDetail, IndexError>(new IndexError($"Failed to get type detail: {ex.Message}"));
        }
    }

    public async Task<Result<IReadOnlyList<CodeSearchMatch>, IndexError>> SearchCodeAsync(
        CodeSearchQuery query,
        string? databasePath = null,
        CancellationToken ct = default)
    {
        try
        {
            string connStr = ResolveConnectionString(databasePath);
            await using var connection = new SqliteConnection(connStr);
            await connection.OpenAsync(ct).ConfigureAwait(false);
            await InitializeSchemaAsync(connection, ct).ConfigureAwait(false);
            await EnsureSourceFtsPopulatedAsync(connection, ct).ConfigureAwait(false);

            string ftsQuery = BuildSafeFtsQuery(query.SearchTerm);
            var matches = new List<CodeSearchMatch>();

            await using var cmd = connection.CreateCommand();
            bool useFts = !string.IsNullOrEmpty(ftsQuery);

            var conditions = new List<string>();
            if (useFts)
            {
                conditions.Add("type_source_fts MATCH @ftsQuery");
                cmd.Parameters.AddWithValue("@ftsQuery", ftsQuery);
            }

            conditions.Add("instr(lower(td.raw_source_code), lower(@rawTerm)) > 0");
            cmd.Parameters.AddWithValue("@rawTerm", query.SearchTerm);

            if (!string.IsNullOrWhiteSpace(query.NamespacePrefix))
            {
                conditions.Add("td.namespace LIKE @ns");
                cmd.Parameters.AddWithValue("@ns", $"{query.NamespacePrefix}%");
            }

            if (!string.IsNullOrWhiteSpace(query.ModuleFilter))
            {
                conditions.Add("EXISTS (SELECT 1 FROM symbols ms WHERE ms.full_name = td.full_name AND LOWER(ms.module_name) = LOWER(@modFilter))");
                cmd.Parameters.AddWithValue("@modFilter", query.ModuleFilter.Trim());
            }

            if (useFts)
            {
                cmd.CommandText = $@"
                    SELECT td.full_name, td.namespace, td.raw_source_code,
                           (SELECT s.module_name FROM symbols s WHERE s.full_name = td.full_name LIMIT 1),
                           (SELECT s.relative_source_path FROM symbols s WHERE s.full_name = td.full_name LIMIT 1)
                    FROM type_source_fts
                    INNER JOIN type_details td ON td.rowid = type_source_fts.rowid
                    WHERE {string.Join(" AND ", conditions)}
                    ORDER BY length(td.full_name) ASC
                    LIMIT @limit;";
            }
            else
            {
                cmd.CommandText = $@"
                    SELECT td.full_name, td.namespace, td.raw_source_code,
                           (SELECT s.module_name FROM symbols s WHERE s.full_name = td.full_name LIMIT 1),
                           (SELECT s.relative_source_path FROM symbols s WHERE s.full_name = td.full_name LIMIT 1)
                    FROM type_details td
                    WHERE {string.Join(" AND ", conditions)}
                    ORDER BY length(td.full_name) ASC
                    LIMIT @limit;";
            }

            cmd.Parameters.AddWithValue("@limit", Math.Clamp(query.Limit <= 0 ? 25 : query.Limit, 1, 100));

            await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                string fullName = reader.GetString(0);
                string ns = reader.GetString(1);
                string rawSource = reader.GetString(2);
                string? modName = reader.IsDBNull(3) ? null : reader.GetString(3);
                string? relPath = reader.IsDBNull(4) ? null : reader.GetString(4);

                var (lineNumber, snippet) = ExtractFirstMatchSnippet(rawSource, query.SearchTerm);
                matches.Add(new CodeSearchMatch(
                    FullName: fullName,
                    Namespace: ns,
                    ModuleName: modName,
                    RelativeSourcePath: relPath,
                    LineNumber: lineNumber,
                    Snippet: snippet));
            }

            return Result.Ok<IReadOnlyList<CodeSearchMatch>, IndexError>(matches);
        }
        catch (Exception ex)
        {
            return Result.Fail<IReadOnlyList<CodeSearchMatch>, IndexError>(new IndexError($"Code search failed: {ex.Message}"));
        }
    }

    private void RecordLastDatabasePath(string? databasePath, string connStr)
    {
        if (!string.IsNullOrWhiteSpace(databasePath))
        {
            _lastIndexedDatabasePath = databasePath;
        }
        else if (connStr.StartsWith("Data Source=", StringComparison.OrdinalIgnoreCase))
        {
            _lastIndexedDatabasePath = connStr["Data Source=".Length..];
        }
    }

    private static string ResolveModuleName(string sourceDirectory, string? explicitModuleName)
    {
        if (!string.IsNullOrWhiteSpace(explicitModuleName))
        {
            return explicitModuleName.Trim();
        }

        var csprojFiles = Directory.GetFiles(sourceDirectory, "*.csproj", SearchOption.AllDirectories);
        if (csprojFiles.Length > 0)
        {
            return Path.GetFileNameWithoutExtension(csprojFiles[0]);
        }

        string dirName = Path.GetFileName(sourceDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return string.IsNullOrWhiteSpace(dirName) ? "PrimaryModule" : dirName;
    }

    private static async Task WriteModuleSymbolsToDbAsync(
        SqliteConnection connection,
        string moduleName,
        string sourceDirectory,
        int fileCount,
        long latestTicks,
        IReadOnlyList<SymbolSummary> symbols,
        IReadOnlyList<TypeDefinitionDetail> typeDetails,
        CancellationToken ct)
    {
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(ct).ConfigureAwait(false);

        await using (var clearCmd = connection.CreateCommand())
        {
            clearCmd.Transaction = transaction;
            clearCmd.CommandText = "DELETE FROM symbols WHERE module_name = @module_name;";
            clearCmd.Parameters.AddWithValue("@module_name", moduleName);
            await clearCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await using var insertSymbolCmd = connection.CreateCommand();
        insertSymbolCmd.Transaction = transaction;
        insertSymbolCmd.CommandText = @"
            INSERT INTO symbols (full_name, kind, namespace, module_name, signature, relative_source_path)
            VALUES (@full_name, @kind, @namespace, @module_name, @signature, @relative_source_path);";

        var pFullName = insertSymbolCmd.Parameters.Add("@full_name", SqliteType.Text);
        var pKind = insertSymbolCmd.Parameters.Add("@kind", SqliteType.Integer);
        var pNamespace = insertSymbolCmd.Parameters.Add("@namespace", SqliteType.Text);
        var pModule = insertSymbolCmd.Parameters.Add("@module_name", SqliteType.Text);
        var pSignature = insertSymbolCmd.Parameters.Add("@signature", SqliteType.Text);
        var pRelativePath = insertSymbolCmd.Parameters.Add("@relative_source_path", SqliteType.Text);

        foreach (var sym in symbols)
        {
            pFullName.Value = sym.FullName;
            pKind.Value = (int)sym.Kind;
            pNamespace.Value = sym.Namespace;
            pModule.Value = sym.ModuleName;
            pSignature.Value = sym.Signature;
            pRelativePath.Value = sym.RelativeSourcePath;
            await insertSymbolCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await using var insertDetailCmd = connection.CreateCommand();
        insertDetailCmd.Transaction = transaction;
        insertDetailCmd.CommandText = @"
            INSERT OR REPLACE INTO type_details (full_name, namespace, base_type, interfaces_json, fields_json, methods_json, raw_source_code)
            VALUES (@full_name, @namespace, @base_type, @interfaces_json, @fields_json, @methods_json, @raw_source_code);";

        var dFullName = insertDetailCmd.Parameters.Add("@full_name", SqliteType.Text);
        var dNamespace = insertDetailCmd.Parameters.Add("@namespace", SqliteType.Text);
        var dBaseType = insertDetailCmd.Parameters.Add("@base_type", SqliteType.Text);
        var dInterfaces = insertDetailCmd.Parameters.Add("@interfaces_json", SqliteType.Text);
        var dFields = insertDetailCmd.Parameters.Add("@fields_json", SqliteType.Text);
        var dMethods = insertDetailCmd.Parameters.Add("@methods_json", SqliteType.Text);
        var dRawSource = insertDetailCmd.Parameters.Add("@raw_source_code", SqliteType.Text);

        foreach (var detail in typeDetails)
        {
            dFullName.Value = detail.FullName;
            dNamespace.Value = detail.Namespace;
            dBaseType.Value = (object?)detail.BaseType ?? DBNull.Value;
            dInterfaces.Value = JsonSerializer.Serialize(detail.Interfaces);
            dFields.Value = JsonSerializer.Serialize(detail.FieldSignatures);
            dMethods.Value = JsonSerializer.Serialize(detail.MethodSignatures);
            dRawSource.Value = detail.RawSourceCode;
            await insertDetailCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await using (var upsertCmd = connection.CreateCommand())
        {
            upsertCmd.Transaction = transaction;
            upsertCmd.CommandText = @"
                INSERT INTO indexed_modules (module_name, source_directory, source_file_count, latest_file_write_time_ticks, indexed_at_utc)
                VALUES (@module, @dir, @count, @ticks, @indexedAt)
                ON CONFLICT(module_name) DO UPDATE SET
                    source_directory = excluded.source_directory,
                    source_file_count = excluded.source_file_count,
                    latest_file_write_time_ticks = excluded.latest_file_write_time_ticks,
                    indexed_at_utc = excluded.indexed_at_utc;";

            upsertCmd.Parameters.AddWithValue("@module", moduleName);
            upsertCmd.Parameters.AddWithValue("@dir", sourceDirectory);
            upsertCmd.Parameters.AddWithValue("@count", fileCount);
            upsertCmd.Parameters.AddWithValue("@ticks", latestTicks);
            upsertCmd.Parameters.AddWithValue("@indexedAt", DateTime.UtcNow.ToString("O"));

            await upsertCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await transaction.CommitAsync(ct).ConfigureAwait(false);
    }

    private static string BuildSafeFtsQuery(string searchTerm)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
        foreach (var ch in searchTerm)
        {
            if (char.IsLetterOrDigit(ch) || ch == '_')
            {
                current.Append(ch);
            }
            else if (current.Length > 0)
            {
                tokens.Add(current.ToString());
                current.Clear();
            }
        }

        if (current.Length > 0)
        {
            tokens.Add(current.ToString());
        }

        if (tokens.Count == 0)
        {
            return string.Empty;
        }

        return string.Join(" AND ", tokens.Select(t => $"\"{t.Replace("\"", "\"\"", StringComparison.Ordinal)}\""));
    }

    private static (int LineNumber, string Snippet) ExtractFirstMatchSnippet(string rawSource, string term)
    {
        var lines = rawSource.Split(["\r\n", "\r", "\n"], StringSplitOptions.None);
        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].Contains(term, StringComparison.OrdinalIgnoreCase))
            {
                int start = Math.Max(0, i - 1);
                int end = Math.Min(lines.Length - 1, i + 1);
                var snippetLines = new List<string>(end - start + 1);
                for (int j = start; j <= end; j++)
                {
                    string trimmed = lines[j].Trim();
                    if (trimmed.Length > 180)
                    {
                        trimmed = trimmed[..180] + "...";
                    }

                    snippetLines.Add($"L{j + 1}: {trimmed}");
                }

                return (i + 1, string.Join(" | ", snippetLines));
            }
        }

        return (1, string.Empty);
    }

    private static async Task EnsureSourceFtsPopulatedAsync(SqliteConnection connection, CancellationToken ct)
    {
        await using var checkCmd = connection.CreateCommand();
        checkCmd.CommandText = @"
            SELECT (SELECT COUNT(1) FROM type_details),
                   (SELECT COUNT(1) FROM type_source_fts_docsize);";
        await using var reader = await checkCmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            long detailsCount = reader.GetInt64(0);
            long ftsIndexedCount = reader.GetInt64(1);
            await reader.CloseAsync().ConfigureAwait(false);

            if (detailsCount > 0 && ftsIndexedCount != detailsCount)
            {
                await using var rebuildCmd = connection.CreateCommand();
                rebuildCmd.CommandText = "INSERT INTO type_source_fts(type_source_fts) VALUES('rebuild');";
                await rebuildCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }
        }
    }

    private static async Task InitializeSchemaAsync(SqliteConnection connection, CancellationToken ct)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS symbols (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                full_name TEXT NOT NULL,
                kind INTEGER NOT NULL,
                namespace TEXT NOT NULL,
                module_name TEXT NOT NULL,
                signature TEXT NOT NULL,
                relative_source_path TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS idx_symbols_full_name ON symbols(full_name);
            CREATE INDEX IF NOT EXISTS idx_symbols_namespace ON symbols(namespace);
            CREATE INDEX IF NOT EXISTS idx_symbols_kind ON symbols(kind);
            CREATE INDEX IF NOT EXISTS idx_symbols_module ON symbols(module_name);

            CREATE TABLE IF NOT EXISTS type_details (
                full_name TEXT PRIMARY KEY,
                namespace TEXT NOT NULL,
                base_type TEXT,
                interfaces_json TEXT NOT NULL,
                fields_json TEXT NOT NULL,
                methods_json TEXT NOT NULL,
                raw_source_code TEXT NOT NULL
            );

            CREATE VIRTUAL TABLE IF NOT EXISTS type_source_fts USING fts5(
                full_name UNINDEXED,
                raw_source_code,
                content='type_details',
                content_rowid='rowid'
            );

            CREATE TRIGGER IF NOT EXISTS trg_type_details_ai AFTER INSERT ON type_details BEGIN
                INSERT INTO type_source_fts(rowid, full_name, raw_source_code)
                VALUES (new.rowid, new.full_name, new.raw_source_code);
            END;

            CREATE TRIGGER IF NOT EXISTS trg_type_details_ad AFTER DELETE ON type_details BEGIN
                INSERT INTO type_source_fts(type_source_fts, rowid, full_name, raw_source_code)
                VALUES('delete', old.rowid, old.full_name, old.raw_source_code);
            END;

            CREATE TRIGGER IF NOT EXISTS trg_type_details_au AFTER UPDATE ON type_details BEGIN
                INSERT INTO type_source_fts(type_source_fts, rowid, full_name, raw_source_code)
                VALUES('delete', old.rowid, old.full_name, old.raw_source_code);
                INSERT INTO type_source_fts(rowid, full_name, raw_source_code)
                VALUES (new.rowid, new.full_name, new.raw_source_code);
            END;

            CREATE TABLE IF NOT EXISTS indexed_modules (
                module_name TEXT PRIMARY KEY,
                source_directory TEXT NOT NULL,
                source_file_count INTEGER NOT NULL,
                latest_file_write_time_ticks INTEGER NOT NULL,
                indexed_at_utc TEXT NOT NULL
            );";

        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }
}
