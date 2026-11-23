using System.Text.Json;
using Microsoft.Data.Sqlite;
using Candied.Core.Errors;
using Candied.Core.Models;
using Candied.Core.Ports;
using Candied.Decompiler.Internal;
using Candied.Primitives;

namespace Candied.Decompiler.Adapters;

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
            var cleanDir = sourceDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var dirName = Path.GetFileName(cleanDir);
            var parentDir = Directory.GetParent(cleanDir)?.FullName;
            var parentDirName = parentDir != null ? Path.GetFileName(parentDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) : null;

            if (string.Equals(dirName, "decompiled", StringComparison.OrdinalIgnoreCase))
            {
                var gameDir = Directory.GetParent(cleanDir)?.FullName ?? cleanDir;
                dbPath = Path.Combine(gameDir, "index.db");
            }
            else if (string.Equals(parentDirName, "decompiled", StringComparison.OrdinalIgnoreCase))
            {
                var gameDir = Directory.GetParent(parentDir!)?.FullName ?? parentDir!;
                dbPath = Path.Combine(gameDir, "index.db");
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

        var dir = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        return $"Data Source={dbPath}";
    }

    public async Task<Result<Unit, IndexError>> IndexSourceTreeAsync(
        string sourceDirectory,
        string? assemblyName = null,
        string? databasePath = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(sourceDirectory) || !Directory.Exists(sourceDirectory))
        {
            return Result.Fail<Unit, IndexError>(new IndexError($"Source directory not found: '{sourceDirectory}'"));
        }

        try
        {
            var connStr = ResolveConnectionString(databasePath, sourceDirectory);
            if (!string.IsNullOrWhiteSpace(databasePath))
            {
                _lastIndexedDatabasePath = databasePath;
            }
            else if (connStr.StartsWith("Data Source=", StringComparison.OrdinalIgnoreCase))
            {
                _lastIndexedDatabasePath = connStr["Data Source=".Length..];
            }

            await using var connection = new SqliteConnection(connStr);
            await connection.OpenAsync(ct);

            await InitializeSchemaAsync(connection, ct);

            var csFiles = Directory.GetFiles(sourceDirectory, "*.cs", SearchOption.AllDirectories);
            if (csFiles.Length == 0)
            {
                return Result.Ok<Unit, IndexError>(new Unit());
            }

            long latestWriteTimeTicks = 0;
            foreach (var file in csFiles)
            {
                var ticks = File.GetLastWriteTimeUtc(file).Ticks;
                if (ticks > latestWriteTimeTicks)
                {
                    latestWriteTimeTicks = ticks;
                }
            }

            // Determine target assembly name
            var targetAssemblyName = assemblyName;
            if (string.IsNullOrWhiteSpace(targetAssemblyName))
            {
                var csprojFiles = Directory.GetFiles(sourceDirectory, "*.csproj", SearchOption.AllDirectories);
                if (csprojFiles.Length > 0)
                {
                    targetAssemblyName = Path.GetFileNameWithoutExtension(csprojFiles[0]);
                }
                else
                {
                    var dirName = Path.GetFileName(sourceDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                    targetAssemblyName = string.Equals(dirName, "decompiled", StringComparison.OrdinalIgnoreCase)
                        ? "Assembly-CSharp"
                        : dirName;
                }
            }

            // Check if assembly is already indexed and up-to-date
            await using (var checkCmd = connection.CreateCommand())
            {
                checkCmd.CommandText = @"
                    SELECT source_file_count, latest_file_write_time_ticks
                    FROM indexed_assemblies
                    WHERE assembly_name = @assembly
                    LIMIT 1;";
                checkCmd.Parameters.AddWithValue("@assembly", targetAssemblyName);

                await using var reader = await checkCmd.ExecuteReaderAsync(ct);
                if (await reader.ReadAsync(ct))
                {
                    var recordedCount = reader.GetInt32(0);
                    var recordedTicks = reader.GetInt64(1);

                    if (recordedCount == csFiles.Length && recordedTicks == latestWriteTimeTicks)
                    {
                        await reader.CloseAsync();
                        await using var verifyCmd = connection.CreateCommand();
                        verifyCmd.CommandText = "SELECT 1 FROM symbols WHERE assembly_name = @assembly LIMIT 1;";
                        verifyCmd.Parameters.AddWithValue("@assembly", targetAssemblyName);
                        var hasSymbols = await verifyCmd.ExecuteScalarAsync(ct) != null;
                        if (hasSymbols)
                        {
                            return Result.Ok<Unit, IndexError>(new Unit());
                        }
                    }
                }
            }

            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(ct);

            // Clean previous symbols ONLY for this target assembly
            await using (var clearCmd = connection.CreateCommand())
            {
                clearCmd.Transaction = transaction;
                clearCmd.CommandText = "DELETE FROM symbols WHERE assembly_name = @assembly_name;";
                clearCmd.Parameters.AddWithValue("@assembly_name", targetAssemblyName);
                await clearCmd.ExecuteNonQueryAsync(ct);
            }

            var insertSymbolCmd = connection.CreateCommand();
            insertSymbolCmd.Transaction = transaction;
            insertSymbolCmd.CommandText = @"
                INSERT INTO symbols (full_name, kind, namespace, assembly_name, signature, relative_source_path)
                VALUES (@full_name, @kind, @namespace, @assembly_name, @signature, @relative_source_path);";

            var pFullName = insertSymbolCmd.Parameters.Add("@full_name", SqliteType.Text);
            var pKind = insertSymbolCmd.Parameters.Add("@kind", SqliteType.Integer);
            var pNamespace = insertSymbolCmd.Parameters.Add("@namespace", SqliteType.Text);
            var pAssembly = insertSymbolCmd.Parameters.Add("@assembly_name", SqliteType.Text);
            var pSignature = insertSymbolCmd.Parameters.Add("@signature", SqliteType.Text);
            var pRelativePath = insertSymbolCmd.Parameters.Add("@relative_source_path", SqliteType.Text);

            var insertDetailCmd = connection.CreateCommand();
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

            foreach (var file in csFiles)
            {
                var relativePath = Path.GetRelativePath(sourceDirectory, file);
                var code = await File.ReadAllTextAsync(file, ct);
                var (symbols, typeDetails) = RoslynSymbolExtractor.ExtractFromFile(code, relativePath, targetAssemblyName);

                foreach (var sym in symbols)
                {
                    pFullName.Value = sym.FullName;
                    pKind.Value = (int)sym.Kind;
                    pNamespace.Value = sym.Namespace;
                    pAssembly.Value = sym.AssemblyName;
                    pSignature.Value = sym.Signature;
                    pRelativePath.Value = sym.RelativeSourcePath;
                    await insertSymbolCmd.ExecuteNonQueryAsync(ct);
                }

                foreach (var detail in typeDetails)
                {
                    dFullName.Value = detail.FullName;
                    dNamespace.Value = detail.Namespace;
                    dBaseType.Value = (object?)detail.BaseType ?? DBNull.Value;
                    dInterfaces.Value = JsonSerializer.Serialize(detail.Interfaces);
                    dFields.Value = JsonSerializer.Serialize(detail.FieldSignatures);
                    dMethods.Value = JsonSerializer.Serialize(detail.MethodSignatures);
                    dRawSource.Value = detail.RawSourceCode;
                    await insertDetailCmd.ExecuteNonQueryAsync(ct);
                }
            }

            await using (var upsertCmd = connection.CreateCommand())
            {
                upsertCmd.Transaction = transaction;
                upsertCmd.CommandText = @"
                    INSERT INTO indexed_assemblies (assembly_name, source_directory, source_file_count, latest_file_write_time_ticks, indexed_at_utc)
                    VALUES (@assembly, @dir, @count, @ticks, @indexedAt)
                    ON CONFLICT(assembly_name) DO UPDATE SET
                        source_directory = excluded.source_directory,
                        source_file_count = excluded.source_file_count,
                        latest_file_write_time_ticks = excluded.latest_file_write_time_ticks,
                        indexed_at_utc = excluded.indexed_at_utc;";

                upsertCmd.Parameters.AddWithValue("@assembly", targetAssemblyName);
                upsertCmd.Parameters.AddWithValue("@dir", sourceDirectory);
                upsertCmd.Parameters.AddWithValue("@count", csFiles.Length);
                upsertCmd.Parameters.AddWithValue("@ticks", latestWriteTimeTicks);
                upsertCmd.Parameters.AddWithValue("@indexedAt", DateTime.UtcNow.ToString("O"));

                await upsertCmd.ExecuteNonQueryAsync(ct);
            }

            await transaction.CommitAsync(ct);
            return Result.Ok<Unit, IndexError>(new Unit());
        }
        catch (Exception ex)
        {
            return Result.Fail<Unit, IndexError>(new IndexError($"Failed to index source tree: {ex.Message}"));
        }
    }

    public async Task<Result<IReadOnlyList<SymbolSummary>, IndexError>> SearchSymbolsAsync(
        SymbolQuery query,
        string? databasePath = null,
        CancellationToken ct = default)
    {
        try
        {
            var connStr = ResolveConnectionString(databasePath);
            await using var connection = new SqliteConnection(connStr);
            await connection.OpenAsync(ct);
            await InitializeSchemaAsync(connection, ct);

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
                        queryConditions.Add("kind IN (0, 1, 2)"); // Class, Struct, Interface
                        break;
                    case SymbolKindFilter.MemberOnly:
                        queryConditions.Add("kind IN (3, 4, 5, 6)"); // Method, Field, Property, Event
                        break;
                }
            }

            cmd.CommandText = $@"
                SELECT full_name, kind, namespace, assembly_name, signature, relative_source_path
                FROM symbols
                WHERE {string.Join(" AND ", queryConditions)}
                ORDER BY length(full_name) ASC
                LIMIT @limit;";

            cmd.Parameters.AddWithValue("@limit", query.Limit);

            var results = new List<SymbolSummary>();
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                results.Add(new SymbolSummary(
                    FullName: reader.GetString(0),
                    Kind: (SymbolKind)reader.GetInt32(1),
                    Namespace: reader.GetString(2),
                    AssemblyName: reader.GetString(3),
                    Signature: reader.GetString(4),
                    RelativeSourcePath: reader.GetString(5)
                ));
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
            var connStr = ResolveConnectionString(databasePath);
            await using var connection = new SqliteConnection(connStr);
            await connection.OpenAsync(ct);
            await InitializeSchemaAsync(connection, ct);

            await using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                SELECT full_name, kind, namespace, assembly_name, signature, relative_source_path
                FROM symbols
                ORDER BY full_name ASC;";

            var results = new List<SymbolSummary>();
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                results.Add(new SymbolSummary(
                    FullName: reader.GetString(0),
                    Kind: (SymbolKind)reader.GetInt32(1),
                    Namespace: reader.GetString(2),
                    AssemblyName: reader.GetString(3),
                    Signature: reader.GetString(4),
                    RelativeSourcePath: reader.GetString(5)
                ));
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
            var connStr = ResolveConnectionString(databasePath);
            await using var connection = new SqliteConnection(connStr);
            await connection.OpenAsync(ct);
            await InitializeSchemaAsync(connection, ct);

            await using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                SELECT full_name, namespace, base_type, interfaces_json, fields_json, methods_json, raw_source_code
                FROM type_details
                WHERE full_name = @full_name
                LIMIT 1;";
            cmd.Parameters.AddWithValue("@full_name", fullTypeName);

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
            {
                return Result.Fail<TypeDefinitionDetail, IndexError>(new IndexError($"Type not found: '{fullTypeName}'"));
            }

            var fullName = reader.GetString(0);
            var ns = reader.GetString(1);
            var baseType = reader.IsDBNull(2) ? null : reader.GetString(2);
            var interfacesJson = reader.GetString(3);
            var fieldsJson = reader.GetString(4);
            var methodsJson = reader.GetString(5);
            var rawSource = reader.GetString(6);

            var interfaces = JsonSerializer.Deserialize<List<string>>(interfacesJson) ?? [];
            var fields = JsonSerializer.Deserialize<List<string>>(fieldsJson) ?? [];
            var methods = JsonSerializer.Deserialize<List<string>>(methodsJson) ?? [];

            await reader.CloseAsync();

            string? assemblyName = null;
            string? relativeSourcePath = null;
            await using (var symCmd = connection.CreateCommand())
            {
                symCmd.CommandText = @"
                    SELECT assembly_name, relative_source_path
                    FROM symbols
                    WHERE full_name = @full_name
                    LIMIT 1;";
                symCmd.Parameters.AddWithValue("@full_name", fullName);
                await using var symReader = await symCmd.ExecuteReaderAsync(ct);
                if (await symReader.ReadAsync(ct))
                {
                    assemblyName = symReader.GetString(0);
                    relativeSourcePath = symReader.GetString(1);
                }
            }

            var detail = new TypeDefinitionDetail(
                FullName: fullName,
                Namespace: ns,
                BaseType: baseType,
                Interfaces: interfaces,
                FieldSignatures: fields,
                MethodSignatures: methods,
                RawSourceCode: rawSource,
                AssemblyName: assemblyName,
                RelativeSourcePath: relativeSourcePath
            );

            return Result.Ok<TypeDefinitionDetail, IndexError>(detail);
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
            var connStr = ResolveConnectionString(databasePath);
            await using var connection = new SqliteConnection(connStr);
            await connection.OpenAsync(ct);
            await InitializeSchemaAsync(connection, ct);
            await EnsureSourceFtsPopulatedAsync(connection, ct);

            var ftsQuery = BuildSafeFtsQuery(query.SearchTerm);
            var matches = new List<CodeSearchMatch>();

            await using var cmd = connection.CreateCommand();
            var useFts = !string.IsNullOrEmpty(ftsQuery);

            if (useFts)
            {
                var conditions = new List<string> { "type_source_fts MATCH @ftsQuery" };
                cmd.Parameters.AddWithValue("@ftsQuery", ftsQuery);

                // Always verify exact substring match so punctuation (e.g. "LookAt(" or "a.b") is honored precisely
                conditions.Add("instr(lower(td.raw_source_code), lower(@rawTerm)) > 0");
                cmd.Parameters.AddWithValue("@rawTerm", query.SearchTerm);

                if (!string.IsNullOrWhiteSpace(query.NamespacePrefix))
                {
                    conditions.Add("td.namespace LIKE @ns");
                    cmd.Parameters.AddWithValue("@ns", $"{query.NamespacePrefix}%");
                }

                cmd.CommandText = $@"
                    SELECT td.full_name, td.namespace, td.raw_source_code,
                           (SELECT s.assembly_name FROM symbols s WHERE s.full_name = td.full_name LIMIT 1),
                           (SELECT s.relative_source_path FROM symbols s WHERE s.full_name = td.full_name LIMIT 1)
                    FROM type_source_fts
                    INNER JOIN type_details td ON td.rowid = type_source_fts.rowid
                    WHERE {string.Join(" AND ", conditions)}
                    ORDER BY length(td.full_name) ASC
                    LIMIT @limit;";
            }
            else
            {
                var conditions = new List<string> { "instr(lower(td.raw_source_code), lower(@rawTerm)) > 0" };
                cmd.Parameters.AddWithValue("@rawTerm", query.SearchTerm);

                if (!string.IsNullOrWhiteSpace(query.NamespacePrefix))
                {
                    conditions.Add("td.namespace LIKE @ns");
                    cmd.Parameters.AddWithValue("@ns", $"{query.NamespacePrefix}%");
                }

                cmd.CommandText = $@"
                    SELECT td.full_name, td.namespace, td.raw_source_code,
                           (SELECT s.assembly_name FROM symbols s WHERE s.full_name = td.full_name LIMIT 1),
                           (SELECT s.relative_source_path FROM symbols s WHERE s.full_name = td.full_name LIMIT 1)
                    FROM type_details td
                    WHERE {string.Join(" AND ", conditions)}
                    ORDER BY length(td.full_name) ASC
                    LIMIT @limit;";
            }

            cmd.Parameters.AddWithValue("@limit", Math.Clamp(query.Limit <= 0 ? 25 : query.Limit, 1, 100));

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var fullName = reader.GetString(0);
                var ns = reader.GetString(1);
                var rawSource = reader.GetString(2);
                var asmName = reader.IsDBNull(3) ? null : reader.GetString(3);
                var relPath = reader.IsDBNull(4) ? null : reader.GetString(4);

                var (lineNumber, snippet) = ExtractFirstMatchSnippet(rawSource, query.SearchTerm);
                matches.Add(new CodeSearchMatch(
                    FullName: fullName,
                    Namespace: ns,
                    AssemblyName: asmName,
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

    private static string BuildSafeFtsQuery(string searchTerm)
    {
        // Extract alphanumeric/underscore identifier tokens for fast FTS5 candidate narrowing
        var tokens = new List<string>();
        var current = new System.Text.StringBuilder();
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

        return string.Join(" AND ", tokens.Select(t => $"\"{t.Replace("\"", "\"\"")}\""));
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
                    var trimmed = lines[j].Trim();
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
        await using var reader = await checkCmd.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
        {
            long detailsCount = reader.GetInt64(0);
            long ftsIndexedCount = reader.GetInt64(1);
            await reader.CloseAsync();

            if (detailsCount > 0 && ftsIndexedCount != detailsCount)
            {
                await using var rebuildCmd = connection.CreateCommand();
                rebuildCmd.CommandText = "INSERT INTO type_source_fts(type_source_fts) VALUES('rebuild');";
                await rebuildCmd.ExecuteNonQueryAsync(ct);
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
                assembly_name TEXT NOT NULL,
                signature TEXT NOT NULL,
                relative_source_path TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS idx_symbols_full_name ON symbols(full_name);
            CREATE INDEX IF NOT EXISTS idx_symbols_namespace ON symbols(namespace);
            CREATE INDEX IF NOT EXISTS idx_symbols_kind ON symbols(kind);
            CREATE INDEX IF NOT EXISTS idx_symbols_assembly ON symbols(assembly_name);

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

            CREATE TABLE IF NOT EXISTS indexed_assemblies (
                assembly_name TEXT PRIMARY KEY,
                source_directory TEXT NOT NULL,
                source_file_count INTEGER NOT NULL,
                latest_file_write_time_ticks INTEGER NOT NULL,
                indexed_at_utc TEXT NOT NULL
            );";

        await cmd.ExecuteNonQueryAsync(ct);
    }
}
