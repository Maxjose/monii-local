using Microsoft.Data.Sqlite;
using Monii.Domain;
using System.Text.Json;
using System.Security.Cryptography;
namespace Monii.Infrastructure;

public sealed partial class SqliteStore
{
    public string NetworkEpoch { get { using var c=Open();using var cmd=c.CreateCommand();cmd.CommandText="SELECT epoch FROM network_meta WHERE id=1";return (string)cmd.ExecuteScalar()!; } }
    public void RenewNetworkEpoch() { using var c=Open();using var cmd=c.CreateCommand();cmd.CommandText="UPDATE network_meta SET epoch=lower(hex(randomblob(16))) WHERE id=1";cmd.ExecuteNonQuery(); }
    public Guid? TerminalId { get; set; }
    public string NetworkTerminalName { get; set; }="Equipo local";
    public string CashName => TerminalId is null ? "Equipo local" : GetSettings().IndependentCash ? NetworkTerminalName : "Caja compartida";
    public string CashScope => TerminalId is null ? "local" : GetSettings().IndependentCash ? TerminalId.Value.ToString("N") : "shared";
    public Guid? RequestId { get; set; }
    public string RequestHash { get; set; } = "";
    public string SessionStamp(Guid userId)
    {
        using var c=Open(); using var cmd=c.CreateCommand();
        cmd.CommandText="SELECT username||name||role||password_hash FROM users WHERE id=$id AND active=1"; cmd.Parameters.AddWithValue("$id",userId.ToString());
        return cmd.ExecuteScalar() is string value ? Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value))) : "";
    }
    public void ResumeSession(SessionUser user,string stamp)
    {
        if(stamp.Length==0||SessionStamp(user.Id)!=stamp) throw new UnauthorizedAccessException("La sesión cambió. Vuelve a iniciar sesión.");
        CurrentUser=user;
    }
    private static void MigrateNetwork(SqliteConnection c,SqliteTransaction tx)
    {
        using var cmd=c.CreateCommand();cmd.Transaction=tx;
        cmd.CommandText="""
            DROP INDEX ix_one_open_cash;
            CREATE UNIQUE INDEX ix_one_open_cash ON cash_sessions(COALESCE(json_extract(payload,'$.CashScope'),'local')) WHERE closed IS NULL;
            CREATE TABLE network_meta(id INTEGER PRIMARY KEY CHECK(id=1),epoch TEXT NOT NULL);
            INSERT INTO network_meta VALUES(1,lower(hex(randomblob(16))));
            CREATE TABLE request_receipts(id TEXT PRIMARY KEY,user_id TEXT NOT NULL,terminal_id TEXT NOT NULL,hash TEXT NOT NULL,result TEXT NOT NULL,at TEXT NOT NULL);
            PRAGMA user_version=5;
            """;
        cmd.ExecuteNonQuery();
    }
    private bool TryReceipt<T>(SqliteConnection c,SqliteTransaction tx,out T? result)
    {
        result=default;if(RequestId is null) return false;
        using var cmd=c.CreateCommand();cmd.Transaction=tx;cmd.CommandText="SELECT hash,result,user_id,terminal_id FROM request_receipts WHERE id=$id";cmd.Parameters.AddWithValue("$id",RequestId.ToString());
        using var reader=cmd.ExecuteReader();if(!reader.Read())return false;
        if(reader.GetString(0)!=RequestHash||reader.GetString(2)!=CurrentUser?.Id.ToString()||reader.GetString(3)!=TerminalId?.ToString()) throw new ArgumentException("El identificador de la operación ya se utilizó con otros datos.");
        result=JsonSerializer.Deserialize<T>(reader.GetString(1));return true;
    }
    private void SaveReceipt<T>(SqliteConnection c,SqliteTransaction tx,T result)
    {
        if(RequestId is null)return;
        using var cmd=c.CreateCommand();cmd.Transaction=tx;
        cmd.CommandText="INSERT INTO request_receipts VALUES($id,$user,$terminal,$hash,$result,$at)";
        cmd.Parameters.AddWithValue("$id",RequestId.ToString());cmd.Parameters.AddWithValue("$user",CurrentUser!.Id.ToString());cmd.Parameters.AddWithValue("$terminal",TerminalId!.ToString());cmd.Parameters.AddWithValue("$hash",RequestHash);cmd.Parameters.AddWithValue("$result",JsonSerializer.Serialize(result));cmd.Parameters.AddWithValue("$at",DateTimeOffset.UtcNow.ToString("O"));cmd.ExecuteNonQuery();
    }
}
