using System.Text.Json;
using Microsoft.Data.Sqlite;
using Monii.Application;
using Monii.Domain;
using Monii.Infrastructure;

internal static class FullFlowChecks
{
    public static void Run()
    {
        var root=Path.Combine(Path.GetTempPath(),"MoniiFullFlow",Guid.NewGuid().ToString("N"));
        var store=new SqliteStore(Path.Combine(root,"business.db"));
        var business=new BusinessService(store); var ops=new OperationsService(store); var count=0;
        void Check(bool okay,string label) { if(!okay) throw new Exception("FALLÓ FLUJO: "+label); count++; Console.WriteLine("OK FLUJO: "+label); }
        string Snapshot()=>JsonSerializer.Serialize(new { Products=store.GetProducts(),Settings=business.Settings,State=ops.State,Audit=store.GetAudit() });
        void Reject(Action action,string label)
        {
            var before=Snapshot();
            try { action(); } catch(ArgumentException) { Check(Snapshot()==before,label+"; sin cambios parciales"); return; }
            throw new Exception("FALLÓ FLUJO: se aceptó "+label);
        }
        var category=new Category(Guid.NewGuid(),"Prueba integral"); store.SaveCategory(category);
        var unit=new Product { Name="Repuesto",Code="FULL-UNIT",Category=category.Name,PriceUsd=10,CostUsd=1 };
        var weight=new Product { Name="Vívere",Code="FULL-KG",Category=category.Name,Unit="Kilogramo",PriceUsd=4,CostUsd=1 };
        business.Save(unit); business.Save(weight);
        business.SaveSettings(business.Settings with { ShowBcv=true,BcvRate=50,ShowManualVes=true,ManualVesRate=60,ShowCop=true,CopRate=4000 });
        var supplier=new Contact { Name="Proveedor integral" }; ops.SaveContact(supplier,true);
        var customer=new Contact { Name="Cliente integral",CreditLimit=100 }; ops.SaveContact(customer,false);
        ops.OpenCash(500,0,0);
        var buy=ops.Buy(supplier.Id,"FULL-001",[(unit.Id,10,3),(weight.Id,2.5m,2)],new(Currency.USD,PaymentMethod.Efectivo,35));
        Check(buy.Total==35&&store.StockBalance(unit.Id)==10&&store.StockBalance(weight.Id)==2.5m,"Compra mixta entera/fraccionaria con total exacto");
        Check(business.Products().Single(p=>p.Id==unit.Id).CostUsd==3&&business.Products().Single(p=>p.Id==weight.Id).CostUsd==2,"Compra actualiza ambos costos");
        var session=OperationsService.OpenSession(ops.State)!;
        decimal Cash(string currency)=>OperationsService.Expected(ops.State,session,currency);
        Check(Cash("USD")==465,"Compra descuenta efectivo USD");
        Reject(()=>ops.Buy(supplier.Id,"INVALID",[(unit.Id,1,99),(weight.Id,-1,2)],new(Currency.USD,PaymentMethod.Efectivo,97)),"Compra con segunda línea inválida");
        var paid=ops.Sell([(unit.Id,2),(weight.Id,.5m)],2,[new(Currency.USD,PaymentMethod.Efectivo,5),new(Currency.VES_BCV,PaymentMethod.Efectivo,500),new(Currency.COP,PaymentMethod.Efectivo,20000)]);
        Check(paid.Total==20&&paid.InitialDebt==0&&paid.ChangeUsd==0,"Venta con descuento y tres monedas cuadra a USD");
        Check(store.StockBalance(unit.Id)==8&&store.StockBalance(weight.Id)==2,"Venta mixta descuenta ambas existencias");
        Check(Cash("USD")==470&&Cash("VES")==500&&Cash("COP")==20000,"Venta conserva cada efectivo físico");
        Check(OperationsService.Margin(paid)==13,"Margen utiliza costos históricos y descuento");
        var credit=ops.Sell([(unit.Id,3)],0,[new(Currency.VES_Manual,PaymentMethod.Efectivo,600)],customer.Id,DateOnly.FromDateTime(DateTime.Today.AddDays(10)));
        Check(credit.InitialDebt==20&&OperationsService.Debt(ops.State,credit)==20,"Crédito con anticipo manual mantiene deuda USD");
        Check(Cash("VES")==1100&&store.StockBalance(unit.Id)==5,"Crédito actualiza caja e inventario");
        var first=ops.PayDebt(credit.Id,new(Currency.COP,PaymentMethod.Efectivo,40000));
        Check(first.Usd==10&&OperationsService.Debt(ops.State,credit)==10&&Cash("COP")==60000,"Abono COP reduce saldo y suma caja");
        business.SaveSettings(business.Settings with { BcvRate=100,CopRate=5000 });
        Check(ops.State.Sales.Single(s=>s.Id==paid.Id).Rates.Bcv==50&&ops.State.Abonos.Single().Rates.Cop==4000,"Cambiar tasas no reescribe venta ni abono");
        var second=ops.PayDebt(credit.Id,new(Currency.VES_BCV,PaymentMethod.Efectivo,1000));
        Check(second.Usd==10&&OperationsService.Debt(ops.State,credit)==0&&Cash("VES")==2100,"Abono BCV actualizado liquida crédito");
        Reject(()=>ops.PayDebt(credit.Id,new(Currency.USD,PaymentMethod.Efectivo,1)),"Abonar crédito liquidado");
        Reject(()=>ops.VoidSale(credit.Id,"Con abonos"),"Anular venta con abonos vigentes");
        ops.VoidDebtPayment(second.Id,"Corrección integral");
        Check(OperationsService.Debt(ops.State,credit)==10&&Cash("VES")==1100,"Anular segundo abono restaura deuda y Bs originales");
        ops.VoidDebtPayment(first.Id,"Corrección integral");
        Check(OperationsService.Debt(ops.State,credit)==20&&Cash("COP")==20000,"Anular abono COP usa importe histórico");
        ops.VoidSale(credit.Id,"Corrección integral");
        Check(OperationsService.Debt(ops.State,ops.State.Sales.Single(s=>s.Id==credit.Id))==0&&store.StockBalance(unit.Id)==8&&Cash("VES")==500,"Anular crédito revierte anticipo y stock");
        var returned=ops.ReturnSale(paid.Id,[(unit.Id,1)],"Devolución integral");
        Check(returned.Total==9.09m&&returned.RefundUsd==9.09m&&store.StockBalance(unit.Id)==9,"Devolución parcial prorratea descuento y restituye stock");
        Check(Cash("USD")==460.91m&&OperationsService.NetTotal(ops.State,paid)==10.91m,"Reintegro y venta neta conservan centavos");
        Reject(()=>ops.ReturnSale(paid.Id,[(unit.Id,2)],"Exceso"),"Devolución superior al remanente");
        business.SaveSettings(business.Settings with { ShowCop=false });
        Reject(()=>ops.CashMovement(new(Currency.COP,PaymentMethod.Efectivo,5000),false,"Inactiva"),"Ingreso en moneda desactivada");
        Check(Cash("COP")==20000,"Desactivar moneda conserva efectivo anterior");
        business.Save(unit with { Active=false });
        Reject(()=>ops.Sell([(unit.Id,1)],0,[new(Currency.USD,PaymentMethod.Efectivo,10)]),"Venta de producto inactivo");
        Check(ops.State.Sales.Single(s=>s.Id==paid.Id).Lines.First().Name=="Repuesto","Desactivar producto conserva documento histórico");
        ops.CashMovement(new(Currency.USD,PaymentMethod.Efectivo,10),true,"Gasto integral");
        ops.CashMovement(new(Currency.USD,PaymentMethod.Transferencia,5),false,"Ingreso bancario");
        Check(Cash("USD")==450.91m,"Gasto efectivo y transferencia mantienen caja física correcta");
        var before=Snapshot(); var backup=store.Backup(Path.Combine(root,"full-backup.db"));
        ops.Adjust(weight.Id,.125m,"Cambio posterior al respaldo");
        var recovery=store.Restore(backup);
        Check(Snapshot()==before,"Respaldo/restauración recupera documentos, productos, configuración y auditoría");
        Check(new SqliteStore(recovery).StockBalance(weight.Id)==2.125m,"Copia previa a restauración conserva cambios posteriores");
        ops.CloseCash(Cash("USD"),Cash("VES"),Cash("COP"));
        var closed=ops.State.Sessions.Single();
        Check(closed.CountedUsd==closed.ExpectedUsd&&closed.CountedVes==closed.ExpectedVes&&closed.CountedCop==closed.ExpectedCop,"Cierre concilia USD, Bs y COP inactivo con saldo");
        Check(new OperationsService(new SqliteStore(store.DatabasePath)).State.Sessions.Single().ClosedAt is not null,"Reabrir SQLite conserva cierre completo");
        using(var connection=new SqliteConnection("Data Source="+store.DatabasePath))
        { connection.Open(); using var command=connection.CreateCommand(); command.CommandText="PRAGMA integrity_check"; Check((string?)command.ExecuteScalar()=="ok","Integridad SQLite tras ciclo completo"); }
        Console.WriteLine($"FLUJO INTEGRAL COMPLETO: {count} comprobaciones. Datos: {root}");
    }
}
