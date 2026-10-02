using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace Monii.Infrastructure;

public sealed record ReleaseManifest(string Version,string Sha256,long Size,string PackageUrl,string Notes);
public sealed record SignedRelease(string Manifest,string Signature);
public sealed record InstalledRelease(string Executable,string Envelope);
public static class UpdatePackages
{
    public static Version Normalize(Version version)=>new(version.Major,version.Minor,Math.Max(0,version.Build),Math.Max(0,version.Revision));
    public static string? LatestExecutable(string root,Version current)
    {
        var pointer=Path.Combine(root,"current-update.json"); if(!File.Exists(pointer)) return null;
        var installation=JsonSerializer.Deserialize<InstalledRelease>(File.ReadAllText(pointer))??throw new InvalidDataException("Referencia de actualización inválida.");
        var release=VerifyManifest(installation.Envelope);
        if(Normalize(Version.Parse(release.Version))<=Normalize(current)) return null;
        var executable=Path.GetFullPath(installation.Executable);
        if(!executable.StartsWith(Path.GetFullPath(root)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||Path.GetFileName(executable)!="Monii.exe"||!File.Exists(executable)) throw new InvalidDataException("No se encuentra la actualización instalada.");
        return executable;
    }
    public const long MaxPackageSize=500_000_000;
    public static string PublicKey()
    {
        using var stream=typeof(UpdatePackages).Assembly.GetManifestResourceStream("Monii.Infrastructure.update-public.pem")??throw new InvalidOperationException("No hay clave pública de actualizaciones en esta compilación.");
        using var reader=new StreamReader(stream); return reader.ReadToEnd();
    }
    public static ReleaseManifest VerifyManifest(string envelope,string? publicKey=null)
    {
        var payload=VerifySignedPayload(envelope,publicKey);
        var release=JsonSerializer.Deserialize<ReleaseManifest>(payload)??throw new InvalidDataException("Versión inválida.");
        ValidateRelease(release);
        return release;
    }
    public static byte[] VerifySignedPayload(string envelope,string? publicKey=null)
    {
        if(envelope.Length>100_000) throw new InvalidDataException("Manifiesto demasiado grande.");
        var signed=JsonSerializer.Deserialize<SignedRelease>(envelope)??throw new InvalidDataException("Manifiesto inválido.");
        if(string.IsNullOrEmpty(signed.Manifest)||string.IsNullOrEmpty(signed.Signature)) throw new InvalidDataException("Falta el contenido o la firma del manifiesto.");
        var payload=Convert.FromBase64String(signed.Manifest); using var rsa=RSA.Create(); rsa.ImportFromPem(publicKey??PublicKey());
        if(!rsa.VerifyData(payload,Convert.FromBase64String(signed.Signature),HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1)) throw new InvalidDataException("Firma de actualización inválida.");
        return payload;
    }
    public static void ValidateRelease(ReleaseManifest release)
    {
        if(!Version.TryParse(release.Version,out var version)||version.Major<0||release.Size<=0||release.Size>MaxPackageSize||release.Sha256 is null||release.Sha256.Length!=64||!release.Sha256.All(Uri.IsHexDigit)||release.Notes is null||release.Notes.Length>10000||release.PackageUrl is null) throw new InvalidDataException("Datos del paquete inválidos.");
        if(release.PackageUrl.Length>0&&(!Uri.TryCreate(release.PackageUrl,UriKind.Absolute,out var url)||url.Scheme!=Uri.UriSchemeHttps)) throw new InvalidDataException("El paquete remoto debe usar HTTPS.");
    }
    public static void VerifyPackage(string zip,ReleaseManifest manifest)
    {
        using var file=File.OpenRead(zip);
        VerifyPackage(file,manifest);
    }
    private static void VerifyPackage(FileStream file,ReleaseManifest manifest)
    {
        if(file.Length!=manifest.Size||!Convert.ToHexString(SHA256.HashData(file)).Equals(manifest.Sha256,StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("El paquete no coincide con su firma y contenido esperado.");
    }
    public static string Install(string zip,string envelope,string root,string? publicKey=null)
    {
        var release=VerifyManifest(envelope,publicKey); using var package=File.OpenRead(zip); VerifyPackage(package,release); package.Position=0;
        root=Path.GetFullPath(root); Directory.CreateDirectory(root);
        var staging=Path.Combine(root,"stage-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(staging);
        var destination=Path.Combine(root,"Monii-"+release.Version+"-"+Guid.NewGuid().ToString("N")[..8]);
        try
        {
            using var archive=new ZipArchive(package,ZipArchiveMode.Read,true);
            var paths=new HashSet<string>(StringComparer.OrdinalIgnoreCase); long expanded=0;
            if(archive.Entries.Count>10000) throw new InvalidDataException("Demasiados archivos.");
            foreach(var entry in archive.Entries)
            {
                var relative=entry.FullName.Replace('\\','/');
                if(relative.Length==0||relative.StartsWith('/')||relative.Contains(':')||relative.Split('/').Any(p=>p is ".." or "."||p.EndsWith(' ')||p.EndsWith('.'))||((entry.ExternalAttributes>>16)&0xF000)==0xA000) throw new InvalidDataException("Ruta de paquete inválida.");
                var target=Path.GetFullPath(Path.Combine(staging,relative));
                if(!target.StartsWith(staging+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||!paths.Add(target)) throw new InvalidDataException("Ruta duplicada o fuera del destino.");
                expanded+=entry.Length; if(expanded>800_000_000) throw new InvalidDataException("Contenido expandido demasiado grande.");
                if(relative.EndsWith('/')) { Directory.CreateDirectory(target); continue; }
                Directory.CreateDirectory(Path.GetDirectoryName(target)!); entry.ExtractToFile(target,false);
            }
            if(!File.Exists(Path.Combine(staging,"Monii.exe"))) throw new InvalidDataException("Falta el ejecutable de Monii.");
            Directory.Move(staging,destination); return Path.Combine(destination,"Monii.exe");
        }
        finally { if(Directory.Exists(staging)) Directory.Delete(staging,true); }
    }
    public static async Task<string> Download(HttpClient client,string url,string destination,long maximum,CancellationToken cancellation=default)
    {
        if(!Uri.TryCreate(url,UriKind.Absolute,out var uri)||uri.Scheme!=Uri.UriSchemeHttps) throw new ArgumentException("Usa una dirección HTTPS.");
        using var response=await client.GetAsync(uri,HttpCompletionOption.ResponseHeadersRead,cancellation); response.EnsureSuccessStatusCode();
        if(response.RequestMessage?.RequestUri?.Scheme!=Uri.UriSchemeHttps||response.Content.Headers.ContentLength>maximum) throw new InvalidDataException("Descarga no válida.");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destination))!);
        try
        {
            await using var source=await response.Content.ReadAsStreamAsync(cancellation); await using var target=new FileStream(destination,FileMode.CreateNew,FileAccess.Write,FileShare.None);
            var buffer=new byte[65536]; long total=0; int read;
            while((read=await source.ReadAsync(buffer,cancellation))>0) { total+=read; if(total>maximum) throw new InvalidDataException("Descarga excede el tamaño permitido."); await target.WriteAsync(buffer.AsMemory(0,read),cancellation); }
            return destination;
        }
        catch { if(File.Exists(destination)) File.Delete(destination); throw; }
    }
}
