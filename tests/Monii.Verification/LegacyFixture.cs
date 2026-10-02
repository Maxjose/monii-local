using Microsoft.Data.Sqlite;
using Monii.Infrastructure;
using System.Text.Json;

internal static class LegacyFixture
{
    public static void Downgrade(SqliteStore store,int version)
    {
        var state=store.ReadOperations(); using var c=new SqliteConnection($"Data Source={store.DatabasePath}"); c.Open(); using var cmd=c.CreateCommand();
        cmd.CommandText="DROP TABLE IF EXISTS categories; DROP TABLE sale_returns; DROP TABLE credit_payments; DROP TABLE cash_entries; DROP TABLE cash_sessions; DROP TABLE stock_moves; DROP TABLE purchase_lines; DROP TABLE purchases; DROP TABLE sale_lines; DROP TABLE sales; DROP TABLE suppliers; DROP TABLE customers; DROP TABLE users; ALTER TABLE audit DROP COLUMN actor;"; cmd.ExecuteNonQuery();
        if(version==2) { cmd.CommandText="CREATE TABLE operations(id INTEGER PRIMARY KEY CHECK(id=1),payload TEXT NOT NULL); INSERT INTO operations VALUES(1,$payload)"; cmd.Parameters.AddWithValue("$payload",JsonSerializer.Serialize(state)); cmd.ExecuteNonQuery(); }
        cmd.Parameters.Clear(); cmd.CommandText="PRAGMA user_version="+version; cmd.ExecuteNonQuery();
    }
}
