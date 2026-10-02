using Monii.Application;
using Monii.Domain;
using Monii.Infrastructure;
using Microsoft.Data.Sqlite;
internal static class LotChecks
{
    public static void Run()
    {
        var root=Path.Combine(Path.GetTempPath(),"MoniiLots",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        var store=new SqliteStore(Path.Combine(root,"lots.db"));var business=new BusinessService(store);var ops=new OperationsService(store);var passed=0;
        void Check(bool value,string label) { if(!value)throw new Exception(label);passed++;Console.WriteLine("OK LOTES: "+label); }
        void Reject(Action action,string label) { try { action(); }catch(ArgumentException) { Check(true,label);return; }throw new Exception(label); }
        Reject(()=>store.SaveUser(null,"admin","Admin",UserRole.Administrador,true,"1234567"),"Rechaza contraseña de siete caracteres");
        store.SaveUser(null,"admin","Admin",UserRole.Administrador,true,"12345678");Check(store.Authenticate("admin","12345678") is not null,"Acepta contraseña de ocho caracteres");
        business.SaveSettings(business.Settings with { Lots=true,AutoBackups=false });var p=new Product { Name="Alimento",Code="LOTE",PriceUsd=10,CostUsd=2 };business.Save(p);
        var supplier=new Contact { Name="Proveedor lotes" };ops.SaveContact(supplier,true);ops.OpenCash(1000,0,0);
        ops.Adjust(p.Id,4,"Existencia anterior");ops.ClassifyLot(p.Id,2,"CLASIFICADO",DateOnly.FromDateTime(DateTime.Today.AddDays(2)),"Clasificar");
        Check(OperationsService.Stock(ops.State,p.Id)==4&&OperationsService.Lots(ops.State,p.Id).Single(l=>l.Code=="").Quantity==2,"Clasificación conserva total y disminuye solo sin lote");
        ops.Adjust(p.Id,2,"Vencidos físicos","VENCIDO",DateOnly.FromDateTime(DateTime.Today.AddDays(-1)));
        var purchase=ops.Buy(supplier.Id,"Dos lotes",[(p.Id,2,2),(p.Id,2,3)],new(Currency.USD,PaymentMethod.Efectivo,10),[new("PRONTO",DateOnly.FromDateTime(DateTime.Today.AddDays(3))),new("TARDIO",DateOnly.FromDateTime(DateTime.Today.AddDays(20)))]);
        Check(purchase.Lines.Count==2&&store.GetProducts().Single().CostUsd==3,"Compra admite varios lotes y conserva último costo");
        var sale=ops.Sell([(p.Id,3)],0,[new(Currency.USD,PaymentMethod.Efectivo,30)]);
        var moves=ops.State.Stock.Where(m=>m.DocumentId==sale.Id&&m.Quantity<0).ToList();
        Check(moves.Single(m=>m.LotCode=="CLASIFICADO").Quantity==-2&&moves.Single(m=>m.LotCode=="PRONTO").Quantity==-1,"FEFO consume lotes vigentes por vencimiento");
        Check(OperationsService.Lots(ops.State,p.Id).Single(l=>l.Code=="VENCIDO").Quantity==2,"Venta deja intacto el lote vencido");
        Reject(()=>ops.VoidPurchase(purchase.Id,"Ya consumido"),"No anula compra sustituyendo lote consumido por otro");
        var before=ops.State.Stock.Count;
        Reject(()=>ops.Sell([(p.Id,6)],0,[new(Currency.USD,PaymentMethod.Efectivo,60)]),"No vende vencidos para completar cantidad");Check(ops.State.Stock.Count==before,"Venta rechazada no deja movimientos parciales");
        ops.ReturnSale(sale.Id,[(p.Id,1)],"Parcial uno");ops.ReturnSale(sale.Id,[(p.Id,1)],"Parcial dos");ops.ReturnSale(sale.Id,[(p.Id,1)],"Resto");
        Check(OperationsService.Lots(ops.State,p.Id).Single(l=>l.Code=="CLASIFICADO").Quantity==2&&OperationsService.Lots(ops.State,p.Id).Single(l=>l.Code=="PRONTO").Quantity==2,"Devoluciones parciales reponen lotes originales sin duplicar");
        var second=ops.Sell([(p.Id,3)],0,[new(Currency.USD,PaymentMethod.Efectivo,30)]);ops.VoidSale(second.Id,"Cancelar");
        Check(OperationsService.Stock(ops.State,p.Id)==10&&OperationsService.Lots(ops.State,p.Id).Single(l=>l.Code=="PRONTO").Quantity==2,"Anulación repone cada lote original");
        Reject(()=>ops.Adjust(p.Id,1,"Fecha diferente","PRONTO",DateOnly.FromDateTime(DateTime.Today.AddDays(8))),"Fecha de lote registrado es inmutable");
        Reject(()=>ops.Adjust(p.Id,1,"Falta lote","",DateOnly.FromDateTime(DateTime.Today)),"Vencimiento exige código de lote");
        Reject(()=>business.SaveSettings(business.Settings with { Inventory=false }),"No desactiva inventario con saldos de lotes");
        business.SaveSettings(business.Settings with { Lots=false });Reject(()=>ops.Sell([(p.Id,9)],0,[new(Currency.USD,PaymentMethod.Efectivo,90)]),"Desactivar módulo no permite vender vencidos");business.SaveSettings(business.Settings with { Lots=true });
        ops.Adjust(p.Id,-2,"Descarte","VENCIDO",DateOnly.FromDateTime(DateTime.Today.AddDays(-1)));Check(OperationsService.Lots(ops.State,p.Id).Single(l=>l.Code=="VENCIDO").Quantity==0,"Permite retirar vencidos por ajuste explícito");
        ops.VoidPurchase(purchase.Id,"Revertir recepción");Check(OperationsService.Stock(ops.State,p.Id)==4,"Anulación de compra retira lotes recibidos");
        var cash=ops.CurrentCash!;ops.CloseCash(OperationsService.Expected(ops.State,cash,"USD"),0,0);
        var backup=store.Backup(Path.Combine(root,"backup.db"));ops.Adjust(p.Id,1,"Posterior");store.Restore(backup);store.Authenticate("admin","12345678");
        Check(OperationsService.Stock(ops.State,p.Id)==4&&OperationsService.Lots(ops.State,p.Id).Single(l=>l.Code=="CLASIFICADO").Quantity==2,"Respaldo y restauración conservan lotes y saldos");
        ops.Adjust(p.Id,1,"Fecha de hoy","HOY",DateOnly.FromDateTime(DateTime.Today));ops.OpenCash(0,0,0);
        var todaySale=ops.Sell([(p.Id,1)],0,[new(Currency.USD,PaymentMethod.Efectivo,10)]);
        Check(ops.State.Stock.Single(m=>m.DocumentId==todaySale.Id).LotCode=="HOY","Lote que vence hoy es válido y se consume primero");ops.VoidSale(todaySale.Id,"Prueba hoy");ops.CloseCash(0,0,0);
        var invalid=Path.Combine(root,"invalid-lot.db");File.Copy(backup,invalid);
        using(var c=new SqliteConnection("Data Source="+invalid)) { c.Open();using var cmd=c.CreateCommand();cmd.CommandText="UPDATE stock_moves SET payload=json_set(payload,'$.LotCode','LOTE-FORJADO') WHERE id=(SELECT id FROM stock_moves WHERE quantity_milli<0 LIMIT 1)";cmd.ExecuteNonQuery(); }
        Reject(()=>store.ValidateBackup(invalid),"Restauración rechaza saldos negativos por lote aunque el total sea positivo");
        // Simulate v5 historical partial return, whose stock moves did not include SourceMoveId.
        var legacy=new SqliteStore(Path.Combine(root,"legacy.db"));var lb=new BusinessService(legacy);var lo=new OperationsService(legacy);lb.Save(p);lo.OpenCash(0,0,0);lo.Adjust(p.Id,4,"Inicial");var ls=lo.Sell([(p.Id,3)],0,[new(Currency.USD,PaymentMethod.Efectivo,30)]);lo.ReturnSale(ls.Id,[(p.Id,1)],"Anterior");
        using(var c=new SqliteConnection("Data Source="+legacy.DatabasePath)) { c.Open();using var cmd=c.CreateCommand();cmd.CommandText="UPDATE stock_moves SET payload=json_remove(payload,'$.SourceMoveId'); DROP INDEX ix_stock_lot; PRAGMA user_version=5";cmd.ExecuteNonQuery(); }
        legacy=new SqliteStore(legacy.DatabasePath);lo=new(legacy);lo.ReturnSale(ls.Id,[(p.Id,2)],"Después de migrar");
        Check(OperationsService.Stock(lo.State,p.Id)==4&&Directory.GetFiles(root,"*.before-v6-*.db").Length==1,"Migración v5 conserva devoluciones previas y permite reintegrar resto");
        Console.WriteLine($"LOTES COMPLETOS: {passed} comprobaciones.");
    }
}
