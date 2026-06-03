using System.Text.RegularExpressions;
using ClosedXML.Excel;
using Microsoft.Data.Sqlite;

namespace AutoToolCatalog.Services.TaeguTec;

/// <summary>
/// Fast TaeguTec catalog-number lookup. Seeds a SQLite table from the master Excel once (re-seeds when the
/// file changes) and keeps an in-memory normalized dictionary for O(1) description → catalog-no lookups.
/// </summary>
public sealed partial class TaeguTecCatalogStore : ITaeguTecCatalogStore
{
    private readonly string _excelPath;
    private readonly string _connectionString;
    private readonly Dictionary<string, string> _map = new(StringComparer.Ordinal);

    public TaeguTecCatalogStore(string excelPath, string dbPath)
    {
        _excelPath = excelPath;
        _connectionString = new SqliteConnectionStringBuilder { DataSource = dbPath }.ConnectionString;
    }

    public int Count => _map.Count;

    public void Initialize()
    {
        EnsureSchema();

        var signature = ComputeExcelSignature();
        if (signature != null && signature == GetMeta("taegutec_catalog_signature") && GetRowCount() > 0)
        {
            LoadIntoMemory();
            return;
        }

        var rows = ParseExcel();
        if (rows.Count > 0)
        {
            Replace(rows);
            SetMeta("taegutec_catalog_signature", signature ?? string.Empty);
        }

        LoadIntoMemory();
    }

    public bool TryResolve(string? toolDescription, out string catalogNo)
    {
        catalogNo = string.Empty;
        if (string.IsNullOrWhiteSpace(toolDescription))
            return false;

        if (_map.TryGetValue(Normalize(toolDescription), out var id))
        {
            catalogNo = id;
            return true;
        }

        return false;
    }

    private List<(string Norm, string Description, string CatalogNo)> ParseExcel()
    {
        var result = new List<(string, string, string)>();
        if (!File.Exists(_excelPath))
            return result;

        using var workbook = new XLWorkbook(_excelPath);
        var worksheet = workbook.Worksheet(1);
        var headerRow = worksheet.Row(1);

        int catalogCol = 0, descCol = 0;
        foreach (var cell in headerRow.CellsUsed())
        {
            var header = cell.GetString().Trim().ToLowerInvariant();
            if (catalogCol == 0 && header.Contains("catalog"))
                catalogCol = cell.Address.ColumnNumber;
            else if (descCol == 0 && header.Contains("description"))
                descCol = cell.Address.ColumnNumber;
        }

        if (catalogCol == 0) catalogCol = 2;
        if (descCol == 0) descCol = 3;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var dataRows = worksheet.RangeUsed()?.Rows().Skip(1) ?? Enumerable.Empty<IXLRangeRow>();
        foreach (var row in dataRows)
        {
            var description = row.Cell(descCol).GetString().Trim();
            var catalogNo = row.Cell(catalogCol).GetString().Trim();
            if (string.IsNullOrWhiteSpace(description) || string.IsNullOrWhiteSpace(catalogNo))
                continue;

            var norm = Normalize(description);
            if (norm.Length == 0 || !seen.Add(norm))
                continue;

            result.Add((norm, description, catalogNo));
        }

        return result;
    }

    private void EnsureSchema()
    {
        using var connection = Open();

        // Drop any legacy taegutec_catalog table (e.g. the v2.8.0 schema with a
        // properties_json NOT NULL column) so the current 3-column schema applies cleanly.
        if (TableExists(connection, "taegutec_catalog") && !HasExpectedColumns(connection))
        {
            using var drop = connection.CreateCommand();
            drop.CommandText = "DROP TABLE taegutec_catalog";
            drop.ExecuteNonQuery();
        }

        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS taegutec_catalog (
                description_norm TEXT PRIMARY KEY,
                description TEXT NOT NULL,
                catalog_no TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS app_meta (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );
            """;
        cmd.ExecuteNonQuery();
    }

    private static bool TableExists(SqliteConnection connection, string name)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM sqlite_master WHERE type='table' AND name=$name";
        cmd.Parameters.AddWithValue("$name", name);
        return cmd.ExecuteScalar() != null;
    }

    private static bool HasExpectedColumns(SqliteConnection connection)
    {
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "PRAGMA table_info(taegutec_catalog)";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            columns.Add(reader.GetString(1));

        return columns.Count == 3 &&
               columns.Contains("description_norm") &&
               columns.Contains("description") &&
               columns.Contains("catalog_no");
    }

    private void Replace(IReadOnlyList<(string Norm, string Description, string CatalogNo)> rows)
    {
        using var connection = Open();
        using var tx = connection.BeginTransaction();

        using (var clear = connection.CreateCommand())
        {
            clear.Transaction = tx;
            clear.CommandText = "DELETE FROM taegutec_catalog";
            clear.ExecuteNonQuery();
        }

        using var cmd = connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO taegutec_catalog (description_norm, description, catalog_no)
            VALUES ($norm, $description, $catalogNo)
            ON CONFLICT(description_norm) DO UPDATE SET
                description = excluded.description,
                catalog_no = excluded.catalog_no
            """;
        var pNorm = cmd.CreateParameter(); pNorm.ParameterName = "$norm"; cmd.Parameters.Add(pNorm);
        var pDesc = cmd.CreateParameter(); pDesc.ParameterName = "$description"; cmd.Parameters.Add(pDesc);
        var pId = cmd.CreateParameter(); pId.ParameterName = "$catalogNo"; cmd.Parameters.Add(pId);

        foreach (var (norm, description, catalogNo) in rows)
        {
            pNorm.Value = norm;
            pDesc.Value = description;
            pId.Value = catalogNo;
            cmd.ExecuteNonQuery();
        }

        tx.Commit();
    }

    private void LoadIntoMemory()
    {
        _map.Clear();
        using var connection = Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT description_norm, catalog_no FROM taegutec_catalog";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            _map[reader.GetString(0)] = reader.GetString(1);
    }

    private int GetRowCount()
    {
        using var connection = Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM taegutec_catalog";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private string? GetMeta(string key)
    {
        using var connection = Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT value FROM app_meta WHERE key = $key";
        cmd.Parameters.AddWithValue("$key", key);
        return cmd.ExecuteScalar() as string;
    }

    private void SetMeta(string key, string value)
    {
        using var connection = Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO app_meta (key, value) VALUES ($key, $value)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value
            """;
        cmd.Parameters.AddWithValue("$key", key);
        cmd.Parameters.AddWithValue("$value", value);
        cmd.ExecuteNonQuery();
    }

    private string? ComputeExcelSignature()
    {
        if (!File.Exists(_excelPath))
            return null;
        var info = new FileInfo(_excelPath);
        return $"{info.Length}:{info.LastWriteTimeUtc.Ticks}";
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    private static string Normalize(string value) =>
        WhitespaceRegex().Replace(value.Trim().ToUpperInvariant(), string.Empty);

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
