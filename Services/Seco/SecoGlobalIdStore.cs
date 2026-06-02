using System.Text.RegularExpressions;
using ClosedXML.Excel;
using Microsoft.Data.Sqlite;

namespace AutoToolCatalog.Services.Seco;

public interface ISecoGlobalIdStore
{
    int Count { get; }
    bool TryResolve(string? toolDescription, out string globalId);
}

/// <summary>
/// Fast SECO item-number lookup. Seeds a SQLite table from the master Excel once (re-seeds when the
/// file changes) and keeps an in-memory normalized dictionary for O(1) description → global-number lookups.
/// </summary>
public sealed partial class SecoGlobalIdStore : ISecoGlobalIdStore
{
    private readonly string _excelPath;
    private readonly string _connectionString;
    private readonly Dictionary<string, string> _map = new(StringComparer.Ordinal);

    public SecoGlobalIdStore(string excelPath, string dbPath)
    {
        _excelPath = excelPath;
        _connectionString = new SqliteConnectionStringBuilder { DataSource = dbPath }.ConnectionString;
    }

    public int Count => _map.Count;

    public void Initialize()
    {
        EnsureSchema();

        var signature = ComputeExcelSignature();
        if (signature != null && signature == GetMeta("seco_global_ids_signature") && GetRowCount() > 0)
        {
            LoadIntoMemory();
            return;
        }

        var rows = ParseExcel();
        if (rows.Count > 0)
        {
            Replace(rows);
            SetMeta("seco_global_ids_signature", signature ?? string.Empty);
        }

        LoadIntoMemory();
    }

    public bool TryResolve(string? toolDescription, out string globalId)
    {
        globalId = string.Empty;
        if (string.IsNullOrWhiteSpace(toolDescription))
            return false;

        if (_map.TryGetValue(Normalize(toolDescription), out var id))
        {
            globalId = id;
            return true;
        }

        return false;
    }

    private List<(string Norm, string Description, string GlobalId)> ParseExcel()
    {
        var result = new List<(string, string, string)>();
        if (!File.Exists(_excelPath))
            return result;

        using var workbook = new XLWorkbook(_excelPath);
        var worksheet = workbook.Worksheet(1);
        var headerRow = worksheet.Row(1);

        int idCol = 0, descCol = 0;
        foreach (var cell in headerRow.CellsUsed())
        {
            var header = cell.GetString().Trim().ToLowerInvariant();
            if (idCol == 0 && header.Contains("global"))
                idCol = cell.Address.ColumnNumber;
            else if (descCol == 0 && header.Contains("description"))
                descCol = cell.Address.ColumnNumber;
        }

        if (idCol == 0) idCol = 2;
        if (descCol == 0) descCol = 3;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var dataRows = worksheet.RangeUsed()?.Rows().Skip(1) ?? Enumerable.Empty<IXLRangeRow>();
        foreach (var row in dataRows)
        {
            var description = row.Cell(descCol).GetString().Trim();
            var globalId = row.Cell(idCol).GetString().Trim();
            if (string.IsNullOrWhiteSpace(description) || string.IsNullOrWhiteSpace(globalId))
                continue;

            var norm = Normalize(description);
            if (norm.Length == 0 || !seen.Add(norm))
                continue;

            result.Add((norm, description, globalId));
        }

        return result;
    }

    private void EnsureSchema()
    {
        using var connection = Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS seco_global_ids (
                description_norm TEXT PRIMARY KEY,
                description TEXT NOT NULL,
                global_id TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS app_meta (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );
            """;
        cmd.ExecuteNonQuery();
    }

    private void Replace(IReadOnlyList<(string Norm, string Description, string GlobalId)> rows)
    {
        using var connection = Open();
        using var tx = connection.BeginTransaction();

        using (var clear = connection.CreateCommand())
        {
            clear.Transaction = tx;
            clear.CommandText = "DELETE FROM seco_global_ids";
            clear.ExecuteNonQuery();
        }

        using var cmd = connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO seco_global_ids (description_norm, description, global_id)
            VALUES ($norm, $description, $globalId)
            ON CONFLICT(description_norm) DO UPDATE SET
                description = excluded.description,
                global_id = excluded.global_id
            """;
        var pNorm = cmd.CreateParameter(); pNorm.ParameterName = "$norm"; cmd.Parameters.Add(pNorm);
        var pDesc = cmd.CreateParameter(); pDesc.ParameterName = "$description"; cmd.Parameters.Add(pDesc);
        var pId = cmd.CreateParameter(); pId.ParameterName = "$globalId"; cmd.Parameters.Add(pId);

        foreach (var (norm, description, globalId) in rows)
        {
            pNorm.Value = norm;
            pDesc.Value = description;
            pId.Value = globalId;
            cmd.ExecuteNonQuery();
        }

        tx.Commit();
    }

    private void LoadIntoMemory()
    {
        _map.Clear();
        using var connection = Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT description_norm, global_id FROM seco_global_ids";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            _map[reader.GetString(0)] = reader.GetString(1);
    }

    private int GetRowCount()
    {
        using var connection = Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM seco_global_ids";
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
