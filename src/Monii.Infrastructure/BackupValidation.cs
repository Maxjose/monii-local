using Microsoft.Data.Sqlite;
using Monii.Domain;

namespace Monii.Infrastructure;
public sealed partial class SqliteStore
{
    private static void ValidateReturns(OperationsState state)
    {
        foreach(var group in state.Returns.GroupBy(r=>r.SaleId))
        {
            var sale=state.Sales.SingleOrDefault(s=>s.Id==group.Key);
            if(sale is null||sale.Voided||group.Sum(r=>r.Total)>sale.Total||group.Any(r=>r.Total<0||r.CostReduction<0||r.DebtReduction<0||r.RefundUsd<0||r.Total!=r.DebtReduction+r.RefundUsd||!Enum.IsDefined(r.RefundMethod)||r.Lines.Count==0)) throw new ArgumentException("Devoluciones inválidas en el respaldo.");
            foreach(var line in group.SelectMany(r=>r.Lines).GroupBy(l=>l.ProductId))
            {
                var original=sale.Lines.SingleOrDefault(l=>l.ProductId==line.Key);
                if(original is null||line.Any(l=>l.Quantity<=0)||line.Sum(l=>l.Quantity)>original.Quantity) throw new ArgumentException("Cantidades de devolución inválidas en el respaldo.");
            }
        }
    }
    private static void ValidateNormalizedBackup(SqliteConnection c)
    {
        using var cmd=c.CreateCommand();
        foreach(var table in new[]{"products","customers","suppliers","sales","purchases","stock_moves","cash_sessions","cash_entries","credit_payments","sale_returns"})
        {
            cmd.CommandText=$"SELECT COUNT(*) FROM {table} WHERE id<>json_extract(payload,'$.Id')";
            if(Convert.ToInt32(cmd.ExecuteScalar())>0) throw new ArgumentException("Identidad inconsistente en respaldo.");
        }
        cmd.CommandText="SELECT COUNT(*) FROM stock_moves WHERE product_id<>json_extract(payload,'$.ProductId') OR quantity_milli<>CAST(round(json_extract(payload,'$.Quantity')*1000) AS INTEGER)";
        if(Convert.ToInt32(cmd.ExecuteScalar())>0) throw new ArgumentException("Existencias e índices inconsistentes en respaldo.");
        foreach(var (table,parent,fk) in new[]{("sale_lines","sales","sale_id"),("purchase_lines","purchases","purchase_id")})
        {
            cmd.CommandText=$"SELECT COUNT(*) FROM {table} l JOIN {parent} p ON p.id=l.{fk} WHERE l.product_id<>json_extract(l.payload,'$.ProductId') OR json_extract(p.payload,'$.Lines['||l.ordinal||']') IS NULL OR json(l.payload)<>json(json_extract(p.payload,'$.Lines['||l.ordinal||']'))";
            if(Convert.ToInt32(cmd.ExecuteScalar())>0) throw new ArgumentException("Líneas inconsistentes en respaldo.");
            cmd.CommandText=$"SELECT COUNT(*) FROM {parent} p WHERE json_array_length(p.payload,'$.Lines')<>(SELECT COUNT(*) FROM {table} l WHERE l.{fk}=p.id)";
            if(Convert.ToInt32(cmd.ExecuteScalar())>0) throw new ArgumentException("Líneas faltantes en respaldo.");
        }
        cmd.CommandText="SELECT COUNT(*) FROM users";
        if(Convert.ToInt32(cmd.ExecuteScalar())>0)
        {
            cmd.CommandText="SELECT COUNT(*) FROM users WHERE active=1 AND role=0";
            if(Convert.ToInt32(cmd.ExecuteScalar())==0) throw new ArgumentException("El respaldo no conserva un administrador activo.");
            cmd.CommandText="SELECT role,salt,password_hash FROM users"; using var reader=cmd.ExecuteReader();
            while(reader.Read()) if(!Enum.IsDefined((UserRole)reader.GetInt32(0))||Convert.FromBase64String(reader.GetString(1)).Length!=32||Convert.FromBase64String(reader.GetString(2)).Length!=32) throw new ArgumentException("Cuenta inválida en respaldo.");
        }
    }
}
