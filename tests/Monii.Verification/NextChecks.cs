using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Monii.Application;
using Monii.Domain;
using Monii.Infrastructure;

internal static class NextChecks
{
    public static void LiveRates()
    {
        var store=new SqliteStore(Path.Combine(Path.GetTempPath(),"MoniiLiveRates",Guid.NewGuid().ToString("N"),"monii.db"));
        new BusinessService(store).SaveSettings(new BusinessSettings { ShowBcv=true,ShowCop=true,AutoBcv=true,AutoCop=true });
        var result=new ExchangeRates(store).RefreshAsync().GetAwaiter().GetResult(); var settings=store.GetSettings();
        Console.WriteLine(result.Message); Console.WriteLine($"BCV {settings.BcvRate} · {settings.BcvEffectiveDate} · {settings.BcvSource}"); Console.WriteLine($"COP {settings.CopRate} · {settings.CopEffectiveDate} · {settings.CopSource}");
        if(!result.BcvUpdated||!result.CopUpdated)
        {
            Console.WriteLine("PENDIENTE: una o ambas fuentes no entregaron una tasa válida para hoy. Revisa el mensaje anterior; no implica un fallo de los flujos financieros.");
            Environment.ExitCode=2;
        }
    }
    public static void Run()
    {
        var root=Path.Combine(Path.GetTempPath(),"Monii03",Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var store=new SqliteStore(Path.Combine(root,"monii.db")); var business=new BusinessService(store); var ops=new OperationsService(store); var count=0;
        void Check(bool okay,string label) { if(!okay) throw new Exception("FALLÓ 0.3: "+label); Console.WriteLine("OK 0.3: "+label); count++; }
        void Reject(Action action,string label) { try { action(); } catch(Exception e) when(e is ArgumentException or InvalidDataException or UnauthorizedAccessException) { Check(true,label); return; } throw new Exception("FALLÓ 0.3: "+label); }
        var p=new Product { Name="Retorno",Code="R",CostUsd=2,PriceUsd=10 }; business.Save(p); ops.Adjust(p.Id,20,"Inicial"); ops.OpenCash(100,0,0);
        var c=new Contact { Name="Cliente",CreditLimit=100 }; ops.SaveContact(c,false);
        var sale=ops.Sell([(p.Id,3)],3,[new(Currency.USD,PaymentMethod.Efectivo,7)],c.Id,DateOnly.FromDateTime(DateTime.Today.AddDays(30)));
        var abono=ops.PayDebt(sale.Id,new(Currency.USD,PaymentMethod.Efectivo,5));
        var first=ops.ReturnSale(sale.Id,[(p.Id,1)],"Devolución parcial");
        Check(first.Total==9&&first.DebtReduction==9&&first.RefundUsd==0&&OperationsService.Debt(ops.State,sale)==6,"Devolución descontada reduce deuda primero");
        Check(store.StockBalance(p.Id)==18,"Devolución restituye inventario exacto");
        var second=ops.ReturnSale(sale.Id,[(p.Id,1)],"Segunda devolución");
        Check(second.DebtReduction==6&&second.RefundUsd==3&&OperationsService.Debt(ops.State,sale)==0,"Devolución liquida deuda y reintegra excedente");
        var last=ops.ReturnSale(sale.Id,[(p.Id,1)],"Última devolución");
        Check(last.RefundUsd==9&&OperationsService.NetTotal(ops.State,sale)==0&&OperationsService.NetMargin(ops.State,sale)==0,"Todas las devoluciones suman total y costo descontados");
        Check(OperationsService.Expected(ops.State,OperationsService.OpenSession(ops.State)!,"USD")==100,"Cobros, abonos y devoluciones concilian caja");
        Reject(()=>ops.ReturnSale(sale.Id,[(p.Id,1)],"Exceso"),"No devolver más de lo vendido");
        Reject(()=>ops.VoidSale(sale.Id,"Conflicto"),"Venta con devoluciones no se anula de nuevo");
        Reject(()=>ops.VoidDebtPayment(abono.Id,"Conflicto"),"Abono previo a devolución no se anula");
        var cheap=p with { Id=Guid.NewGuid(),Code="CENT",Name="Centavo",PriceUsd=.01m,CostUsd=.01m }; business.Save(cheap); ops.Adjust(cheap.Id,10,"Inicial");
        var fractional=cheap with { Id=Guid.NewGuid(),Code="PESO",Unit="Kilogramo",PriceUsd=.03m,CostUsd=.01m }; business.Save(fractional); ops.Adjust(fractional.Id,1,"Inicial");
        var tiny=ops.Sell([(cheap.Id,3),(fractional.Id,1)],.05m,[new(Currency.USD,PaymentMethod.Efectivo,.01m)]);
        foreach(var line in tiny.Lines) ops.ReturnSale(tiny.Id,[(line.ProductId,line.Quantity)],"Centavos");
        Check(OperationsService.NetTotal(ops.State,tiny)==0&&OperationsService.NetMargin(ops.State,tiny)==0,"Prorrateo extremo conserva centavos sin cantidades negativas");
        var weighted=ops.Sell([(fractional.Id,1)],0,[new(Currency.USD,PaymentMethod.Efectivo,.03m)]);
        ops.ReturnSale(weighted.Id,[(fractional.Id,.333m)],"Peso 1"); ops.ReturnSale(weighted.Id,[(fractional.Id,.333m)],"Peso 2"); ops.ReturnSale(weighted.Id,[(fractional.Id,.334m)],"Peso 3");
        Check(OperationsService.NetMargin(ops.State,weighted)==0&&store.StockBalance(fractional.Id)==1,"Devoluciones fraccionarias conservan costo al último centavo");
        var transfer=ops.Sell([(p.Id,1)],0,[new(Currency.USD,PaymentMethod.Transferencia,10)]);
        ops.CashMovement(new(Currency.USD,PaymentMethod.Efectivo,100),true,"Retiro"); var before=ops.State.Returns.Count;
        Reject(()=>ops.ReturnSale(transfer.Id,[(p.Id,1)],"Sin efectivo"),"Sin efectivo se revierte devolución completa");
        Check(ops.State.Returns.Count==before&&store.StockBalance(p.Id)==19,"Fallo de reintegro conserva documentos y stock");
        ops.ReturnSale(transfer.Id,[(p.Id,1)],"Reintegro transferencia",PaymentMethod.Transferencia);
        business.Save(p with { Active=false });
        var backup=store.Backup(Path.Combine(root,"respaldo.db")); store.ValidateBackup(backup);
        Check(new SqliteStore(backup).ReadOperations().Returns.Count==ops.State.Returns.Count,"Respaldo conserva devoluciones");
        LegacyFixture.Downgrade(store,2); store=new SqliteStore(store.DatabasePath); ops=new(store); business=new(store);
        Check(ops.State.Sales.Count==4&&store.GetProducts().Count==3&&store.StockBalance(p.Id)==20,"Migración v2 a v3 conserva historial real y decimales");
        Check(Directory.GetFiles(root,"*.before-v3-*.db").Length==1,"Migración v3 crea respaldo previo");
        Check(store.SalesPage(0,"",2).Count==2&&store.SalesPage(1,"",2).Count==2&&store.SalesPage(2,"",2).Count==0&&store.SalesPage(0,"%",2).Count==0,"Paginación SQL y búsqueda literal");
        const string password="Monii-test-03-seguro";
        Reject(()=>store.SaveUser(null,"admin","Admin",UserRole.Administrador,true,"123"),"Contraseña insuficiente rechazada");
        store.SaveUser(null,"admin","Admin",UserRole.Administrador,true,password); store.Authenticate("ADMIN",password); var admin=store.CurrentUser!;
        store.SaveUser(null,"cajero","Caja",UserRole.Cajero,true,password); store.SaveUser(null,"vendedor","Ventas",UserRole.Vendedor,true,password);
        Reject(()=>store.SaveUser(admin.Id,"admin","Admin",UserRole.Cajero,true,null),"No desactivar último administrador");
        store.Authenticate("vendedor",password);
        Reject(()=>business.Save(p),"Vendedor no modifica productos"); Reject(()=>ops.Adjust(p.Id,1,"Intento"),"Vendedor no ajusta inventario");
        Reject(()=>ops.SaveContact(c with { CreditLimit=200 },false),"Vendedor no amplía límites de crédito");
        business=new(store); ops=new(store);
        Reject(()=>ops.Sell([(cheap.Id,1)],0,[],c.Id,DateOnly.FromDateTime(DateTime.Today.AddDays(1))),"Vendedor no crea crédito aunque llame el servicio directamente");
        var sellerSale=ops.Sell([(cheap.Id,1)],0,[new(Currency.USD,PaymentMethod.Transferencia,.01m)]);
        Check(sellerSale.SellerName=="Ventas"&&store.GetAudit().First().ActorName=="Ventas","Venta y auditoría atribuyen usuario real");
        Reject(()=>ops.VoidSale(sellerSale.Id,"Intento"),"Vendedor no anula ventas"); Reject(()=>store.Backup(Path.Combine(root,"sinpermiso.db")),"Vendedor no exporta base");
        store.AutomaticBackup(); Check(Directory.GetFiles(Path.Combine(root,"backups"),"*.db").Length==1,"Respaldo automático funciona con vendedor sin darle permiso manual");
        store.Authenticate("admin",password); var seller=store.Users().Single(u=>u.Username=="vendedor");
        store.SaveUser(seller.Id,seller.Username,seller.Name,seller.Role,false,null); Reject(()=>store.Authenticate("vendedor",password),"Cuenta inactiva no inicia sesión");
        for(var i=0;i<5;i++) Reject(()=>store.Authenticate("cajero","incorrecta"),"Intento fallido "+(i+1));
        Reject(()=>store.Authenticate("cajero",password),"Cinco errores bloquean temporalmente incluso con contraseña correcta");
        store.Authenticate("admin",password); var secured=store.Backup(Path.Combine(root,"usuarios.db")); store.Restore(secured);
        Check(store.CurrentUser is null&&!store.Can(Permission.Settings),"Restaurar cierra sesión y exige autenticarse otra vez"); store.Authenticate("admin",password);
        Check(store.Users().Count==3,"Respaldo restaura usuarios sin exponer contraseñas");
        var shadow=Path.Combine(root,"indice-corrupto.db"); File.Copy(secured,shadow);
        using(var connection=new SqliteConnection("Data Source="+shadow)) { connection.Open(); using var command=connection.CreateCommand(); command.CommandText="UPDATE stock_moves SET quantity_milli=quantity_milli+1 WHERE rowid=(SELECT MIN(rowid) FROM stock_moves)"; command.ExecuteNonQuery(); }
        Reject(()=>store.ValidateBackup(shadow),"Respaldo con índice de stock distinto al documento se rechaza");
        var badReturn=Path.Combine(root,"retorno-corrupto.db"); File.Copy(secured,badReturn);
        using(var connection=new SqliteConnection("Data Source="+badReturn)) { connection.Open(); using var command=connection.CreateCommand(); command.CommandText="UPDATE sale_returns SET payload=json_set(payload,'$.Total',999) WHERE rowid=(SELECT MIN(rowid) FROM sale_returns)"; command.ExecuteNonQuery(); }
        Reject(()=>store.Restore(badReturn),"Restauración rechaza devolución con saldo imposible");
        Check(store.CurrentUser?.Username=="admin"&&store.GetSettings().Name==business.Settings.Name,"Restauración rechazada conserva sesión y datos activos");
        var today=DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow,TimeZoneInfo.FindSystemTimeZoneById("America/Caracas")).DateTime);
        var html=$"<div id='dolar'><strong>860,17530001</strong></div><span class='date-display-single' content='{today:yyyy-MM-dd}T00:00:00-04:00'>Fecha</span>";
        Check(ExchangeRates.ParseBcv(html,today).Value==860.17530001m,"BCV admite ocho decimales sin redondear tasa de origen");
        var cop=$"[{{\"valor\":\"3312.84\",\"vigenciadesde\":\"{today:yyyy-MM-dd}T00:00:00\",\"vigenciahasta\":\"{today:yyyy-MM-dd}T00:00:00\"}}]";
        Check(ExchangeRates.ParseCop(cop,today).Value==3312.84m,"TRM oficial parsea valor y vigencia");
        Reject(()=>ExchangeRates.ParseBcv(html,today.AddDays(8)),"BCV demasiado antiguo conserva tasa previa"); Reject(()=>ExchangeRates.ParseBcv(html,today.AddDays(-1)),"BCV futuro se rechaza");
        Reject(()=>ExchangeRates.ParseBcv(html.Replace("860,17530001","860.17530001"),today),"Cambio de formato BCV se rechaza sin multiplicar la tasa");
        Reject(()=>ExchangeRates.ParseCop(cop,today.AddDays(1)),"TRM vencida se rechaza");
        business.SaveSettings(business.Settings with { ShowBcv=true,BcvRate=50,AutoBcv=true,ShowCop=true,CopRate=4000,AutoCop=true,ShowManualVes=true,ManualVesRate=60 });
        using var client=new HttpClient(new RateHandler(html,cop)); var refresh=new ExchangeRates(store,client).RefreshAsync().GetAwaiter().GetResult();
        Check(refresh.BcvUpdated&&refresh.CopUpdated&&store.GetSettings().ManualVesRate==60,"Consulta automática actualiza ambas fuentes y conserva tasa manual");
        using var offline=new HttpClient(new RateHandler("","",true)); refresh=new ExchangeRates(store,offline).RefreshAsync().GetAwaiter().GetResult();
        Check(!refresh.BcvUpdated&&!refresh.CopUpdated&&store.GetSettings().BcvRate==860.17530001m,"Fallo de red conserva última tasa y fecha");
        using var partial=new HttpClient(new RateHandler(html,"[]")); refresh=new ExchangeRates(store,partial).RefreshAsync().GetAwaiter().GetResult();
        Check(refresh.BcvUpdated&&!refresh.CopUpdated&&store.GetSettings().CopRate==3312.84m,"Fallo de COP no impide actualización BCV");
        using var rsa=RSA.Create(2048); var publicKey=rsa.ExportSubjectPublicKeyInfoPem();
        string Sign(ReleaseManifest manifest) { var bytes=JsonSerializer.SerializeToUtf8Bytes(manifest); return JsonSerializer.Serialize(new SignedRelease(Convert.ToBase64String(bytes),Convert.ToBase64String(rsa.SignData(bytes,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1)))); }
        var folder=Path.Combine(root,"package"); Directory.CreateDirectory(folder); File.WriteAllText(Path.Combine(folder,"Monii.exe"),"archivo de prueba, no se ejecuta");
        var zip=Path.Combine(root,"package.zip"); ZipFile.CreateFromDirectory(folder,zip);
        ReleaseManifest Manifest(string file) { using var stream=File.OpenRead(file); return new("0.4.0",Convert.ToHexString(SHA256.HashData(stream)),stream.Length,"","Prueba"); }
        var manifest=Manifest(zip); var envelope=Sign(manifest);
        Check(UpdatePackages.VerifyManifest(envelope,publicKey)==manifest,"Firma de manifiesto auténtica verificada");
        var installed=UpdatePackages.Install(zip,envelope,Path.Combine(root,"versions"),publicKey);
        Check(File.Exists(installed)&&File.Exists(Path.Combine(folder,"Monii.exe")),"Actualización instala aparte y conserva versión anterior");
        using var other=RSA.Create(2048); Reject(()=>UpdatePackages.VerifyManifest(envelope,other.ExportSubjectPublicKeyInfoPem()),"Firma de otro desarrollador rechazada");
        File.AppendAllText(zip,"alterado"); Reject(()=>UpdatePackages.VerifyPackage(zip,manifest),"Paquete modificado se rechaza");
        var evil=Path.Combine(root,"evil.zip"); using(var archive=ZipFile.Open(evil,ZipArchiveMode.Create)) { var entry=archive.CreateEntry("../escape.txt"); using var writer=new StreamWriter(entry.Open()); writer.Write("malicioso"); }
        Reject(()=>UpdatePackages.Install(evil,Sign(Manifest(evil)),Path.Combine(root,"evilversions"),publicKey),"Incluso paquete firmado rechaza rutas fuera del destino");
        Check(!File.Exists(Path.Combine(root,"escape.txt"))&&Directory.GetDirectories(Path.Combine(root,"evilversions")).Length==0,"Extracción fallida limpia carpeta temporal");
        Console.WriteLine($"VERIFICACIÓN 0.3 COMPLETA: {count} comprobaciones. Datos aislados: {root}");
    }
    private sealed class RateHandler(string html,string cop,bool fail=false):HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            if(fail) throw new HttpRequestException("Sin conexión de prueba");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content=new StringContent(request.RequestUri!.Host=="www.bcv.org.ve"?html:cop,Encoding.UTF8) });
        }
    }
}
