using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Monii.Application;
using Monii.Domain;
namespace Monii.Infrastructure;

public sealed record ConnectionSettings
{
    public string Mode { get; init; } = "Local";
    public string Address { get; init; } = "https://localhost:58443";
    public string Fingerprint { get; init; } = "";
    public Guid TerminalId { get; init; } = Guid.NewGuid();
    public string TerminalName { get; init; } = Environment.MachineName;
    public static ConnectionSettings Load(string directory) => File.Exists(Path.Combine(directory,"connection.json")) ? JsonSerializer.Deserialize<ConnectionSettings>(File.ReadAllText(Path.Combine(directory,"connection.json")))! : new();
    public void Save(string directory) { Directory.CreateDirectory(directory); File.WriteAllText(Path.Combine(directory,"connection.json"),JsonSerializer.Serialize(this)); }
}
public sealed record PendingRequest(Guid UserId,NetworkRequest Request);
public sealed class RemoteStore : IMoniiStore, IDisposable
{
    private readonly HttpClient client;
    private string epoch="";
    public event Action<bool>? ConnectionChanged;
    private bool connected=true;
    private void ConnectionState(bool value) { if(connected==value)return;connected=value;ConnectionChanged?.Invoke(value); }
    private readonly ConnectionSettings connection;
    private readonly string pendingPath;
    public string DatabasePath => "Datos centralizados en el equipo principal";
    public string? BackupWarning { get; set; }
    public SessionUser? CurrentUser { get; private set; }
    public bool HasUsers => true;
    public string CashScope => GetSettings().IndependentCash ? connection.TerminalId.ToString("N") : "shared";
    public string CashName => GetSettings().IndependentCash ? connection.TerminalName : "Caja compartida";
    public bool HasPending => File.Exists(pendingPath);
    public RemoteStore(ConnectionSettings settings,string directory)
    {
        if(settings.TerminalId==Guid.Empty||string.IsNullOrWhiteSpace(settings.TerminalName)) throw new ArgumentException("Identifica esta caja.");
        if(!Uri.TryCreate(settings.Address,UriKind.Absolute,out var uri)||uri.Scheme!="https"||uri.UserInfo.Length>0||uri.AbsolutePath!="/") throw new ArgumentException("Indica la dirección HTTPS del equipo principal.");
        var fingerprint=settings.Fingerprint.Replace(":","").Replace(" ","").ToUpperInvariant();
        if(fingerprint.Length!=64||fingerprint.Any(c=>!Uri.IsHexDigit(c))) throw new ArgumentException("Copia la huella de conexión del equipo principal (64 caracteres).");
        var handler=new HttpClientHandler { AllowAutoRedirect=false,UseProxy=false };
        handler.ServerCertificateCustomValidationCallback=(_,cert,_,_)=>cert is not null&&Convert.ToHexString(SHA256.HashData(cert.RawData))==fingerprint;
        client=new HttpClient(handler) { BaseAddress=uri,Timeout=TimeSpan.FromSeconds(15) };connection=settings;
        Directory.CreateDirectory(directory);pendingPath=Path.Combine(directory,"pending-operation.json");
    }
    private JsonElement Send(string route,object? body=null)
    {
        try
        {
            using var request=new HttpRequestMessage(body is null?HttpMethod.Get:HttpMethod.Post,route);
            if(body is not null) request.Content=JsonContent.Create(body,options:NetworkJson.Options);
            using var response=client.Send(request);
            var json=response.Content.ReadAsStringAsync().GetAwaiter().GetResult();ConnectionState(true);
            if(!response.IsSuccessStatusCode)
            {
                string message;try { message=JsonDocument.Parse(json).RootElement.GetProperty("error").GetString()!; } catch(JsonException) { message="El servidor rechazó la operación."; }
                if(response.StatusCode==System.Net.HttpStatusCode.Unauthorized) throw new UnauthorizedAccessException(message);
                if((int)response.StatusCode<500) throw new ArgumentException(message);
                throw new IOException(message);
            }
            return JsonSerializer.Deserialize<JsonElement>(json,NetworkJson.Options);
        }
        catch(Exception e) when(e is HttpRequestException or TaskCanceledException) { ConnectionState(false);throw new IOException("No hay conexión con el equipo principal. Los datos confirmados se conservan. Si estabas cobrando, reconcilia la operación pendiente antes de continuar.",e); }
    }
    private T Read<T>(string name,object? args=null)=>Send("api/read",new RemoteCommand(name,JsonSerializer.SerializeToElement(args??new{},NetworkJson.Options))).Deserialize<T>(NetworkJson.Options)!;
    private T Write<T>(string name,object args)
    {
        if(HasPending) throw new ArgumentException("Hay una operación pendiente de confirmar. Resuélvela en Configuración > Conexión y servidor.");
        return Send("api/write",NetworkJson.Command(name,args)).Deserialize<T>(NetworkJson.Options)!;
    }
    public T Transact<T>(Func<OperationsState,IReadOnlyList<Product>,BusinessSettings,T> operation,string action,RemoteCommand? command=null)
    {
        if(command is null||CurrentUser is null) throw new UnauthorizedAccessException("Inicia sesión antes de operar.");
        PendingRequest pending;
        if(HasPending)
        {
            pending=JsonSerializer.Deserialize<PendingRequest>(File.ReadAllText(pendingPath),NetworkJson.Options)!;
            if(pending.UserId!=CurrentUser.Id||JsonSerializer.Serialize(pending.Request.Command,NetworkJson.Options)!=JsonSerializer.Serialize(command,NetworkJson.Options)) throw new ArgumentException("Resuelve primero la operación pendiente en Configuración > Conexión y servidor.");
        }
        else { pending=new(CurrentUser.Id,new(Guid.NewGuid(),command,epoch)); File.WriteAllText(pendingPath,JsonSerializer.Serialize(pending,NetworkJson.Options)); }
        try { var result=Send("api/operation",pending.Request).Deserialize<T>(NetworkJson.Options)!;File.Delete(pendingPath);return result; }
        catch(ArgumentException) { File.Delete(pendingPath);throw; }
    }
    public JsonElement ResolvePending()
    {
        var pending=JsonSerializer.Deserialize<PendingRequest>(File.ReadAllText(pendingPath),NetworkJson.Options)!;
        if(CurrentUser?.Id!=pending.UserId)throw new ArgumentException("Inicia sesión con el usuario que realizó la operación pendiente.");
        try { var result=Send("api/operation",pending.Request);File.Delete(pendingPath);return result; }
        catch(ArgumentException) { File.Delete(pendingPath);throw; }
    }
    public string PendingDescription => !HasPending ? "Ninguna" : JsonSerializer.Deserialize<PendingRequest>(File.ReadAllText(pendingPath),NetworkJson.Options)!.Request.Command.Method switch
    {
        "Sell"=>"Venta", "Buy"=>"Compra", "OpenCash"=>"Apertura de caja", "CloseCash"=>"Cierre de caja", "PayDebt"=>"Abono de crédito", "VoidSale"=>"Anulación de venta", "VoidPurchase"=>"Anulación de compra", "VoidDebtPayment"=>"Anulación de abono", "ReturnSale"=>"Devolución de venta", "CashMovement"=>"Movimiento de caja", "Adjust"=>"Ajuste de inventario", "SaveContact"=>"Cliente o proveedor", _=>"Operación pendiente"
    };
    // This is only a UI hint; the server rechecks account state and permission on every request.
    public bool Can(Permission permission)=>CurrentUser is not null&&SqliteStore.Allowed(CurrentUser.Role,permission);
    public void Require(Permission permission) { if(!Can(permission))throw new UnauthorizedAccessException("Tu usuario no tiene permiso para esta operación."); }
    public SessionUser Authenticate(string username,string password)
    {
        SignOut();var reply=Send("api/login",new LoginRequest(username,password,connection.TerminalId,connection.TerminalName)).Deserialize<LoginReply>(NetworkJson.Options)!;
        client.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",reply.Token);CurrentUser=reply.User;epoch=reply.Epoch;return reply.User;
    }
    public void SignOut() { if(CurrentUser is not null) { try { Send("api/logout",new{}); } catch(IOException) { } catch(UnauthorizedAccessException) { } } client.DefaultRequestHeaders.Authorization=null;CurrentUser=null; }
    public IReadOnlyList<Product> GetProducts()=>Read<List<Product>>("Products");
    public void SaveProduct(Product product)=>Write<bool>("Product",new { product });
    public BusinessSettings GetSettings()=>CurrentUser is null ? new() : Read<BusinessSettings>("Settings");
    public void SaveSettings(BusinessSettings settings)=>Write<bool>("Settings",new { settings });
    public IReadOnlyList<AuditEntry> GetAudit()=>Read<List<AuditEntry>>("Audit");
    public OperationsState ReadOperations()=>Read<OperationsState>("State");
    public IReadOnlyList<Category> GetCategories()=>Read<List<Category>>("Categories");
    public void SaveCategory(Category category)=>Write<bool>("Category",new { category });
    public IReadOnlyDictionary<Guid,decimal> StockBalances()=>Read<Dictionary<Guid,decimal>>("Stock");
    public IReadOnlyList<Sale> SalesPage(int page,string search="",int size=50)=>Read<List<Sale>>("Sales",new { page,search,size });
    public IReadOnlyList<UserAccount> Users()=>Read<List<UserAccount>>("Users");
    public void SaveUser(Guid? id,string username,string name,UserRole role,bool active,string? password)=>Write<bool>("User",new { id,username,name,role,active,password });
    public int ImportProducts(ProductImportPreview preview)=>Write<int>("Import",new { preview });
    public void AutomaticBackup()=>Write<bool>("AutoBackup",new{});
    public void ApplyOfficialRates(ExchangeQuote? bcv,ExchangeQuote? cop)=>throw new ArgumentException("Las tasas se consultan en el equipo principal.");
    public Task<ExchangeRefresh> RefreshRates(bool force)=>Task.Run(()=>Read<ExchangeRefresh>("Rates",new { force }));
    public string Backup(string destination)
    {
        if(File.Exists(destination)) throw new ArgumentException("Elige un nombre nuevo para el respaldo.");
        var bytes=Convert.FromBase64String(Read<string>("Backup"));Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destination))!);File.WriteAllBytes(destination,bytes);return destination;
    }
    public void ValidateBackup(string path)=>Write<bool>("ValidateBackup",new { data=Convert.ToBase64String(File.ReadAllBytes(path)) });
    public string Restore(string sourcePath)=>Write<string>("Restore",new { data=Convert.ToBase64String(File.ReadAllBytes(sourcePath)) });
    public JsonElement Status()=>Read<JsonElement>("Status");
    public void Dispose()=>client.Dispose();
}
