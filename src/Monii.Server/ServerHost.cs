using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Reflection;
using System.Net;
using Monii.Application;
using Monii.Domain;
using Monii.Infrastructure;
namespace Monii.Server;

public sealed record ServerConfiguration(int Port=58443);
public sealed record ServerSession(SessionUser User,string Stamp,Guid TerminalId,string TerminalName,DateTimeOffset Expires);
public static class ServerHost
{
    public static async Task Run(string[] args)
    {
        string Option(string name,string fallback) { var i=Array.IndexOf(args,name);return i>=0&&i+1<args.Length?args[i+1]:fallback; }
        var directory=Path.GetFullPath(Option("--data-dir",Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),"MoniiServer","data")));
        if(args.Contains("--prepare")) { Prepare(directory,Option("--import",""));return; }
        var app=Build(directory,args);await app.RunAsync();
    }
    public static void Prepare(string directory,string import="")
    {
        Directory.CreateDirectory(directory);var database=Path.Combine(directory,"monii.db");
        if(import.Length>0)
        {
            if(File.Exists(database)) throw new InvalidOperationException("El servidor ya tiene datos. No se sobrescriben al instalar.");
            // Validate the source without touching it before taking ownership of a copy.
            var probe=new SqliteStore(Path.Combine(directory,"validation.db"));probe.ValidateBackup(import);
            File.Copy(import,database);
        }
        var store=new SqliteStore(database);
        if(import.Length>0&&store.ReadOperations().Sessions.Any(s=>s.ClosedAt is null))throw new InvalidOperationException("Cierra todas las cajas antes de preparar el servidor.");
        if(!store.HasUsers)throw new InvalidOperationException("Crea un administrador en Monii local antes de instalar el servidor.");
        var certificatePath=Path.Combine(directory,"server.pfx");
        if(!File.Exists(certificatePath))
        {
            using var key=RSA.Create(3072);var request=new CertificateRequest("CN=Monii principal",key,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1);
            var san=new SubjectAlternativeNameBuilder();san.AddDnsName("localhost");san.AddDnsName(Environment.MachineName);san.AddIpAddress(IPAddress.Loopback);request.CertificateExtensions.Add(san.Build());
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false,false,0,false));
            request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature|X509KeyUsageFlags.KeyEncipherment,true));
            var usages=new System.Security.Cryptography.OidCollection { new("1.3.6.1.5.5.7.3.1") };request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(usages,true));
            using var cert=request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1),DateTimeOffset.UtcNow.AddYears(5));File.WriteAllBytes(certificatePath,cert.Export(X509ContentType.Pfx));
        }
        using var loaded=X509CertificateLoader.LoadPkcs12FromFile(certificatePath,null,X509KeyStorageFlags.EphemeralKeySet);
        var connection=new ConnectionSettings { Mode="Principal",Fingerprint=Convert.ToHexString(SHA256.HashData(loaded.RawData)) };
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(directory)!,"connection.json"),JsonSerializer.Serialize(connection));
        if(!File.Exists(Path.Combine(directory,"server.json")))File.WriteAllText(Path.Combine(directory,"server.json"),JsonSerializer.Serialize(new ServerConfiguration()));
        Console.WriteLine("Servidor preparado. Huella: "+connection.Fingerprint);
    }
    public static WebApplication Build(string directory,string[]? args=null)
    {
        var configuration=JsonSerializer.Deserialize<ServerConfiguration>(File.ReadAllText(Path.Combine(directory,"server.json")))!;
        var database=Path.Combine(directory,"monii.db");var initial=new SqliteStore(database);
        if(!initial.HasUsers)throw new InvalidOperationException("El servidor necesita un administrador creado en modo local.");
        var certificate=X509CertificateLoader.LoadPkcs12FromFile(Path.Combine(directory,"server.pfx"),null,X509KeyStorageFlags.MachineKeySet);
        var builder=WebApplication.CreateBuilder(args??[]);
        builder.Logging.AddProvider(new FileLog(Path.Combine(Path.GetDirectoryName(directory)!,"logs")));
        builder.Services.AddWindowsService(options=>options.ServiceName="MoniiServer");
        builder.WebHost.ConfigureKestrel(options=>
        {
            options.Limits.MaxRequestBodySize=140_000_000;
            options.Listen(IPAddress.Any,configuration.Port,listen=>listen.UseHttps(certificate));
        });
        builder.Services.ConfigureHttpJsonOptions(options=> { options.SerializerOptions.IncludeFields=true;options.SerializerOptions.PropertyNameCaseInsensitive=true; });
        var app=builder.Build();app.Lifetime.ApplicationStopped.Register(certificate.Dispose);var sessions=new ConcurrentDictionary<string,ServerSession>();var gate=new SemaphoreSlim(1,1);
        var attempts=new ConcurrentDictionary<string,(DateTimeOffset At,int Count)>();
        app.Use(async(context,next)=>
        {
            await gate.WaitAsync(context.RequestAborted);
            try { await next(context); }
            catch(Exception error)
            {
                while(error is TargetInvocationException&&error.InnerException is not null)error=error.InnerException;
                var status=error is UnauthorizedAccessException?401:error is ArgumentException or InvalidDataException or JsonException or KeyNotFoundException?400:500;
                if(status==500) app.Logger.LogError(error,"Operación rechazada");
                context.Response.StatusCode=status;
                await context.Response.WriteAsJsonAsync(new { error=status==500?"No se confirmó la respuesta. Reconcilia la operación pendiente; los datos confirmados se conservan.":error.Message });
            }
            finally { gate.Release(); }
        });
        SqliteStore Authorized(HttpContext context)
        {
            var token=context.Request.Headers.Authorization.ToString();
            if(!token.StartsWith("Bearer ")||!sessions.TryGetValue(token[7..],out var session)||session.Expires<DateTimeOffset.UtcNow)throw new UnauthorizedAccessException("Inicia sesión en el equipo principal.");
            var store=new SqliteStore(database) { TerminalId=session.TerminalId,NetworkTerminalName=session.TerminalName };store.ResumeSession(session.User,session.Stamp);return store;
        }
        app.MapPost("/api/login",(LoginRequest login,HttpContext context)=>
        {
            if(login.TerminalId==Guid.Empty||string.IsNullOrWhiteSpace(login.TerminalName)||login.TerminalName.Length>80||login.Username.Length>100||login.Password.Length>1024)throw new ArgumentException("Datos de acceso inválidos.");
            var ip=context.Connection.RemoteIpAddress?.ToString()??"unknown";var now=DateTimeOffset.UtcNow;
            var counter=attempts.AddOrUpdate(ip,(now,1),(_,old)=>now-old.At>TimeSpan.FromMinutes(1)?(now,1):(old.At,old.Count+1));
            if(counter.Count>15)throw new ArgumentException("Demasiados intentos. Espera un minuto antes de iniciar sesión.");
            foreach(var expired in sessions.Where(s=>s.Value.Expires<now))sessions.TryRemove(expired.Key,out _);
            var store=new SqliteStore(database);var user=store.Authenticate(login.Username,login.Password);
            var token=Convert.ToHexString(RandomNumberGenerator.GetBytes(32));sessions[token]=new(user,store.SessionStamp(user.Id),login.TerminalId,login.TerminalName,now.AddHours(12));return new LoginReply(token,user,store.NetworkEpoch);
        });
        app.MapPost("/api/logout",(HttpContext context)=> { Authorized(context);sessions.TryRemove(context.Request.Headers.Authorization.ToString()[7..],out _);return true; });
        app.MapPost("/api/operation",(NetworkRequest request,HttpContext context)=>
        {
            var store=Authorized(context);if(request.Id==Guid.Empty)throw new ArgumentException("Identificador de operación inválido.");
            if(request.Epoch!=store.NetworkEpoch)throw new ArgumentException("La base fue restaurada. Esta petición corresponde al historial anterior y no se repetirá. Revisa sus documentos.");
            store.RequestId=request.Id;store.RequestHash=Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(request.Command,NetworkJson.Options)));
            var allowed=new HashSet<string>(["Adjust","Sell","VoidSale","OpenCash","CloseCash","CashMovement","SaveContact","Buy","VoidPurchase","PayDebt","VoidDebtPayment","ReturnSale"]);
            if(!allowed.Contains(request.Command.Method))throw new ArgumentException("Operación desconocida.");
            var method=typeof(OperationsService).GetMethod(request.Command.Method)!;
            var parameters=method.GetParameters().Select(p=>request.Command.Arguments.GetProperty(p.Name!).Deserialize(p.ParameterType,NetworkJson.Options)).ToArray();
            return Results.Json(method.Invoke(new OperationsService(store),parameters)??true,NetworkJson.Options);
        });
        app.MapPost("/api/read",async(RemoteCommand request,HttpContext context)=>
        {
            var store=Authorized(context);var a=request.Arguments;object value;
            switch(request.Method)
            {
                case "Can": value=store.Can(a.GetProperty("permission").Deserialize<Permission>());break;
                case "Settings": value=store.GetSettings() with { BackupDirectory="",UpdateFeedUrl="" };break;
                case "Products": value=store.GetProducts().Select(p=>store.Can(Permission.Products)?p:p with { CostUsd=0 });break;
                case "Categories": value=store.GetCategories();break;
                case "Stock": value=store.StockBalances();break;
                case "Users": value=store.Users();break;
                case "Audit":store.Require(Permission.Reports);value=store.GetAudit();break;
                case "Sales": value=store.SalesPage(a.GetProperty("page").GetInt32(),a.GetProperty("search").GetString()!,a.GetProperty("size").GetInt32()).Where(s=>store.Can(Permission.Reports)||s.SellerId==store.CurrentUser!.Id).Select(s=>store.Can(Permission.Reports)?s:s with { Lines=s.Lines.Select(l=>l with { Cost=0 }).ToList() });break;
                case "State":value=VisibleState(store);break;
                case "Status":store.Require(Permission.Settings);value=new { Verification=File.Exists(Path.Combine(directory,"verification.allow")),State="En línea",ActiveSessions=sessions.Count,OpenCash=store.ReadOperations().Sessions.Count(s=>s.ClosedAt is null),Terminals=sessions.Values.Select(s=>new { s.TerminalId,s.TerminalName,s.User.Name }).Distinct().ToArray(),Version="0.7.0" };break;
                case "Rates":store.Require(Permission.Settings);value=await new ExchangeRates(store).RefreshAsync(a.GetProperty("force").GetBoolean());break;
                case "Backup":
                    store.Require(Permission.Backup);var file=Path.Combine(directory,"downloads",Guid.NewGuid()+".db");
                    try { store.Backup(file);value=Convert.ToBase64String(await File.ReadAllBytesAsync(file)); } finally { if(File.Exists(file))File.Delete(file); } break;
                default:throw new ArgumentException("Consulta desconocida.");
            }
            return Results.Json(value,NetworkJson.Options);
        });
        app.MapPost("/api/write",(RemoteCommand request,HttpContext context)=>
        {
            var store=Authorized(context);var a=request.Arguments;object result=true;
            switch(request.Method)
            {
                case "Product":new BusinessService(store).Save(a.GetProperty("product").Deserialize<Product>(NetworkJson.Options)!);break;
                case "Settings":
                    var incoming=a.GetProperty("settings").Deserialize<BusinessSettings>(NetworkJson.Options)!;var previous=store.GetSettings();
                    new BusinessService(store).SaveSettings(incoming with { BackupDirectory=previous.BackupDirectory,UpdateFeedUrl=previous.UpdateFeedUrl });break;
                case "Category":store.SaveCategory(a.GetProperty("category").Deserialize<Category>(NetworkJson.Options)!);break;
                case "User":store.SaveUser(a.GetProperty("id").Deserialize<Guid?>(),a.GetProperty("username").GetString()!,a.GetProperty("name").GetString()!,a.GetProperty("role").Deserialize<UserRole>(),a.GetProperty("active").GetBoolean(),a.GetProperty("password").GetString());break;
                case "Import":result=store.ImportProducts(a.GetProperty("preview").Deserialize<ProductImportPreview>(NetworkJson.Options)!);break;
                case "AutoBackup":store.Require(Permission.Backup);store.AutomaticBackup();break;
                case "ValidateBackup":case "Restore":
                    store.Require(request.Method=="Restore"?Permission.Restore:Permission.Backup);
                    var bytes=Convert.FromBase64String(a.GetProperty("data").GetString()!);if(bytes.Length>100_000_000)throw new ArgumentException("Respaldo superior a 100 MB.");
                    var upload=Path.Combine(directory,"upload-"+Guid.NewGuid()+".db");
                    try
                    {
                        File.WriteAllBytes(upload,bytes);store.ValidateBackup(upload);EnsureAdministratorBackup(upload);
                        if(request.Method=="Restore")
                        {
                            if(store.ReadOperations().Sessions.Any(s=>s.ClosedAt is null))throw new ArgumentException("Cierra todas las cajas antes de restaurar.");
                            result=Path.GetFileName(store.Restore(upload));store.RenewNetworkEpoch();sessions.Clear();
                        }
                    }
                    finally { if(File.Exists(upload))File.Delete(upload); }break;
                default:throw new ArgumentException("Acción desconocida.");
            }
            return Results.Json(result,NetworkJson.Options);
        });
        // Maintenance shares the same gate as requests: no backup/rate update can interleave with restore.
        _=Maintenance(app,database,gate);
        return app;
    }
    private static void EnsureAdministratorBackup(string path)
    {
        using var connection=new Microsoft.Data.Sqlite.SqliteConnection(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource=path,Mode=Microsoft.Data.Sqlite.SqliteOpenMode.ReadOnly,Pooling=false }.ToString());connection.Open();using var command=connection.CreateCommand();
        command.CommandText="SELECT count(*) FROM sqlite_master WHERE type='table' AND name='users'";
        if(Convert.ToInt32(command.ExecuteScalar())==0)throw new ArgumentException("El respaldo del servidor debe conservar un administrador activo.");
        command.CommandText="SELECT count(*) FROM users WHERE role=0 AND active=1";
        if(Convert.ToInt32(command.ExecuteScalar())==0)throw new ArgumentException("El respaldo del servidor debe conservar un administrador activo.");
    }
    private static OperationsState VisibleState(SqliteStore store)
    {
        var state=store.ReadOperations();var reports=store.Can(Permission.Reports);
        if(!reports)
        {
            state.Sales=state.Sales.Select(s=>s with { Lines=s.Lines.Select(l=>l with { Cost=0 }).ToList() }).ToList();
            state.Returns=state.Returns.Select(r=>r with { CostReduction=0,Lines=r.Lines.Select(l=>l with { Cost=0 }).ToList() }).ToList();
            state.Sessions=state.Sessions.Where(s=>s.CashScope==store.CashScope).ToList();var ids=state.Sessions.Select(s=>s.Id).ToHashSet();state.Cash=state.Cash.Where(e=>ids.Contains(e.SessionId)).ToList();
        }
        if(!store.Can(Permission.Purchases)) { state.Purchases=[];state.Suppliers=[]; }
        if(!store.Can(Permission.Customers))state.Customers=[];
        // Customers can see debt balances. Keep USD values needed for Debt(), but hide payment details without Credit.
        if(!store.Can(Permission.Credit))state.Abonos=state.Abonos.Select(a=>a with { Payment=new(Currency.USD,PaymentMethod.Efectivo,0),Rates=new(0,0,0),Voided=a.Voided }).ToList();
        return state;
    }
    private static async Task Maintenance(WebApplication app,string database,SemaphoreSlim gate)
    {
        try
        {
            using var timer=new PeriodicTimer(TimeSpan.FromMinutes(60));
            do
            {
                await gate.WaitAsync(app.Lifetime.ApplicationStopping);
                try { var store=new SqliteStore(database);store.AutomaticBackup();await new ExchangeRates(store).RefreshAsync(); }
                catch(Exception error) { app.Logger.LogWarning(error,"Mantenimiento del servidor"); }
                finally { gate.Release(); }
            }while(await timer.WaitForNextTickAsync(app.Lifetime.ApplicationStopping));
        }
        catch(OperationCanceledException) { }
    }
}
