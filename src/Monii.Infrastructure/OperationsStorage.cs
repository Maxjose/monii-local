using Microsoft.Data.Sqlite;
using Monii.Domain;
using System.Text.Json;
using Monii.Application;

namespace Monii.Infrastructure;

public sealed partial class SqliteStore
{
    public OperationsState ReadOperations()
    {
        using var connection = Open();
        return ReadState(connection, null);
    }
    private static OperationsState ReadState(SqliteConnection connection, SqliteTransaction? transaction)
    {
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "PRAGMA user_version";
        if (Convert.ToInt32(command.ExecuteScalar()) >= 3) return ReadNormalized(connection, transaction);
        command.CommandText = "SELECT payload FROM operations WHERE id=1";
        return command.ExecuteScalar() is string payload ? JsonSerializer.Deserialize<OperationsState>(payload) ?? throw new InvalidDataException("Datos operativos inválidos.") : new();
    }
    public T Transact<T>(Func<OperationsState, IReadOnlyList<Product>, BusinessSettings, T> operation, string action, RemoteCommand? remoteCommand=null)
    {
        Require(ActionPermission(action));
        using var connection = Open(); using var transaction = connection.BeginTransaction(deferred: false);
        if(TryReceipt<T>(connection,transaction,out var replay)) return replay!;
        var state = ReadState(connection, transaction);
        var previous=Snapshot(state);
        var previousLimits=state.Customers.ToDictionary(c=>c.Id,c=>c.CreditLimit);
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "SELECT payload FROM products";
        var products = new List<Product>();
        using (var reader = command.ExecuteReader()) while (reader.Read()) products.Add(JsonSerializer.Deserialize<Product>(reader.GetString(0))!);
        command.CommandText = "SELECT payload FROM settings WHERE id=1";
        var settings = command.ExecuteScalar() is string json ? JsonSerializer.Deserialize<BusinessSettings>(json)! : new();
        var previousPurchases = state.Purchases.Count;
        var previousSales = state.Sales.Count;
        var previousReturns = state.Returns.Count;
        var result = operation(state, products, settings);
        if(action=="Cliente guardado"&&state.Customers.Any(c=>c.CreditLimit!=previousLimits.GetValueOrDefault(c.Id))) Require(Permission.Credit);
        foreach (var sale in state.Sales.Skip(previousSales).ToList())
        {
            if(sale.InitialDebt>0) Require(Permission.Credit);
            var attributed = sale with { SellerId = CurrentUser?.Id, SellerName = CurrentUser?.Name ?? "Histórico sin usuario" };
            state.Sales[state.Sales.IndexOf(sale)] = attributed;
            if (result is Sale returned && returned.Id == sale.Id) result = (T)(object)attributed;
        }
        foreach(var item in state.Returns.Skip(previousReturns).ToList())
        {
            var attributed=item with { ActorName=CurrentUser?.Name??"Histórico sin usuario" };
            state.Returns[state.Returns.IndexOf(item)]=attributed;
            if(result is SaleReturn returned&&returned.Id==item.Id) result=(T)(object)attributed;
        }
        // Receipt, stock, costs, payment and audit commit together.
        foreach (var purchase in state.Purchases.Skip(previousPurchases))
            foreach (var line in purchase.Lines)
            {
                var product = products.Single(p => p.Id == line.ProductId) with { CostUsd = line.Cost };
                command.CommandText = "UPDATE products SET payload=$payload WHERE id=$id";
                command.Parameters.Clear(); command.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(product)); command.Parameters.AddWithValue("$id", product.Id.ToString()); command.ExecuteNonQuery();
            }
        PersistState(connection, transaction, state,previous);
        command.CommandText = "INSERT INTO audit(at,action,details,actor) VALUES($at,$action,$details,$actor)";
        command.Parameters.Clear(); command.Parameters.AddWithValue("$actor", CurrentUser?.Name ?? "Histórico sin usuario");
        command.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("O")); command.Parameters.AddWithValue("$action", action); command.Parameters.AddWithValue("$details", JsonSerializer.Serialize(result)); command.ExecuteNonQuery();
        SaveReceipt(connection,transaction,result);
        transaction.Commit(); return result;
    }

    public string Backup(string destination)
    {
        Require(Permission.Backup);
        return BackupCore(destination);
    }
    private string BackupCore(string destination)
    {
        var path = Path.GetFullPath(destination);
        if (File.Exists(path) || path.Equals(DatabasePath, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Elige un nombre nuevo para el respaldo.");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var source = Open())
            using (var target = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = temporary, Pooling = false }.ToString()))
            { target.Open(); source.BackupDatabase(target); }
            ValidateBackup(temporary);
            File.Move(temporary, path);
            return path;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public void ValidateBackup(string path)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.GetFullPath(path), Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString()); connection.Open();
        using var command = connection.CreateCommand(); command.CommandText = "PRAGMA integrity_check";
        if (command.ExecuteScalar()?.ToString() != "ok") throw new ArgumentException("El respaldo no supera la comprobación de integridad.");
        command.CommandText = "PRAGMA user_version"; var version = Convert.ToInt32(command.ExecuteScalar());
        if (version is < 1 or > 5) throw new ArgumentException("El archivo no es un respaldo compatible de Monii.");
        command.CommandText = "PRAGMA foreign_key_check";
        using (var reader = command.ExecuteReader()) if (reader.Read()) throw new ArgumentException("El respaldo tiene relaciones inválidas.");
        command.CommandText = "SELECT payload FROM products";
        var ids = new HashSet<Guid>();
        using (var reader = command.ExecuteReader()) while (reader.Read()) { var product = JsonSerializer.Deserialize<Product>(reader.GetString(0)) ?? throw new ArgumentException("Producto inválido en respaldo."); ids.Add(product.Id); }
        command.CommandText = "SELECT payload FROM settings WHERE id=1";
        if (command.ExecuteScalar() is string settings) _ = JsonSerializer.Deserialize<BusinessSettings>(settings) ?? throw new ArgumentException("Configuración inválida en respaldo.");
        command.CommandText = "SELECT count(*) FROM audit"; command.ExecuteScalar();
        if (version >= 2)
        {
            var state = ReadState(connection, null);
            if (state.Stock.Any(m => !ids.Contains(m.ProductId)) || state.Sales.SelectMany(s => s.Lines).Any(l => !ids.Contains(l.ProductId)) || state.Purchases.SelectMany(s => s.Lines).Any(l => !ids.Contains(l.ProductId))) throw new ArgumentException("Referencias de productos inválidas en respaldo.");
            if (state.Stock.GroupBy(m => m.ProductId).Any(g => g.Sum(m => m.Quantity) < 0) || state.Sales.Any(s => Monii.Application.OperationsService.Debt(state, s) < 0) || state.Abonos.Any(a => !state.Sales.Any(s => s.Id == a.SaleId)) || state.Sales.Any(s => s.CustomerId is { } id && !state.Customers.Any(c => c.Id == id)) || state.Purchases.Any(p => !state.Suppliers.Any(s => s.Id == p.SupplierId)) || state.Cash.Any(e => !state.Sessions.Any(s => s.Id == e.SessionId)) || state.Sessions.Where(s=>s.ClosedAt is null).GroupBy(s=>s.CashScope).Any(g=>g.Count()>1))
                throw new ArgumentException("El respaldo contiene saldos o relaciones operativas inválidas.");
            ValidateReturns(state);
        }
        if(version>=3) ValidateNormalizedBackup(connection);
        if(version>=5)
        {
            command.CommandText="SELECT epoch FROM network_meta WHERE id=1";
            if(command.ExecuteScalar() is not string epoch||epoch.Length!=32)throw new ArgumentException("Identidad de servidor inválida en respaldo.");
            command.CommandText="SELECT count(*) FROM request_receipts WHERE length(hash)<>64 OR json_valid(result)=0";
            if(Convert.ToInt32(command.ExecuteScalar())>0)throw new ArgumentException("Comprobantes de peticiones inválidos en respaldo.");
        }
        if(version>=4)
        {
            command.CommandText="SELECT name FROM categories"; var categories=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using(var reader=command.ExecuteReader()) while(reader.Read()) if(string.IsNullOrWhiteSpace(reader.GetString(0))||!categories.Add(reader.GetString(0))) throw new ArgumentException("Categorías inválidas en respaldo.");
            command.CommandText="SELECT payload FROM products";
            using(var reader=command.ExecuteReader()) while(reader.Read()) { var product=JsonSerializer.Deserialize<Product>(reader.GetString(0))!; if(product.Category.Length>0&&!categories.Contains(product.Category)) throw new ArgumentException("Producto sin categoría válida en respaldo."); }
        }
    }

    public string Restore(string sourcePath)
    {
        Require(Permission.Restore);
        var source = Path.GetFullPath(sourcePath);
        if (source.Equals(DatabasePath, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Selecciona un respaldo distinto a la base activa.");
        ValidateBackup(source);
        var staging = Path.Combine(Path.GetDirectoryName(DatabasePath)!, "restore-" + Guid.NewGuid().ToString("N") + ".db");
        var recovery = Path.Combine(Path.GetDirectoryName(DatabasePath)!, "recovery-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..6] + ".db");
        try
        {
            File.Copy(source, staging); _ = new SqliteStore(staging); ValidateBackup(staging); Backup(recovery);
            using var incoming = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = staging, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString()); incoming.Open();
            using var target = Open(); incoming.BackupDatabase(target);
            SignOut();
            return recovery;
        }
        finally { SqliteConnection.ClearAllPools(); if (File.Exists(staging)) File.Delete(staging); }
    }

    public void AutomaticBackup()
    {
        var settings = GetSettings(); if (!settings.AutoBackups) return;
        var directory = string.IsNullOrWhiteSpace(settings.BackupDirectory) ? Path.Combine(Path.GetDirectoryName(DatabasePath)!, "backups") : Path.GetFullPath(settings.BackupDirectory);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "monii-auto-" + DateTime.Now.ToString("yyyyMMdd") + ".db");
        if (!File.Exists(path)) BackupCore(path);
        foreach (var file in new DirectoryInfo(directory).GetFiles("monii-auto-????????.db").OrderByDescending(f => f.Name).Skip(14)) file.Delete();
    }
}
