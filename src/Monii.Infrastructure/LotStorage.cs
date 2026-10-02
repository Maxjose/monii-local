using Microsoft.Data.Sqlite;
using Monii.Domain;
using Monii.Application;
namespace Monii.Infrastructure;
public sealed partial class SqliteStore
{
    private static void MigrateLots(SqliteConnection connection,SqliteTransaction transaction)
    {
        var state=ReadNormalized(connection,transaction);
        foreach(var move in state.Stock.Where(m=>m.Quantity>0&&m.SourceMoveId is null).ToList())
        {
            Guid? saleId=move.Reason.StartsWith("Anulación de venta:")?move.DocumentId:state.Returns.FirstOrDefault(r=>r.Id==move.DocumentId)?.SaleId;
            if(saleId is null)continue;
            var source=state.Stock.SingleOrDefault(m=>m.DocumentId==saleId&&m.ProductId==move.ProductId&&m.Reason=="Venta"&&m.Quantity<0);
            if(source is not null)state.Stock[state.Stock.IndexOf(move)]=move with { SourceMoveId=source.Id };
        }
        PersistState(connection,transaction,state);
        using var command=connection.CreateCommand();command.Transaction=transaction;
        command.CommandText="CREATE INDEX IF NOT EXISTS ix_stock_lot ON stock_moves(product_id,COALESCE(json_extract(payload,'$.LotCode'),'')); PRAGMA user_version=6";command.ExecuteNonQuery();
    }
    private static void ValidateLots(OperationsState state)
    {
        if(state.Stock.GroupBy(m=>(m.ProductId,Code:m.LotCode.ToUpperInvariant(),m.Expiry)).Any(g=>g.Sum(m=>m.Quantity)<0))throw new ArgumentException("Saldo negativo de lote.");
        if(state.Stock.Any(m=>m.LotCode.Length>80||m.LotCode.Any(char.IsControl)||m.Expiry is not null&&m.LotCode.Length==0))throw new ArgumentException("Datos de lote inválidos.");
        if(state.Stock.Where(m=>m.LotCode.Length>0).GroupBy(m=>(m.ProductId,Code:m.LotCode.ToUpperInvariant())).Any(g=>g.Select(m=>m.Expiry).Distinct().Count()>1))throw new ArgumentException("Fechas inconsistentes en un lote.");
        foreach(var group in state.Stock.Where(m=>m.SourceMoveId is not null).GroupBy(m=>m.SourceMoveId))
        {
            var source=state.Stock.FirstOrDefault(m=>m.Id==group.Key);
            if(source is null||source.Quantity>=0||group.Sum(m=>m.Quantity)>-source.Quantity||group.Any(m=>m.Quantity<=0||m.ProductId!=source.ProductId||m.LotCode!=source.LotCode||m.Expiry!=source.Expiry))throw new ArgumentException("Reintegro de lote inconsistente.");
        }
    }
}
