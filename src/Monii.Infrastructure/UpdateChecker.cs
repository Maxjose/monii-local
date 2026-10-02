using System.Net;
using System.Text;
using System.Text.Json;

namespace Monii.Infrastructure;

public sealed record UpdateCatalog(int Schema,string Application,string Channel,string Platform,ReleaseManifest? Latest);
public enum UpdateCheckStatus { NoReleases,Current,Available,NotPublished,Error }
public sealed record UpdateCheckResult(UpdateCheckStatus Status,DateTimeOffset At,string Message,ReleaseManifest? Latest=null)
{
    public bool Verified=>Status is UpdateCheckStatus.NoReleases or UpdateCheckStatus.Current or UpdateCheckStatus.Available;
}
public sealed class UpdateChecker(HttpClient? httpClient=null,string? publicKey=null)
{
    public const string DefaultFeed="https://raw.githubusercontent.com/Maxjose/monii-local/main/updates/stable.json";
    private static readonly HttpClient Shared=new(new HttpClientHandler { AllowAutoRedirect=false }) { Timeout=TimeSpan.FromSeconds(20) };
    public static UpdateCatalog VerifyCatalog(string envelope,string? key=null)
    {
        var catalog=JsonSerializer.Deserialize<UpdateCatalog>(UpdatePackages.VerifySignedPayload(envelope,key))??throw new InvalidDataException("Catálogo de actualizaciones inválido.");
        if(catalog.Schema!=1||catalog.Application!="Monii"||catalog.Channel!="stable"||catalog.Platform!="win-x64") throw new InvalidDataException("Manifiesto incompatible con Monii, el canal estable o Windows x64.");
        if(catalog.Latest is { } release)
        {
            UpdatePackages.ValidateRelease(release);
            if(string.IsNullOrWhiteSpace(release.PackageUrl)) throw new InvalidDataException("La versión publicada debe indicar su paquete HTTPS.");
        }
        return catalog;
    }
    public async Task<UpdateCheckResult> CheckAsync(string feed,Version current,CancellationToken cancellation=default)
    {
        var at=DateTimeOffset.UtcNow;
        if(!Uri.TryCreate(feed,UriKind.Absolute,out var uri)||uri.Scheme!=Uri.UriSchemeHttps||uri.UserInfo.Length>0) return new(UpdateCheckStatus.Error,at,"La dirección del manifiesto debe usar HTTPS, sin credenciales.");
        try
        {
            using var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancellation); timeout.CancelAfter(TimeSpan.FromSeconds(20));
            var token=timeout.Token;
            using var request=new HttpRequestMessage(HttpMethod.Get,uri);
            request.Headers.UserAgent.ParseAdd("Monii/"+current.ToString()); request.Headers.CacheControl=new() { NoCache=true };
            using var response=await (httpClient??Shared).SendAsync(request,HttpCompletionOption.ResponseHeadersRead,token);
            if(response.StatusCode==HttpStatusCode.NotFound) return new(UpdateCheckStatus.NotPublished,at,"El manifiesto todavía no está publicado o no es accesible. No se puede confirmar si hay actualizaciones.");
            response.EnsureSuccessStatusCode();
            if(response.RequestMessage?.RequestUri?.Scheme!=Uri.UriSchemeHttps||response.Content.Headers.ContentLength>100_000) throw new InvalidDataException("Respuesta de actualizaciones no válida o demasiado grande.");
            await using var stream=await response.Content.ReadAsStreamAsync(token); using var buffer=new MemoryStream();
            var chunk=new byte[8192]; int read;
            while((read=await stream.ReadAsync(chunk,token))>0) { if(buffer.Length+read>100_000) throw new InvalidDataException("Manifiesto demasiado grande."); buffer.Write(chunk,0,read); }
            var catalog=VerifyCatalog(new UTF8Encoding(false,true).GetString(buffer.ToArray()),publicKey);
            if(catalog.Latest is null) return new(UpdateCheckStatus.NoReleases,at,"El manifiesto es válido. Todavía no se han publicado versiones para actualizar.");
            var newer=UpdatePackages.Normalize(Version.Parse(catalog.Latest.Version))>UpdatePackages.Normalize(current);
            return newer?new(UpdateCheckStatus.Available,at,"Hay una actualización disponible: Monii "+catalog.Latest.Version,catalog.Latest):new(UpdateCheckStatus.Current,at,"Monii está al día con las versiones publicadas.",catalog.Latest);
        }
        catch(OperationCanceledException) { return new(UpdateCheckStatus.Error,at,cancellation.IsCancellationRequested?"Comprobación cancelada.":"La comprobación agotó el tiempo de espera. Inténtalo más tarde."); }
        catch(Exception e) when(e is HttpRequestException or IOException or InvalidDataException or JsonException or FormatException or System.Security.Cryptography.CryptographicException or ArgumentException)
        { return new(UpdateCheckStatus.Error,at,"No se pudo verificar la actualización. "+e.Message); }
    }
}
