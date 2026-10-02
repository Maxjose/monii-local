using Monii.Application;
using Monii.Domain;
using Monii.Infrastructure;
using Monii.Server;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using System.Security.Cryptography;
using System.Net.Http.Json;
using System.Net.Http.Headers;

try { await Verify(); } catch(Exception error) { Console.Error.WriteLine(error);Environment.ExitCode=1; }
static async Task Verify()
{
    var directory=Path.GetFullPath(Path.Combine("artifacts","network-verification-"+Guid.NewGuid().ToString("N")));Directory.CreateDirectory(directory);
    var data=Path.Combine(directory,"server");Directory.CreateDirectory(data);
    var local=new SqliteStore(Path.Combine(directory,"source.db"));var pass=Convert.ToHexString(RandomNumberGenerator.GetBytes(20));
    local.SaveUser(null,"admin","Administrador",UserRole.Administrador,true,pass);local.Authenticate("admin",pass);
    local.SaveUser(null,"cajero","Cajero",UserRole.Cajero,true,pass);local.SaveUser(null,"vendedor","Vendedor",UserRole.Vendedor,true,pass);
    var product=new Product { Code="00001",Name="Producto de prueba",PriceUsd=10,CostUsd=3 };
    new BusinessService(local).Save(product);new OperationsService(local).Adjust(product.Id,20,"Inicio");
    var source=local.Backup(Path.Combine(directory,"initial.db"));using(var legacy=new Microsoft.Data.Sqlite.SqliteConnection(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource=source,Pooling=false }.ToString()))
    {
        legacy.Open();using var command=legacy.CreateCommand();command.CommandText="DROP TABLE network_meta;DROP TABLE request_receipts;DROP INDEX ix_one_open_cash;CREATE UNIQUE INDEX ix_one_open_cash ON cash_sessions((1)) WHERE closed IS NULL;PRAGMA user_version=4";command.ExecuteNonQuery();
    }
    ServerHost.Prepare(data,source);File.WriteAllText(Path.Combine(data,"verification.allow"),"isolated test");
    File.WriteAllText(Path.Combine(data,"server.json"),JsonSerializer.Serialize(new ServerConfiguration(0)));
    var app=ServerHost.Build(data);await app.StartAsync();
    var addresses=app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses;
    var port=new Uri(addresses.Single()).Port;File.WriteAllText(Path.Combine(data,"server.json"),JsonSerializer.Serialize(new ServerConfiguration(port)));var config=ConnectionSettings.Load(directory) with { Address=$"https://localhost:{port}",Mode="Cliente",TerminalName="Caja A" };
    using var a=new RemoteStore(config,Path.Combine(directory,"a"));using var b=new RemoteStore(config with { TerminalId=Guid.NewGuid(),TerminalName="Caja B" },Path.Combine(directory,"b"));
    var passed=0;void Check(bool ok,string name) { if(!ok)throw new Exception("FALLO: "+name);Console.WriteLine("OK RED: "+name);passed++; }
    void Reject(Action action,string name) { try { action(); } catch(Exception e) when(e is ArgumentException or UnauthorizedAccessException or IOException) { Check(true,name);return; }throw new Exception("FALLO: "+name); }
    try
    {
        Check(Directory.GetFiles(data,"*.before-v5-*.db").Length==1,"Migración v4 a v5 conserva copia previa del servidor");
        Reject(()=>a.Authenticate("admin","incorrecta"),"Contraseña incorrecta rechazada");
        a.Authenticate("admin",pass);b.Authenticate("cajero",pass);var oa=new OperationsService(a);var ob=new OperationsService(b);
        Check(a.GetProducts().Single().Code=="00001","Datos existentes importados con código intacto");
        Check(a.Can(Permission.Settings)&&!b.Can(Permission.Settings),"Permisos distintos por sesión");
        Reject(()=>b.SaveSettings(b.GetSettings() with { Name="intruso" }),"Servidor impide ajustes a cajero");
        Reject(()=>b.SaveProduct(product with { PriceUsd=1 }),"Servidor impide alterar catálogo a cajero");
        Reject(()=>b.Backup(Path.Combine(directory,"forbidden.db")),"Servidor protege respaldos");
        Check(b.GetProducts().Single().CostUsd==0,"Costo oculto en respuestas del cajero");
        oa.OpenCash(100,0,0);ob.OpenCash(50,0,0);
        Check(oa.CurrentCash!.Id!=ob.CurrentCash!.Id&&a.ReadOperations().Sessions.Count==2,"Dos cajas independientes abiertas simultáneamente");
        oa.Sell([(product.Id,1)],0,[new(Currency.USD,PaymentMethod.Efectivo,10)]);
        ob.Sell([(product.Id,2)],0,[new(Currency.USD,PaymentMethod.Efectivo,20)]);
        Check(OperationsService.Expected(oa.State,oa.CurrentCash!,"USD")==110&&OperationsService.Expected(ob.State,ob.CurrentCash!,"USD")==70,"Cada venta afecta su propio efectivo");
        Check(a.StockBalances()[product.Id]==17&&b.StockBalances()[product.Id]==17,"Inventario central compartido");
        Check(ob.State.Sessions.Count==1&&ob.State.Cash.All(e=>e.SessionId==ob.CurrentCash!.Id),"Cajero solo recibe movimientos de su caja");
        Reject(()=>new BusinessService(a).SaveSettings(a.GetSettings() with { IndependentCash=false }),"Cambio de modalidad bloqueado con cajas abiertas");
        var customer=new Contact { Name="Cliente red",CreditLimit=100 };oa.SaveContact(customer,false);
        var credit=ob.Sell([(product.Id,1)],0,[],customer.Id,DateOnly.FromDateTime(DateTime.Today.AddDays(5)));
        Check(credit.InitialDebt==10,"Venta a crédito se calcula en servidor");
        var payment=ob.PayDebt(credit.Id,new(Currency.USD,PaymentMethod.Efectivo,10));
        Check(OperationsService.Debt(oa.State,credit)==0,"Abono de otra caja actualiza saldo central");
        Reject(()=>ob.PayDebt(credit.Id,new(Currency.USD,PaymentMethod.Efectivo,1)),"Sobreabono bloqueado en servidor");
        oa.VoidDebtPayment(payment.Id,"Corrección desde principal");oa.VoidSale(credit.Id,"Venta anulada");
        Check(oa.State.Sales.Single(s=>s.Id==credit.Id).Voided&&a.StockBalances()[product.Id]==17,"Anulaciones conservan saldos y recuperan inventario");
        var provider=new Contact { Name="Proveedor red" };oa.SaveContact(provider,true);
        var purchase=oa.Buy(provider.Id,"FAC-RED",[(product.Id,3,2m)],new(Currency.USD,PaymentMethod.Efectivo,6));
        Check(a.StockBalances()[product.Id]==20&&a.GetProducts().Single().CostUsd==2,"Compra remota actualiza stock y costo");
        oa.VoidPurchase(purchase.Id,"Corrección de compra");Check(a.StockBalances()[product.Id]==17,"Anulación de compra remota repone saldo");
        var sold=oa.Sell([(product.Id,2)],0,[new(Currency.USD,PaymentMethod.Efectivo,20)]);
        var returned=oa.ReturnSale(sold.Id,[(product.Id,1)],"Devolución parcial");
        Check(returned.RefundUsd==10&&OperationsService.NetTotal(oa.State,sold)==10,"Devolución parcial remota calcula reintegro");
        var ca=oa.CurrentCash!;var cb=ob.CurrentCash!;
        oa.CloseCash(OperationsService.Expected(oa.State,ca,"USD"),0,0);ob.CloseCash(OperationsService.Expected(ob.State,cb,"USD"),0,0);
        new BusinessService(a).SaveSettings(a.GetSettings() with { IndependentCash=false });oa.OpenCash(100,0,0);
        Check(oa.CurrentCash!.Id==ob.CurrentCash!.Id,"Caja compartida visible desde ambos terminales");
        ob.Sell([(product.Id,1)],0,[new(Currency.USD,PaymentMethod.Efectivo,10)]);
        Check(OperationsService.Expected(oa.State,oa.CurrentCash!,"USD")==110,"Venta en caja compartida consolida efectivo");
        Reject(()=>ob.OpenCash(0,0,0),"No abre dos veces caja compartida");
        // Replay through HTTPS, including conflicting payload and ownership.
        using var raw=new HttpClient(new HttpClientHandler { ServerCertificateCustomValidationCallback=(_,cert,_,_)=>cert is not null&&Convert.ToHexString(SHA256.HashData(cert.RawData))==config.Fingerprint,UseProxy=false }) { BaseAddress=new Uri(config.Address) };
        var login=(await (await raw.PostAsJsonAsync("api/login",new LoginRequest("admin",pass,config.TerminalId,"Caja A"),NetworkJson.Options)).Content.ReadFromJsonAsync<LoginReply>(NetworkJson.Options))!;
        raw.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",login.Token);
        var command=NetworkJson.Command("Sell",new { items=new List<(Guid ProductId,decimal Quantity)> { (product.Id,1) },discount=0m,payments=new[]{new Payment(Currency.USD,PaymentMethod.Efectivo,10)},customerId=(Guid?)null,due=(DateOnly?)null });
        var request=new NetworkRequest(Guid.NewGuid(),command,login.Epoch);
        async Task<Sale> Send(NetworkRequest item) { var reply=await raw.PostAsJsonAsync("api/operation",item,NetworkJson.Options);reply.EnsureSuccessStatusCode();return (await reply.Content.ReadFromJsonAsync<Sale>(NetworkJson.Options))!; }
        var first=await Send(request);var replay=await Send(request);
        Check(first.Id==replay.Id&&oa.State.Sales.Count(s=>s.Id==first.Id)==1,"Reintento devuelve venta original sin duplicarla");
        var conflict=request with { Command=NetworkJson.Command("CloseCash",new { usd=120m,ves=0m,cop=0m }) };
        Check((await raw.PostAsJsonAsync("api/operation",conflict,NetworkJson.Options)).StatusCode==System.Net.HttpStatusCode.BadRequest,"Mismo ID con otros datos rechazado");
        // Persisted pending file simulates a lost response after a successful commit.
        var pendingPath=Path.Combine(directory,"a","pending-operation.json");File.WriteAllText(pendingPath,JsonSerializer.Serialize(new PendingRequest(a.CurrentUser!.Id,request),NetworkJson.Options));
        using(var reopened=new RemoteStore(config,Path.Combine(directory,"a")))
        {
            reopened.Authenticate("admin",pass);Check(reopened.HasPending,"Petición incierta sobrevive al cierre de interfaz");
            Reject(()=>reopened.SaveCategory(new Category(Guid.NewGuid(),"Bloqueada")),"Nueva mutación bloqueada hasta reconciliar");
            var result=reopened.ResolvePending().Deserialize<Sale>(NetworkJson.Options)!;Check(result.Id==first.Id&&!reopened.HasPending,"Reconciliación recupera venta ya confirmada");
        }
        await app.StopAsync();await app.DisposeAsync();app=ServerHost.Build(data);await app.StartAsync();
        a.Authenticate("admin",pass);b.Authenticate("cajero",pass);
        var restarted=(await (await raw.PostAsJsonAsync("api/login",new LoginRequest("admin",pass,config.TerminalId,"Caja A"),NetworkJson.Options)).Content.ReadFromJsonAsync<LoginReply>(NetworkJson.Options))!;
        raw.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",restarted.Token);
        Check((await Send(request)).Id==first.Id,"Idempotencia persiste al reiniciar servidor");
        Check(oa.CurrentCash is not null&&a.StockBalances()[product.Id]==14,"Reinicio conserva inventario y caja abierta");
        var current=oa.CurrentCash!;ob.CloseCash(OperationsService.Expected(oa.State,current,"USD"),0,0);
        new BusinessService(a).SaveSettings(a.GetSettings() with { IndependentCash=true });oa.OpenCash(100,0,0);ob.OpenCash(100,0,0);
        oa.Adjust(product.Id,1-a.StockBalances()[product.Id],"Dejar última unidad");
        var attempts=await Task.WhenAll(Task.Run(()=>TrySell(oa)),Task.Run(()=>TrySell(ob)));
        Check(attempts.Count(x=>x)==1&&a.StockBalances()[product.Id]==0,"Dos ventas simultáneas: solo una obtiene la última unidad");
        Reject(()=>b.SaveUser(null,"ilegítimo","X",UserRole.Administrador,true,pass),"Cajero no puede crear administrador");
        using(var vendor=new RemoteStore(config with { TerminalId=Guid.NewGuid() },Path.Combine(directory,"vendor")))
        {
            vendor.Authenticate("vendedor",pass);Check(!vendor.Can(Permission.Credit)&&vendor.ReadOperations().Abonos.All(p=>p.Payment.Amount==0)&&vendor.ReadOperations().Purchases.Count==0,"Vendedor no recibe detalles de pagos ni compras");
        }
        var sa=oa.CurrentCash!;var sb=ob.CurrentCash!;oa.CloseCash(OperationsService.Expected(oa.State,sa,"USD"),0,0);ob.CloseCash(OperationsService.Expected(ob.State,sb,"USD"),0,0);
        var noAdmin=new SqliteStore(Path.Combine(directory,"no-admin.db"));var noAdminBackup=noAdmin.Backup(Path.Combine(directory,"no-admin-backup.db"));Reject(()=>a.ValidateBackup(noAdminBackup),"Restauración de servidor rechaza respaldo sin administrador y evita bloquear acceso");
        var backup=a.Backup(Path.Combine(directory,"download.db"));a.ValidateBackup(backup);
        var persisted=new SqliteStore(backup);Check(persisted.ReadOperations().Sessions.Count==5&&persisted.StockBalances()[product.Id]==0,"Respaldo remoto conserva cajas históricas e inventario");
        a.SaveCategory(new Category(Guid.NewGuid(),"Temporal"));Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();a.Restore(backup);
        Reject(()=>b.GetProducts(),"Restaurar revoca sesiones de todas las cajas");
        a.Authenticate("admin",pass);Check(a.GetCategories().All(c=>c.Name!="Temporal")&&a.StockBalances()[product.Id]==0,"Restauración central recupera catálogo y saldos");
        raw.DefaultRequestHeaders.Authorization=null;
        var after=(await (await raw.PostAsJsonAsync("api/login",new LoginRequest("admin",pass,config.TerminalId,"Caja A"),NetworkJson.Options)).Content.ReadFromJsonAsync<LoginReply>(NetworkJson.Options))!;raw.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",after.Token);
        Check((await raw.PostAsJsonAsync("api/operation",request,NetworkJson.Options)).StatusCode==System.Net.HttpStatusCode.BadRequest,"Petición de historial anterior no se ejecuta tras restaurar");
        using(var bad=new RemoteStore(config with { Fingerprint=new string('0',64) },Path.Combine(directory,"bad")))Reject(()=>bad.Authenticate("admin",pass),"Certificado distinto al configurado bloquea conexión");
        Check((await raw.PostAsJsonAsync("api/operation",new NetworkRequest(Guid.NewGuid(),NetworkJson.Command("Restore",new{}),after.Epoch),NetworkJson.Options)).StatusCode==System.Net.HttpStatusCode.BadRequest,"API solo ejecuta operaciones expresamente permitidas");
        var user=a.Users().Single(u=>u.Username=="cajero");a.SaveUser(user.Id,user.Username,user.Name,user.Role,false,null);
        Reject(()=>b.GetProducts(),"Desactivar cuenta invalida su sesión existente");
        b.Authenticate("admin",pass);b.SignOut();Reject(()=>b.GetProducts(),"Cerrar sesión revoca token");
        var uiProduct=new Product { Code="UI-RED",Name="Producto UI remoto",PriceUsd=4,CostUsd=1 };a.SaveProduct(uiProduct);oa.Adjust(uiProduct.Id,5,"UI remoto");
        var uiDirectory=Path.Combine(directory,"ui-client");config.Save(uiDirectory);var uiOutput=Path.Combine(directory,"ui");
        var start=new System.Diagnostics.ProcessStartInfo(File.Exists(".tools/dotnet/dotnet.exe") ? Path.GetFullPath(".tools/dotnet/dotnet.exe") : "dotnet") { UseShellExecute=false,CreateNoWindow=true };
        start.ArgumentList.Add(Path.GetFullPath("src/Monii.Desktop/bin/Release/net10.0-windows/Monii.dll"));start.ArgumentList.Add("--data-dir");start.ArgumentList.Add(uiDirectory);start.ArgumentList.Add("--network-ui-verify");start.ArgumentList.Add(uiOutput);start.Environment["MONII_UI_NETWORK_TEST"]=pass;
        using(var ui=System.Diagnostics.Process.Start(start)!)
        {
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(60));await ui.WaitForExitAsync(timeout.Token);
            Check(ui.ExitCode==0,"Interfaz WPF conectada completa apertura, venta y cierre"+(ui.ExitCode==0?"":File.ReadAllText(Path.Combine(uiOutput,"ui-error.txt"))));
        }
        Check(a.StockBalances()[uiProduct.Id]==4&&oa.CurrentCash is null,"Otra conexión observa inventario y cierre producidos en WPF");
        await app.StopAsync();await app.DisposeAsync();
        Reject(()=>oa.OpenCash(20,0,0),"Conexión interrumpida devuelve error controlado");
        Check(a.HasPending&&new SqliteStore(Path.Combine(data,"monii.db")).ReadOperations().Sessions.All(s=>s.ClosedAt is not null),"Sin conexión conserva petición y no simula apertura local");
        app=ServerHost.Build(data);await app.StartAsync();a.Authenticate("admin",pass);a.ResolvePending();
        Check(!a.HasPending&&oa.CurrentCash!.OpeningUsd==20,"Reconectar confirma una petición que no había llegado al servidor");oa.CloseCash(20,0,0);
        new BusinessService(a).SaveSettings(a.GetSettings() with { ShowCop=true,CopRate=5000,ShowManualVes=true,ManualVesRate=100 });oa.OpenCash(20,1000,100000);
        var converted=oa.Sell([(uiProduct.Id,1)],0,[new(Currency.COP,PaymentMethod.Efectivo,20000)]);
        Check(converted.Total==4&&converted.Rates.Cop==5000&&OperationsService.Expected(oa.State,oa.CurrentCash!,"COP")==120000,"Venta en COP usa tasa central y efectivo físico separado");
        new BusinessService(a).SaveSettings(a.GetSettings() with { CopRate=6000 });oa.VoidSale(converted.Id,"Reversión tras cambio de tasa");
        Check(OperationsService.Expected(oa.State,oa.CurrentCash!,"COP")==100000&&a.StockBalances()[uiProduct.Id]==4,"Anulación devuelve COP original aunque cambie tasa actual");oa.CloseCash(20,1000,100000);
        var import=ProductImport.Preview(new ProductImportFile("Red",new[]{"Codigo","Nombre","Categoria","CostoUSD","PrecioUSD"},new[]{new ImportSourceRow(2,new[]{new ImportCell("000IMPORT"),new ImportCell("Importado por API"),new ImportCell("Categoría red"),new ImportCell("1.25"),new ImportCell("2.50")})}),a.GetProducts(),a.GetCategories(),false,true);
        Check(a.ImportProducts(import)==1&&a.GetProducts().Any(p=>p.Code=="000IMPORT"&&p.CostUsd==1.25m)&&a.GetCategories().Any(c=>c.Name=="Categoría red"),"Importación remota conserva código, costo y categoría");
        Check(await a.RefreshRates(false) is not null,"Consulta de tasas se ejecuta en servidor según configuración");
        new BusinessService(a).SaveSettings(a.GetSettings() with { Lots=true });
        oa.ClassifyLot(uiProduct.Id,2,"RED-FEFO",DateOnly.FromDateTime(DateTime.Today.AddDays(1)),"Clasificar por API");
        oa.Adjust(uiProduct.Id,1,"Vencido API","RED-VENCIDO",DateOnly.FromDateTime(DateTime.Today.AddDays(-1)));
        oa.OpenCash(0,0,0);
        var lotSale=oa.Sell([(uiProduct.Id,3)],0,[new(Currency.USD,PaymentMethod.Efectivo,12)]);
        Check(a.ReadOperations().Stock.Single(m=>m.DocumentId==lotSale.Id&&m.LotCode=="RED-FEFO").Quantity==-2,"Servidor asigna FEFO a venta remota");
        Reject(()=>oa.Sell([(uiProduct.Id,2)],0,[new(Currency.USD,PaymentMethod.Efectivo,8)]),"Servidor rechaza venta remota que necesitaría vencidos");
        oa.VoidSale(lotSale.Id,"Reponer lotes API");
        Check(OperationsService.Lots(a.ReadOperations(),uiProduct.Id).Single(l=>l.Code=="RED-FEFO").Quantity==2,"Anulación remota recupera lote original");
        var lotSupplier=new Contact { Name="Proveedor lotes API" };oa.SaveContact(lotSupplier,true);
        var lotPurchase=oa.Buy(lotSupplier.Id,"Lote remoto",[(uiProduct.Id,2,1)],new(Currency.USD,PaymentMethod.Transferencia,2),[new("RED-COMPRA",DateOnly.FromDateTime(DateTime.Today.AddDays(10)))]);
        Check(a.ReadOperations().Purchases.Single(p=>p.Id==lotPurchase.Id).Lines.Single().LotCode=="RED-COMPRA","Compra remota conserva lote y vencimiento");
        oa.VoidPurchase(lotPurchase.Id,"Revertir lote remoto");oa.CloseCash(0,0,0);
        var fullProfile=a.GetSettings();new BusinessService(a).SaveSettings(fullProfile.WithProfile(BusinessProfile.Basic));
        using(var basicClient=new RemoteStore(config with { TerminalId=Guid.NewGuid(),TerminalName="Consulta básica" },Path.Combine(directory,"basic-client")))
        {
            basicClient.Authenticate("admin",pass);Check(basicClient.GetSettings().Profile==BusinessProfile.Basic,"Perfil básico se comparte entre equipos");
            var basicOperations=new OperationsService(basicClient);Reject(()=>basicOperations.OpenCash(0,0,0),"Servidor bloquea apertura de caja en básico");Reject(()=>basicOperations.Sell([(uiProduct.Id,1)],0,[new(Currency.USD,PaymentMethod.Efectivo,4)]),"Servidor bloquea ventas incluso desde petición directa");
            var simple=new Product { Name="Catálogo básico remoto",Code="BAS-RED",PriceUsd=3.50m };new BusinessService(basicClient).Save(simple);
            basicClient.SaveCategory(new Category(Guid.NewGuid(),"Básico remoto"));
            Check(a.GetProducts().Any(p=>p.Code=="BAS-RED"&&p.PriceUsd==3.50m)&&a.GetCategories().Any(c=>c.Name=="Básico remoto"),"Catálogo y categorías funcionan en perfil básico remoto");
            Check(!basicClient.HasPending,"Operaciones prohibidas no dejan pendientes financieros");
        }
        new BusinessService(a).SaveSettings(fullProfile);
        Check(a.ReadOperations().Sales.Any(s=>s.Id==lotSale.Id)&&OperationsService.Lots(a.ReadOperations(),uiProduct.Id).Any(l=>l.Code=="RED-FEFO"&&l.Quantity==2),"Volver a perfil completo conserva ventas y lotes centrales");
        var recoveryServer=Path.Combine(directory,"recovery-server");Directory.CreateDirectory(recoveryServer);
        a.Backup(Path.Combine(recoveryServer,"monii.db"));
        var recoveryLocal=Path.Combine(directory,"recovery-local");Directory.CreateDirectory(recoveryLocal);
        var oldLocal=new SqliteStore(Path.Combine(recoveryLocal,"monii.db"));oldLocal.SaveProduct(new Product { Code="ANTERIOR",Name="Local anterior",PriceUsd=1 });
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        ServerHost.ExportLocal(recoveryServer,recoveryLocal);ServerHost.ActivateLocal(recoveryLocal);
        var recovered=new SqliteStore(Path.Combine(recoveryLocal,"monii.db"));
        Check(recovered.GetProducts().Any(p=>p.Code=="000IMPORT")&&recovered.ReadOperations().Sales.Count==a.ReadOperations().Sales.Count,"Desinstalación recupera catálogo y ventas centrales en modo local");
        Check(ConnectionSettings.Load(recoveryLocal).Mode=="Local","Desinstalación configura inicio local");
        var priorCopy=Directory.GetFiles(recoveryLocal,"monii-before-server-uninstall-*.db").Single();
        Check(new SqliteStore(priorCopy).GetProducts().Any(p=>p.Code=="ANTERIOR"),"Desinstalación conserva copia de los datos locales anteriores");
        Check(File.Exists(Path.Combine(recoveryServer,"monii.db")),"Desinstalación conserva base original del servidor");
        Reject(()=>ServerHost.ExportLocal(recoveryServer,recoveryServer),"Recuperación rechaza reemplazar datos del servidor");
        Console.WriteLine($"RED COMPLETA: {passed} comprobaciones. Datos aislados: {directory}");
    }
    finally { await app.StopAsync();await app.DisposeAsync(); }
    static bool TrySell(OperationsService operations) { try { operations.Sell([(operations.State.Stock.First().ProductId,1)],0,[new(Currency.USD,PaymentMethod.Efectivo,10)]);return true; }catch(ArgumentException) { return false; } }
}
