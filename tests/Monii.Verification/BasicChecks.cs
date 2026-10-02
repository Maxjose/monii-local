using Monii.Application;
using Monii.Domain;
using Monii.Infrastructure;
internal static class BasicChecks
{
    public static void Run()
    {
        var root=Path.Combine(Path.GetTempPath(),"MoniiBasic",Guid.NewGuid().ToString("N"));var store=new SqliteStore(Path.Combine(root,"monii.db"));var service=new BusinessService(store);var ops=new OperationsService(store);var passed=0;
        void Check(bool value,string label) { if(!value)throw new Exception(label);passed++;Console.WriteLine("OK BÁSICO: "+label); }
        void Reject(Action action,string label) { try { action(); }catch(ArgumentException) { Check(true,label);return; }throw new Exception(label); }
        store.SaveUser(null,"admin","Administrador",UserRole.Administrador,true,"12345678");store.Authenticate("admin","12345678");
        var category=new Category(Guid.NewGuid(),"Básico");store.SaveCategory(category);var product=new Product { Name="Producto",Code="BAS-1",Category=category.Name,CostUsd=2.75m,PriceUsd=10,Brand="Marca previa",Unit="Kilogramo",MinimumStock=3 };service.Save(product);ops.Adjust(product.Id,5,"Existencia previa");ops.OpenCash(0,0,0);
        Reject(()=>service.SaveSettings(service.Settings.WithProfile(BusinessProfile.Basic)),"Rechaza activar básico con una caja abierta");
        var customer=new Contact { Name="Cliente previo",CreditLimit=100 };ops.SaveContact(customer,false);var sale=ops.Sell([(product.Id,1)],0,[],customer.Id,DateOnly.FromDateTime(DateTime.Today.AddDays(1)));ops.CloseCash(0,0,0);
        Reject(()=>service.SaveSettings(service.Settings.WithProfile(BusinessProfile.Basic)),"Rechaza ocultar créditos pendientes");ops.OpenCash(0,0,0);ops.PayDebt(sale.Id,new(Currency.USD,PaymentMethod.Efectivo,10));ops.CloseCash(10,0,0);
        var advanced=service.Settings;service.SaveSettings(advanced.WithProfile(BusinessProfile.Basic));
        Check(service.Settings.Profile==BusinessProfile.Basic&&service.Settings.Inventory==advanced.Inventory&&service.Settings.Cash==advanced.Cash,"Perfil básico conserva preferencias de módulos anteriores");
        Reject(()=>ops.Sell([(product.Id,1)],0,[new(Currency.USD,PaymentMethod.Efectivo,10)]),"Básico bloquea ventas en la capa de aplicación");Reject(()=>ops.OpenCash(0,0,0),"Básico bloquea apertura de caja");Reject(()=>ops.Adjust(product.Id,1,"Intento"),"Básico bloquea movimientos de inventario");Reject(()=>ops.SaveContact(new Contact { Name="Otro" },true),"Básico bloquea gestión de proveedores");
        store.SaveCategory(category with { Name="Precios básicos" });product=service.Products().Single();service.Save(product with { PriceUsd=12 });
        var reopened=new SqliteStore(store.DatabasePath);reopened.Authenticate("admin","12345678");var saved=reopened.GetProducts().Single();
        Check(reopened.GetSettings().Profile==BusinessProfile.Basic&&saved.PriceUsd==12&&saved.Category=="Precios básicos","Reabrir conserva perfil, categoría y precios editados");
        Check(saved.CostUsd==2.75m&&saved.Brand=="Marca previa"&&saved.Unit=="Kilogramo"&&OperationsService.Stock(reopened.ReadOperations(),product.Id)==4,"Perfil básico conserva datos avanzados y saldo anterior");
        Check(reopened.ReadOperations().Sales.Count==1&&reopened.ReadOperations().Abonos.Count==1,"Operaciones rechazadas no alteran histórico financiero");
        var backup=store.Backup(Path.Combine(root,"basic-backup.db"));service.Save(saved with { PriceUsd=13 });store.Restore(backup);store.Authenticate("admin","12345678");
        Check(service.Settings.Profile==BusinessProfile.Basic&&service.Products().Single().PriceUsd==12,"Respaldo y restauración conservan perfil básico");
        service.SaveSettings(service.Settings with { Profile=BusinessProfile.General });ops.OpenCash(0,0,0);Check(ops.CurrentCash is not null&&ops.State.Sales.Count==1,"Volver a perfil completo habilita funciones con histórico conservado");ops.CloseCash(0,0,0);
        Check((int)BusinessProfile.General==0&&(int)BusinessProfile.Groceries==1&&(int)BusinessProfile.Parts==2&&(int)BusinessProfile.Basic==3,"Perfil nuevo conserva identificadores anteriores");
        Console.WriteLine($"BÁSICO COMPLETO: {passed} comprobaciones.");
    }
}
