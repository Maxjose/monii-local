using Microsoft.Data.Sqlite;
using System.Text.Json;
using Monii.Application;
using Monii.Domain;
using Monii.Infrastructure;

internal static class DesignChecks
{
    public static void Run()
    {
        var root=Path.Combine(Path.GetTempPath(),"Monii04",Guid.NewGuid().ToString("N")); var store=new SqliteStore(Path.Combine(root,"monii.db")); var business=new BusinessService(store); var ops=new OperationsService(store); var count=0;
        void Check(bool condition,string label) { if(!condition) throw new Exception("FALLÓ 0.4: "+label); count++; Console.WriteLine("OK 0.4: "+label); }
        void Reject(Action action,string label) { try { action(); } catch(Exception e) when(e is ArgumentException or UnauthorizedAccessException) { Check(true,label); return; } throw new Exception("FALLÓ 0.4: "+label); }
        var category=new Category(Guid.NewGuid(),"  Víveres  "); store.SaveCategory(category); category=store.GetCategories().Single(); Check(category.Name=="Víveres","Crear categoría normaliza espacios");
        Reject(()=>store.SaveCategory(new Category(Guid.NewGuid(),"víveres")),"Rechazar categoría duplicada sin distinguir mayúsculas");
        Reject(()=>store.SaveCategory(new Category(Guid.NewGuid()," ")),"Nombre vacío rechazado"); Reject(()=>store.SaveCategory(new Category(Guid.Empty,"Inválida")),"Identificador vacío rechazado");
        var product=new Product { Name="Alimento",Code="AL-1",Category=category.Name,PriceUsd=2,CostUsd=1 }; business.Save(product); ops.Adjust(product.Id,5,"Inicial");
        business.SaveSettings(business.Settings with { Cash=false }); var sale=ops.Sell([(product.Id,1)],0,[new(Currency.USD,PaymentMethod.Transferencia,2)]);
        store.SaveCategory(category with { Name="Alimentos" }); product=business.Products().Single();
        Check(product.Category=="Alimentos"&&store.StockBalance(product.Id)==4&&ops.State.Sales.Single().Id==sale.Id&&ops.State.Sales.Single().Total==sale.Total,"Renombrar mantiene producto, stock e historial de venta");
        category=store.GetCategories().Single(); store.SaveCategory(category with { Active=false }); business.Save(product with { PriceUsd=3 });
        Check(!store.GetCategories().Single().Active&&business.Products().Single().Category=="Alimentos","Desactivar conserva asignación y permite editar producto anterior");
        Reject(()=>business.Save(product with { Id=Guid.NewGuid(),Code="AL-2" }),"No asignar categoría inactiva a un producto nuevo");
        Reject(()=>business.Save(product with { Category="Inventada" }),"Servicio rechaza categoría escrita fuera del listado");
        store.SaveCategory(category); business.Save(product with { Id=Guid.NewGuid(),Code="AL-2" }); Check(business.Products().Count==2,"Reactivar permite nuevas asignaciones");
        business.SaveSettings(business.Settings with { DarkMode=true,AccentColor="#2563EB" });
        Check(new SqliteStore(store.DatabasePath).GetSettings().DarkMode&&new SqliteStore(store.DatabasePath).GetSettings().AccentColor=="#2563EB","Apariencia persiste al reabrir");
        Reject(()=>business.SaveSettings(business.Settings with { AccentColor="rojo" }),"Color no hexadecimal se rechaza");
        var backup=store.Backup(Path.Combine(root,"backup.db")); store.SaveCategory(category with { Name="Otra" }); store.Restore(backup);
        Check(store.GetCategories().Single().Name=="Alimentos"&&store.GetSettings().DarkMode,"Restaurar conserva categorías y apariencia");
        // Emulate real v3 free-text categories, without using current category validation.
        using(var connection=new SqliteConnection("Data Source="+store.DatabasePath))
        {
            connection.Open(); using var command=connection.CreateCommand(); command.CommandText="DROP TABLE network_meta; DROP TABLE request_receipts; DROP TABLE categories; PRAGMA user_version=3"; command.ExecuteNonQuery();
            foreach(var p in store.GetProducts())
            {
                command.Parameters.Clear(); command.CommandText="UPDATE products SET payload=$payload WHERE id=$id"; command.Parameters.AddWithValue("$id",p.Id.ToString()); command.Parameters.AddWithValue("$payload",JsonSerializer.Serialize(p with { Category=p.Code=="AL-1"?" Alimentos ":"alimentos" })); command.ExecuteNonQuery();
            }
        }
        store=new SqliteStore(store.DatabasePath); business=new(store); ops=new(store);
        Check(store.GetCategories().Count==1&&business.Products().All(p=>p.Category=="Alimentos"),"Migración v3 importa y unifica categorías de texto existentes");
        Check(Directory.GetFiles(root,"*.before-v4-*.db").Length==1&&ops.State.Sales.Count==1&&store.StockBalance(product.Id)==4,"Migración crea respaldo y conserva operaciones");
        store.ValidateBackup(store.Backup(Path.Combine(root,"v4.db"))); Check(true,"Respaldo de esquema 4 validado");
        const string password="Monii-Categorias-Seguro"; store.SaveUser(null,"admin","Admin",UserRole.Administrador,true,password); store.Authenticate("admin",password); store.SaveUser(null,"vendedor","Vendedor",UserRole.Vendedor,true,password); store.Authenticate("vendedor",password);
        Reject(()=>store.SaveCategory(new Category(Guid.NewGuid(),"Sin permiso")),"Vendedor no gestiona categorías");
        Console.WriteLine($"VERIFICACIÓN 0.4 COMPLETA: {count} comprobaciones. Datos: {root}");
    }
}
