using System.Text.Json;
using Monii.Domain;
namespace Monii.Application;

public static class NetworkJson
{
    public static readonly JsonSerializerOptions Options = new() { IncludeFields=true, PropertyNameCaseInsensitive=true };
    public static RemoteCommand Command(string method, object arguments) => new(method,JsonSerializer.SerializeToElement(arguments,Options));
}
public sealed record RemoteCommand(string Method,JsonElement Arguments);
public sealed record NetworkRequest(Guid Id,RemoteCommand Command,string Epoch="");
public sealed record LoginRequest(string Username,string Password,Guid TerminalId,string TerminalName);
public sealed record LoginReply(string Token,SessionUser User,string Epoch);
public interface IMoniiStore : IStore, IOperationsStore
{
    string DatabasePath { get; }
    string? BackupWarning { get; set; }
    SessionUser? CurrentUser { get; }
    bool HasUsers { get; }
    bool Can(Permission permission);
    void Require(Permission permission);
    void SignOut();
    SessionUser Authenticate(string username,string password);
    IReadOnlyList<UserAccount> Users();
    void SaveUser(Guid? id,string username,string name,UserRole role,bool active,string? password);
    IReadOnlyList<Category> GetCategories();
    void SaveCategory(Category category);
    IReadOnlyDictionary<Guid,decimal> StockBalances();
    IReadOnlyList<Sale> SalesPage(int page,string search="",int size=50);
    int ImportProducts(ProductImportPreview preview);
    string Backup(string destination);
    void ValidateBackup(string path);
    string Restore(string sourcePath);
    void AutomaticBackup();
    void ApplyOfficialRates(ExchangeQuote? bcv,ExchangeQuote? cop);
}
