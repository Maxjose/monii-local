using Microsoft.Data.Sqlite;
using Monii.Application;
using Monii.Domain;
using System.Globalization;
using System.Text.Json;

namespace Monii.Infrastructure;

public sealed partial class SqliteStore : IMoniiStore
{
    private readonly string connectionString;
    public string DatabasePath { get; }
    public string? BackupWarning { get; set; }
    public SqliteStore(string path)
    {
        DatabasePath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);
        connectionString = new SqliteConnectionStringBuilder { DataSource = DatabasePath, ForeignKeys = true }.ToString();
        using var connection = Open();
        using (var probe = connection.CreateCommand())
        {
            probe.CommandText = "PRAGMA user_version";
            var existingVersion = Convert.ToInt32(probe.ExecuteScalar(), CultureInfo.InvariantCulture);
            if (existingVersion is 1 or 2 or 3 or 4 or 5)
            {
                var copy = DatabasePath + (existingVersion == 1 ? ".before-v2-" : existingVersion==2 ? ".before-v3-" : existingVersion==3 ? ".before-v4-" : existingVersion==4 ? ".before-v5-" : ".before-v6-") + Guid.NewGuid().ToString("N") + ".db";
                using var backup = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = copy, Pooling = false }.ToString());
                backup.Open(); connection.BackupDatabase(backup);
            }
        }
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "PRAGMA user_version";
        var version = Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
        if (version > 6) throw new InvalidOperationException("Esta base de datos requiere una versión más reciente de Monii.");
        if (version == 0)
        {
            command.CommandText = """
                CREATE TABLE products(id TEXT PRIMARY KEY, code TEXT NOT NULL COLLATE NOCASE UNIQUE, payload TEXT NOT NULL);
                CREATE TABLE settings(id INTEGER PRIMARY KEY CHECK(id=1), payload TEXT NOT NULL);
                CREATE TABLE audit(id INTEGER PRIMARY KEY AUTOINCREMENT, at TEXT NOT NULL, action TEXT NOT NULL, details TEXT NOT NULL);
                PRAGMA user_version=1;
                """;
            command.ExecuteNonQuery();
        }
        if (version < 2)
        {
            command.CommandText = "CREATE TABLE operations(id INTEGER PRIMARY KEY CHECK(id=1), payload TEXT NOT NULL); PRAGMA user_version=2;";
            command.ExecuteNonQuery();
        }
        if (version < 3) MigrateNormalized(connection, transaction);
        if (version < 4) MigrateCategories(connection,transaction);
        if (version < 5) MigrateNetwork(connection,transaction);
        if(version<6)MigrateLots(connection,transaction);
        transaction.Commit();
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA busy_timeout=5000";
        command.ExecuteNonQuery();
        return connection;
    }

    public IReadOnlyList<Product> GetProducts()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT payload FROM products";
        using var reader = command.ExecuteReader();
        var products = new List<Product>();
        while (reader.Read()) products.Add(JsonSerializer.Deserialize<Product>(reader.GetString(0))!);
        return products;
    }

    public void SaveProduct(Product product) => Write(
        "INSERT INTO products(id,code,payload) VALUES($id,$code,$payload) ON CONFLICT(id) DO UPDATE SET code=$code,payload=$payload",
        [("$id", product.Id.ToString()), ("$code", product.Code), ("$payload", JsonSerializer.Serialize(product))],
        product.Active ? "Producto guardado" : "Producto desactivado", JsonSerializer.Serialize(product));

    public BusinessSettings GetSettings()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT payload FROM settings WHERE id=1";
        return command.ExecuteScalar() is string payload ? JsonSerializer.Deserialize<BusinessSettings>(payload)! : new();
    }

    public void SaveSettings(BusinessSettings settings) => Write(
        "INSERT INTO settings(id,payload) VALUES(1,$payload) ON CONFLICT(id) DO UPDATE SET payload=$payload",
        [("$payload", JsonSerializer.Serialize(settings))], "Configuración guardada", JsonSerializer.Serialize(settings));

    private void Write(string sql, (string Key, string Value)[] parameters, string action, string details)
    {
        Require(action.StartsWith("Producto", StringComparison.Ordinal) ? Permission.Products : Permission.Settings);
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        if(action.StartsWith("Producto",StringComparison.Ordinal)) ValidateProductCategory(connection,transaction,JsonSerializer.Deserialize<Product>(parameters.Single(p=>p.Key=="$payload").Value.ToString()!)!);
        foreach (var parameter in parameters) command.Parameters.AddWithValue(parameter.Key, parameter.Value);
        command.ExecuteNonQuery();
        command.Parameters.Clear();
        command.CommandText = "INSERT INTO audit(at,action,details,actor) VALUES($at,$action,$details,$actor)";
        command.Parameters.AddWithValue("$actor", CurrentUser?.Name ?? "Histórico sin usuario");
        command.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$action", action);
        command.Parameters.AddWithValue("$details", details);
        command.ExecuteNonQuery();
        transaction.Commit();
    }

    public IReadOnlyList<AuditEntry> GetAudit()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT at,action,details,actor FROM audit ORDER BY id DESC LIMIT 100";
        using var reader = command.ExecuteReader();
        var entries = new List<AuditEntry>();
        while (reader.Read()) entries.Add(new(DateTimeOffset.Parse(reader.GetString(0), CultureInfo.InvariantCulture), reader.GetString(1), reader.GetString(2)) { ActorName = reader.GetString(3) });
        return entries;
    }
}
