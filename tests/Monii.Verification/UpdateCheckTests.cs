using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Monii.Infrastructure;

internal static class UpdateCheckTests
{
    public static void Run()
    {
        using var rsa=RSA.Create(2048); var publicKey=rsa.ExportSubjectPublicKeyInfoPem(); var count=0;
        void Check(bool okay,string label) { if(!okay) throw new Exception("FALLÓ DETECCIÓN: "+label); Console.WriteLine("OK DETECCIÓN: "+label); count++; }
        string Sign(UpdateCatalog value)
        { var bytes=JsonSerializer.SerializeToUtf8Bytes(value); return JsonSerializer.Serialize(new SignedRelease(Convert.ToBase64String(bytes),Convert.ToBase64String(rsa.SignData(bytes,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1)))); }
        var empty=new UpdateCatalog(1,"Monii","stable","win-x64",null);
        Check(UpdateChecker.VerifyCatalog(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"stable.json"))).Latest is null,"Manifiesto real de preparación firmado con clave original, sin versiones");
        var release=new ReleaseManifest("0.7.0",new string('A',64),1234,"https://github.com/Maxjose/monii-local/releases/download/v0.7.0/Monii.zip","Notas de prueba; no existe ni se descarga.");
        (UpdateCheckResult Result,int Requests) Fetch(string body,HttpStatusCode status=HttpStatusCode.OK,string? redirect=null,string? feed=null)
        {
            using var handler=new Handler(body,status,redirect); using var http=new HttpClient(handler);
            var result=new UpdateChecker(http,publicKey).CheckAsync(feed??UpdateChecker.DefaultFeed,new Version(0,6,0)).GetAwaiter().GetResult(); return(result,handler.Count);
        }
        var result=Fetch(Sign(empty)); Check(result.Result.Status==UpdateCheckStatus.NoReleases&&result.Result.Verified,"Manifiesto firmado sin versiones no necesita paquete");
        result=Fetch(Sign(empty with { Latest=release })); Check(result.Result.Status==UpdateCheckStatus.Available&&result.Result.Latest==release,"Versión mayor detectada con notas");
        Check(result.Requests==1,"Detección solo consulta manifiesto; nunca descarga paquete");
        Check(Fetch(Sign(empty with { Latest=release with { Version="0.6.0.0" } })).Result.Status==UpdateCheckStatus.Current,"Versiones 0.6.0 y 0.6.0.0 equivalentes");
        Check(Fetch(Sign(empty with { Latest=release with { Version="0.5.0" } })).Result.Status==UpdateCheckStatus.Current,"No anuncia downgrade");
        Check(Fetch("",HttpStatusCode.NotFound).Result.Status==UpdateCheckStatus.NotPublished,"404 informa manifiesto no publicado; no afirma estar al día");
        Check(Fetch("",HttpStatusCode.ServiceUnavailable).Result.Status==UpdateCheckStatus.Error,"503 informa error de comprobación");
        Check(Fetch("<html>error</html>").Result.Status==UpdateCheckStatus.Error,"HTML no se interpreta como manifiesto");
        Check(Fetch("{}").Result.Status==UpdateCheckStatus.Error,"Firma o contenido ausentes rechazados");
        var signed=JsonSerializer.Deserialize<SignedRelease>(Sign(empty))!;
        Check(Fetch(JsonSerializer.Serialize(signed with { Manifest=Convert.ToBase64String(Encoding.UTF8.GetBytes("{}")) })).Result.Status==UpdateCheckStatus.Error,"Contenido alterado rechazado por firma");
        using(var another=RSA.Create(2048))
        { using var handler=new Handler(Sign(empty),HttpStatusCode.OK,null); using var http=new HttpClient(handler); Check(new UpdateChecker(http,another.ExportSubjectPublicKeyInfoPem()).CheckAsync(UpdateChecker.DefaultFeed,new Version(0,6,0)).GetAwaiter().GetResult().Status==UpdateCheckStatus.Error,"Clave ajena no autentica manifiesto"); }
        Check(Fetch(Sign(empty with { Schema=2 })).Result.Status==UpdateCheckStatus.Error,"Esquema futuro rechazado");
        Check(Fetch(Sign(empty with { Application="Otro" })).Result.Status==UpdateCheckStatus.Error,"Manifiesto de otra aplicación rechazado");
        Check(Fetch(Sign(empty with { Channel="beta" })).Result.Status==UpdateCheckStatus.Error,"Canal diferente rechazado");
        Check(Fetch(Sign(empty with { Platform="linux-x64" })).Result.Status==UpdateCheckStatus.Error,"Plataforma diferente rechazada");
        Check(Fetch(Sign(empty with { Latest=release with { PackageUrl="http://example.test/file.zip" } })).Result.Status==UpdateCheckStatus.Error,"Paquete sin HTTPS rechazado");
        Check(Fetch(Sign(empty with { Latest=release with { Size=0 } })).Result.Status==UpdateCheckStatus.Error,"Tamaño inválido rechazado");
        Check(Fetch(Sign(empty with { Latest=release with { Notes=null! } })).Result.Status==UpdateCheckStatus.Error,"Campos nulos no producen excepción no controlada");
        Check(Fetch(new string('X',100001)).Result.Status==UpdateCheckStatus.Error,"Límite de manifiesto validado");
        Check(Fetch(Sign(empty),redirect:"http://example.test/file").Result.Status==UpdateCheckStatus.Error,"Respuesta redirigida a HTTP rechazada");
        result=Fetch(Sign(empty),feed:"http://example.test/feed"); Check(result.Result.Status==UpdateCheckStatus.Error&&result.Requests==0,"Dirección insegura bloqueada antes de conectar");
        result=Fetch(Sign(empty),feed:"https://user:secret@example.test/feed"); Check(result.Result.Status==UpdateCheckStatus.Error&&result.Requests==0,"Credenciales en URL bloqueadas");
        using(var handler=new Handler("",HttpStatusCode.OK,null,true))
        { using var http=new HttpClient(handler); Check(new UpdateChecker(http,publicKey).CheckAsync(UpdateChecker.DefaultFeed,new Version(0,6,0)).GetAwaiter().GetResult().Status==UpdateCheckStatus.Error,"Sin conexión devuelve error controlado"); }
        using(var cts=new CancellationTokenSource())
        { cts.Cancel(); using var handler=new Handler(Sign(empty),HttpStatusCode.OK,null); using var http=new HttpClient(handler); Check(new UpdateChecker(http,publicKey).CheckAsync(UpdateChecker.DefaultFeed,new Version(0,6,0),cts.Token).GetAwaiter().GetResult().Status==UpdateCheckStatus.Error,"Cancelación termina controladamente"); }
        Console.WriteLine($"DETECCIÓN COMPLETA: {count} comprobaciones.");
    }
    private sealed class Handler(string body,HttpStatusCode status,string? redirect,bool fail=false):HttpMessageHandler
    {
        public int Count { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested(); Count++;
            if(fail) throw new HttpRequestException("Sin conexión simulada");
            var response=new HttpResponseMessage(status) { Content=new StringContent(body),RequestMessage=redirect is null?request:new HttpRequestMessage(HttpMethod.Get,redirect) };
            return Task.FromResult(response);
        }
    }
}
