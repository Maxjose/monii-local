using Microsoft.Data.Sqlite;
using Monii.Domain;
using System.Text.Json;

namespace Monii.Infrastructure;

public sealed partial class SqliteStore
{
    private static void MigrateNormalized(SqliteConnection connection, SqliteTransaction transaction)
    {
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "SELECT payload FROM operations WHERE id=1";
        var state = command.ExecuteScalar() is string payload ? JsonSerializer.Deserialize<OperationsState>(payload)! : new();
        command.CommandText = """
            CREATE TABLE customers(id TEXT PRIMARY KEY,payload TEXT NOT NULL);
            CREATE TABLE suppliers(id TEXT PRIMARY KEY,payload TEXT NOT NULL);
            CREATE TABLE sales(id TEXT PRIMARY KEY,number INTEGER NOT NULL UNIQUE,at TEXT NOT NULL,customer_id TEXT REFERENCES customers(id),customer_name TEXT NOT NULL,voided INTEGER NOT NULL,payload TEXT NOT NULL);
            CREATE INDEX ix_sales_at ON sales(at DESC);
            CREATE INDEX ix_sales_customer ON sales(customer_id,voided);
            CREATE TABLE sale_lines(sale_id TEXT NOT NULL REFERENCES sales(id),ordinal INTEGER NOT NULL,product_id TEXT NOT NULL REFERENCES products(id),payload TEXT NOT NULL,PRIMARY KEY(sale_id,ordinal));
            CREATE TABLE purchases(id TEXT PRIMARY KEY,at TEXT NOT NULL,supplier_id TEXT NOT NULL REFERENCES suppliers(id),payload TEXT NOT NULL);
            CREATE INDEX ix_purchases_at ON purchases(at DESC);
            CREATE TABLE purchase_lines(purchase_id TEXT NOT NULL REFERENCES purchases(id),ordinal INTEGER NOT NULL,product_id TEXT NOT NULL REFERENCES products(id),payload TEXT NOT NULL,PRIMARY KEY(purchase_id,ordinal));
            CREATE TABLE stock_moves(id TEXT PRIMARY KEY,product_id TEXT NOT NULL REFERENCES products(id),at TEXT NOT NULL,quantity_milli INTEGER NOT NULL,payload TEXT NOT NULL);
            CREATE INDEX ix_stock_product ON stock_moves(product_id,at);
            CREATE TABLE cash_sessions(id TEXT PRIMARY KEY,opened TEXT NOT NULL,closed TEXT,payload TEXT NOT NULL);
            CREATE UNIQUE INDEX ix_one_open_cash ON cash_sessions((1)) WHERE closed IS NULL;
            CREATE TABLE cash_entries(id TEXT PRIMARY KEY,session_id TEXT NOT NULL REFERENCES cash_sessions(id),document_id TEXT,at TEXT NOT NULL,payload TEXT NOT NULL);
            CREATE INDEX ix_cash_session ON cash_entries(session_id,at);
            CREATE INDEX ix_cash_document ON cash_entries(document_id);
            CREATE TABLE credit_payments(id TEXT PRIMARY KEY,sale_id TEXT NOT NULL REFERENCES sales(id),at TEXT NOT NULL,payload TEXT NOT NULL);
            CREATE INDEX ix_credit_sale ON credit_payments(sale_id,at);
            CREATE TABLE sale_returns(id TEXT PRIMARY KEY,sale_id TEXT NOT NULL REFERENCES sales(id),at TEXT NOT NULL,payload TEXT NOT NULL);
            CREATE INDEX ix_returns_sale ON sale_returns(sale_id,at);
            CREATE TABLE users(id TEXT PRIMARY KEY,username TEXT NOT NULL COLLATE NOCASE UNIQUE,name TEXT NOT NULL,role INTEGER NOT NULL,active INTEGER NOT NULL,salt TEXT NOT NULL,password_hash TEXT NOT NULL,failures INTEGER NOT NULL DEFAULT 0,locked_until TEXT);
            ALTER TABLE audit ADD COLUMN actor TEXT NOT NULL DEFAULT 'Histórico sin usuario';
            """;
        command.ExecuteNonQuery();
        PersistState(connection, transaction, state);
        command.CommandText = "DROP TABLE operations; PRAGMA user_version=3"; command.ExecuteNonQuery();
    }
    private static List<T> ReadRows<T>(SqliteConnection connection, SqliteTransaction? transaction, string table)
    {
        using var command = connection.CreateCommand(); command.Transaction = transaction; command.CommandText = $"SELECT payload FROM {table}";
        using var reader = command.ExecuteReader(); var result = new List<T>();
        while (reader.Read()) result.Add(JsonSerializer.Deserialize<T>(reader.GetString(0)) ?? throw new InvalidDataException("Registro inválido en " + table)); return result;
    }
    private static OperationsState ReadNormalized(SqliteConnection connection, SqliteTransaction? transaction) => new()
    {
        Customers = ReadRows<Contact>(connection,transaction,"customers"), Suppliers = ReadRows<Contact>(connection,transaction,"suppliers"),
        Sales = ReadRows<Sale>(connection,transaction,"sales"), Purchases = ReadRows<Purchase>(connection,transaction,"purchases"),
        Stock = ReadRows<StockMove>(connection,transaction,"stock_moves"), Sessions = ReadRows<CashSession>(connection,transaction,"cash_sessions"),
        Cash = ReadRows<CashEntry>(connection,transaction,"cash_entries"), Abonos = ReadRows<CreditPayment>(connection,transaction,"credit_payments"), Returns = ReadRows<SaleReturn>(connection,transaction,"sale_returns")
    };

    private static Dictionary<string,string> Snapshot(OperationsState state)
    {
        var snapshot=new Dictionary<string,string>();
        void Add(string table,Guid id,object item)=>snapshot.Add(table+id,JsonSerializer.Serialize(item,item.GetType()));
        foreach(var c in state.Customers) Add("customers",c.Id,c); foreach(var c in state.Suppliers) Add("suppliers",c.Id,c);
        foreach(var s in state.Sales) { Add("sales",s.Id,s); for(var i=0;i<s.Lines.Count;i++) snapshot.Add("sale_lines"+s.Id+":"+i,JsonSerializer.Serialize(s.Lines[i])); }
        foreach(var p in state.Purchases) { Add("purchases",p.Id,p); for(var i=0;i<p.Lines.Count;i++) snapshot.Add("purchase_lines"+p.Id+":"+i,JsonSerializer.Serialize(p.Lines[i])); }
        foreach(var m in state.Stock) Add("stock_moves",m.Id,m); foreach(var s in state.Sessions) Add("cash_sessions",s.Id,s);
        foreach(var e in state.Cash) Add("cash_entries",e.Id,e); foreach(var a in state.Abonos) Add("credit_payments",a.Id,a); foreach(var r in state.Returns) Add("sale_returns",r.Id,r);
        return snapshot;
    }
    private static void PersistState(SqliteConnection connection, SqliteTransaction transaction, OperationsState state,Dictionary<string,string>? previous=null)
    {
        void Upsert(string table, object item, (string Column, object? Value)[] keys)
        {
            var payload=JsonSerializer.Serialize(item,item.GetType());
            var snapshotKey=table+keys[0].Value+(table is "sale_lines" or "purchase_lines"?":"+keys[1].Value:"");
            if(previous is not null&&previous.TryGetValue(snapshotKey,out var existing)&&existing==payload) return;
            using var command = connection.CreateCommand(); command.Transaction = transaction;
            var names = keys.Select(k => k.Column).Append("payload").ToList();
            command.CommandText = $"INSERT INTO {table}({string.Join(',',names)}) VALUES({string.Join(',', names.Select(n => "$"+n))}) ON CONFLICT DO UPDATE SET {string.Join(',', names.Select(n => n+"=excluded."+n))} WHERE {table}.payload<>excluded.payload";
            foreach (var key in keys) command.Parameters.AddWithValue("$"+key.Column, key.Value ?? DBNull.Value);
            command.Parameters.AddWithValue("$payload", payload); command.ExecuteNonQuery();
        }
        foreach (var c in state.Customers) Upsert("customers", c, [("id",c.Id.ToString())]);
        foreach (var c in state.Suppliers) Upsert("suppliers", c, [("id",c.Id.ToString())]);
        foreach (var s in state.Sales)
        {
            Upsert("sales",s,[("id",s.Id.ToString()),("number",s.Number),("at",s.At.ToUniversalTime().ToString("O")),("customer_id",s.CustomerId?.ToString()),("customer_name",s.CustomerName),("voided",s.Voided ? 1 : 0)]);
            for (var i=0;i<s.Lines.Count;i++) Upsert("sale_lines",s.Lines[i],[("sale_id",s.Id.ToString()),("ordinal",i),("product_id",s.Lines[i].ProductId.ToString())]);
        }
        foreach (var p in state.Purchases)
        {
            Upsert("purchases",p,[("id",p.Id.ToString()),("at",p.At.ToUniversalTime().ToString("O")),("supplier_id",p.SupplierId.ToString())]);
            for (var i=0;i<p.Lines.Count;i++) Upsert("purchase_lines",p.Lines[i],[("purchase_id",p.Id.ToString()),("ordinal",i),("product_id",p.Lines[i].ProductId.ToString())]);
        }
        foreach(var m in state.Stock) Upsert("stock_moves",m,[("id",m.Id.ToString()),("product_id",m.ProductId.ToString()),("at",m.At.ToUniversalTime().ToString("O")),("quantity_milli",checked((long)(m.Quantity*1000)))]);
        foreach(var s in state.Sessions) Upsert("cash_sessions",s,[("id",s.Id.ToString()),("opened",s.OpenedAt.ToUniversalTime().ToString("O")),("closed",s.ClosedAt?.ToUniversalTime().ToString("O"))]);
        foreach(var e in state.Cash) Upsert("cash_entries",e,[("id",e.Id.ToString()),("session_id",e.SessionId.ToString()),("document_id",e.DocumentId?.ToString()),("at",e.At.ToUniversalTime().ToString("O"))]);
        foreach(var a in state.Abonos) Upsert("credit_payments",a,[("id",a.Id.ToString()),("sale_id",a.SaleId.ToString()),("at",a.At.ToUniversalTime().ToString("O"))]);
        foreach(var r in state.Returns) Upsert("sale_returns",r,[("id",r.Id.ToString()),("sale_id",r.SaleId.ToString()),("at",r.At.ToUniversalTime().ToString("O"))]);
    }

    public IReadOnlyList<Sale> SalesPage(int page, string search = "", int size = 50)
    {
        if (page < 0 || size is < 1 or > 200) throw new ArgumentException("Paginación inválida.");
        using var connection = Open(); using var command = connection.CreateCommand();
        command.CommandText = "SELECT payload FROM sales WHERE customer_name LIKE $search ESCAPE '!' OR CAST(number AS TEXT) LIKE $search ESCAPE '!' ORDER BY at DESC,number DESC LIMIT $size OFFSET $offset";
        command.Parameters.AddWithValue("$search", "%"+search.Replace("!","!!").Replace("%","!%").Replace("_","!_")+"%"); command.Parameters.AddWithValue("$size",size); command.Parameters.AddWithValue("$offset",checked(page*size));
        using var reader=command.ExecuteReader(); var result=new List<Sale>(); while(reader.Read()) result.Add(JsonSerializer.Deserialize<Sale>(reader.GetString(0))!); return result;
    }
    public decimal StockBalance(Guid productId)
    {
        using var connection=Open(); using var command=connection.CreateCommand(); command.CommandText="SELECT COALESCE(SUM(quantity_milli),0) FROM stock_moves WHERE product_id=$id"; command.Parameters.AddWithValue("$id",productId.ToString()); return Convert.ToDecimal(command.ExecuteScalar())/1000m;
    }
    public IReadOnlyDictionary<Guid,decimal> StockBalances()
    {
        using var c=Open(); using var cmd=c.CreateCommand(); cmd.CommandText="SELECT product_id,SUM(quantity_milli) FROM stock_moves GROUP BY product_id";
        using var r=cmd.ExecuteReader(); var balances=new Dictionary<Guid,decimal>(); while(r.Read()) balances.Add(Guid.Parse(r.GetString(0)),r.GetInt64(1)/1000m); return balances;
    }
}
