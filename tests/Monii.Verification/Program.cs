using Monii.Application;
using Monii.Domain;
using Monii.Infrastructure;

try
{
if(args.Contains("--live-rates")) { NextChecks.LiveRates(); return; }
if(args.Contains("--live-update-check"))
{
    var result=new UpdateChecker().CheckAsync(UpdateChecker.DefaultFeed,new Version(0,6,0)).GetAwaiter().GetResult();
    Console.WriteLine(result.Status+": "+result.Message);
    Console.WriteLine("Consulta únicamente manifiesto; no descarga ni instala paquetes.");
    Environment.ExitCode=result.Status==UpdateCheckStatus.Error?2:0;
    return;
}

var directory = Path.Combine(Path.GetTempPath(), "MoniiVerification", Guid.NewGuid().ToString("N"));
var path = Path.Combine(directory, "test.db");
var service = new BusinessService(new SqliteStore(path));
var passed = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new Exception("FALLÓ: " + name);
    Console.WriteLine("OK: " + name); passed++;
}
void Reject(Action action, string name)
{
    try { action(); } catch (ArgumentException) { Check(true, name); return; }
    throw new Exception("FALLÓ: " + name);
}

new SqliteStore(path).SaveCategory(new Category(Guid.NewGuid(),"Motor"));
var product = new Product { Name = "Filtro", Code = "R-001", Category = "Motor", CostUsd = 3.27m, PriceUsd = 8.75m, Compatibility = "Vehículo de prueba" };
service.Save(product);
Check(service.Products().Count == 1, "Crear producto en SQLite");
service = new BusinessService(new SqliteStore(path));
Check(service.Products()[0] == product, "Reabrir base de datos conserva producto y decimales exactos");
service.Save(product with { PriceUsd = 9.15m });
Check(service.Products()[0].PriceUsd == 9.15m, "Editar producto persistente");
Check(service.Products("r-001").Count == 1 && service.Products("motor").Count == 1, "Buscar código y categoría");
Reject(() => service.Save(new Product { Name = "Duplicado", Code = "r-001" }), "Rechazar código duplicado sin distinguir mayúsculas");
Reject(() => service.Save(product with { PriceUsd = -1 }), "Rechazar precio negativo");
Reject(() => service.Save(product with { Name = " " }), "Rechazar nombre vacío");
Reject(() => service.Save(product with { PriceUsd = 1.001m }), "Rechazar precisión monetaria excesiva");
service.Save(product with { Active = false });
Check(service.Products().Count == 0 && service.Products(includeInactive: true).Count == 1, "Desactivar conserva producto");
Reject(() => service.Save(new Product { Name = "Duplicado", Code = "R-001" }), "Código inactivo continúa reservado");
Check(service.Audit.Count(a=>a.Action.StartsWith("Producto")) == 3, "Historial conserva creación, edición y desactivación; rechazos no generan cambios");
Check(service.Audit.Any(a => a.Details.Contains("9.15")), "Historial conserva precio anterior");
var settings = service.Settings.WithProfile(BusinessProfile.Parts) with { Name = "Repuestos Monii", ShowBcv = true, BcvRate = 50.123456m, ShowManualVes = true, ManualVesRate = 60, ShowCop = true, CopRate = 4200 };
service.SaveSettings(settings);
service = new BusinessService(new SqliteStore(path));
Check(service.Settings.Name == "Repuestos Monii" && service.Settings.VehicleCompatibility, "Perfil y negocio persisten");
Check(service.Settings.BcvRate == 50.123456m && service.Settings.RatesUpdatedAt is not null, "Tasas y fecha persisten con precisión");
Reject(() => service.SaveSettings(service.Settings with { CopRate = 0 }), "Moneda habilitada requiere tasa positiva");
Reject(() => service.SaveSettings(service.Settings with { Customers = false, Credit = true }), "Créditos requieren clientes");
service.SaveSettings(service.Settings with { Customers = false, Credit = false, Inventory = false, ShowBcv = false });
Check(service.Products(includeInactive: true).Count == 1 && service.Settings.BcvRate == 50.123456m, "Desactivar módulos y moneda conserva sus datos");
Check(service.Settings.WithProfile(BusinessProfile.Groceries).Lots && !service.Settings.WithProfile(BusinessProfile.Groceries).VehicleCompatibility, "Perfil víveres ajusta preferencias");
Check(BusinessService.ConvertPrice(1.25m, 50.123456m) == 62.65m, "Conversión y redondeo decimal");
service.Save(product with { Active = true });
Check(service.Products().Count == 1, "Reactivar producto");
Console.WriteLine($"VERIFICACIÓN COMPLETA: {passed} comprobaciones. Base de prueba: {path}");
OperationChecks.Run();
NextChecks.Run();
DesignChecks.Run();
FullFlowChecks.Run();
ImportChecks.Run();
UpdateCheckTests.Run();
}
catch(Exception error)
{
    Console.Error.WriteLine("FALLÓ LA VERIFICACIÓN: " + error);
    Environment.ExitCode=1;
}
