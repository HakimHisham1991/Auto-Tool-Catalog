using Microsoft.Data.Sqlite;

namespace AutoToolCatalog.Data;

public class CatalogRepository : ICatalogRepository
{
    private readonly string _connectionString;

    public CatalogRepository(IConfiguration configuration, IWebHostEnvironment environment)
    {
        var dbPath = configuration["CatalogDb:Path"];
        if (string.IsNullOrWhiteSpace(dbPath))
        {
            var dataDir = Path.Combine(environment.ContentRootPath, "Data");
            Directory.CreateDirectory(dataDir);
            dbPath = Path.Combine(dataDir, "catalog.db");
        }

        _connectionString = new SqliteConnectionStringBuilder { DataSource = dbPath }.ConnectionString;
    }

    public void Initialize()
    {
        using var connection = Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS raw_products (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                session_id TEXT NOT NULL,
                record_index INTEGER NOT NULL,
                supplier TEXT NOT NULL,
                product_url TEXT,
                item_number TEXT,
                raw_json TEXT NOT NULL,
                fetched_utc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS product_attributes (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                session_id TEXT NOT NULL,
                record_index INTEGER NOT NULL,
                attribute_key TEXT NOT NULL,
                attribute_value TEXT NOT NULL,
                UNIQUE(session_id, record_index, attribute_key)
            );

            CREATE INDEX IF NOT EXISTS ix_raw_products_session ON raw_products(session_id, record_index);
            CREATE INDEX IF NOT EXISTS ix_attributes_session ON product_attributes(session_id, record_index);
            """;
        cmd.ExecuteNonQuery();
    }

    public void SaveRawProduct(string sessionId, int recordIndex, string supplier, string? productUrl, string? itemNumber, string rawJson)
    {
        using var connection = Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO raw_products (session_id, record_index, supplier, product_url, item_number, raw_json, fetched_utc)
            VALUES ($sessionId, $recordIndex, $supplier, $productUrl, $itemNumber, $rawJson, $fetchedUtc)
            """;
        cmd.Parameters.AddWithValue("$sessionId", sessionId);
        cmd.Parameters.AddWithValue("$recordIndex", recordIndex);
        cmd.Parameters.AddWithValue("$supplier", supplier);
        cmd.Parameters.AddWithValue("$productUrl", (object?)productUrl ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$itemNumber", (object?)itemNumber ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$rawJson", rawJson);
        cmd.Parameters.AddWithValue("$fetchedUtc", DateTime.UtcNow.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    public void SaveAttributes(string sessionId, int recordIndex, IReadOnlyDictionary<string, string> attributes)
    {
        using var connection = Open();
        using var tx = connection.BeginTransaction();
        foreach (var (key, value) in attributes)
        {
            using var cmd = connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO product_attributes (session_id, record_index, attribute_key, attribute_value)
                VALUES ($sessionId, $recordIndex, $key, $value)
                ON CONFLICT(session_id, record_index, attribute_key)
                DO UPDATE SET attribute_value = excluded.attribute_value
                """;
            cmd.Parameters.AddWithValue("$sessionId", sessionId);
            cmd.Parameters.AddWithValue("$recordIndex", recordIndex);
            cmd.Parameters.AddWithValue("$key", key);
            cmd.Parameters.AddWithValue("$value", value);
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }

    public IReadOnlyDictionary<string, string> GetAttributes(string sessionId, int recordIndex)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using var connection = Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT attribute_key, attribute_value
            FROM product_attributes
            WHERE session_id = $sessionId AND record_index = $recordIndex
            """;
        cmd.Parameters.AddWithValue("$sessionId", sessionId);
        cmd.Parameters.AddWithValue("$recordIndex", recordIndex);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            result[reader.GetString(0)] = reader.GetString(1);
        return result;
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }
}
