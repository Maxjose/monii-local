using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Monii.Infrastructure;

try
{
    if(args.Length==3&&args[0]=="keygen")
    {
        if(File.Exists(args[1])||File.Exists(args[2])) throw new InvalidOperationException("La clave existente se conserva; no la reemplaces entre versiones.");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[1]))!);
        using var rsa=RSA.Create(3072); File.WriteAllText(args[1],rsa.ExportRSAPrivateKeyPem()); File.WriteAllText(args[2],rsa.ExportSubjectPublicKeyInfoPem());
        Console.WriteLine("Clave privada creada. Resguárdala fuera de los paquetes de entrega.");
    }
    else if(args.Length==4&&args[0]=="catalog")
    {
        var catalog=JsonSerializer.Deserialize<UpdateCatalog>(File.ReadAllText(args[1]))??throw new InvalidDataException("Catálogo inválido.");
        var payload=JsonSerializer.SerializeToUtf8Bytes(catalog); using var rsa=RSA.Create(); rsa.ImportFromPem(File.ReadAllText(args[2]));
        var envelope=JsonSerializer.Serialize(new SignedRelease(Convert.ToBase64String(payload),Convert.ToBase64String(rsa.SignData(payload,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1))));
        UpdateChecker.VerifyCatalog(envelope,rsa.ExportSubjectPublicKeyInfoPem());
        if(File.Exists(args[3])) throw new InvalidOperationException("El manifiesto ya existe; consérvalo o usa otro destino.");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[3]))!); File.WriteAllText(args[3],envelope); Console.WriteLine(args[3]);
    }
    else if(args.Length>=6&&args[0]=="pack")
    {
        var actual=FileVersionInfo.GetVersionInfo(Path.Combine(args[1],"Monii.exe")).FileVersion;
        if(!Version.TryParse(actual,out var actualVersion)||!Version.TryParse(args[4],out var requested)||UpdatePackages.Normalize(actualVersion)!=UpdatePackages.Normalize(requested)) throw new InvalidDataException("El número de versión debe coincidir con el ejecutable compilado.");
        var zip=Path.GetFullPath(args[3]); ZipFile.CreateFromDirectory(args[1],zip,CompressionLevel.Optimal,false);
        using var file=File.OpenRead(zip); var release=new ReleaseManifest(args[4],Convert.ToHexString(SHA256.HashData(file)),file.Length,args.Length>6?args[6]:"",args[5]);
        var payload=JsonSerializer.SerializeToUtf8Bytes(release); using var rsa=RSA.Create(); rsa.ImportFromPem(File.ReadAllText(args[2]));
        File.WriteAllText(zip+".json",JsonSerializer.Serialize(new SignedRelease(Convert.ToBase64String(payload),Convert.ToBase64String(rsa.SignData(payload,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1)))));
        Console.WriteLine(zip+".json");
    }
    else if(args.Length==7&&args[0]=="apply")
    {
        if(int.TryParse(args[5],out var pid))
        {
            try { using var process=Process.GetProcessById(pid); if(!process.WaitForExit(60000)) throw new InvalidOperationException("Monii continúa abierto. La actualización se detuvo."); }
            catch(ArgumentException) { }
        }
        var envelope=File.ReadAllText(args[2]); var release=UpdatePackages.VerifyManifest(envelope);
        var executable=UpdatePackages.Install(args[1],envelope,args[3]);
        var actual=FileVersionInfo.GetVersionInfo(executable).FileVersion;
        if(!Version.TryParse(actual,out var actualVersion)||UpdatePackages.Normalize(actualVersion)!=UpdatePackages.Normalize(Version.Parse(release.Version))) throw new InvalidDataException("El ejecutable no corresponde a la versión firmada.");
        var pointer=Path.Combine(args[3],"current-update.json"); var temporary=pointer+"."+Guid.NewGuid().ToString("N")+".tmp";
        File.WriteAllText(temporary,JsonSerializer.Serialize(new InstalledRelease(executable,envelope))); File.Move(temporary,pointer,true);
        File.WriteAllText(Path.Combine(args[3],"ultima-instalacion.txt"),executable);
        File.WriteAllText(Path.Combine(args[3],"resultado-actualizacion.txt"),"Instalada: "+executable);
        var start=new ProcessStartInfo(executable) { UseShellExecute=false }; start.ArgumentList.Add("--data-dir"); start.ArgumentList.Add(args[4]);
        if(args[6]=="launch") Process.Start(start);
        Console.WriteLine(executable);
    }
    else throw new ArgumentException("Uso: catalog catalogo.json privada.pem salida.json | keygen privada.pem publica.pem | pack carpeta privada.pem paquete.zip version notas [urlHTTPS] | apply paquete.zip manifiesto.json destino datos pid launch|no-launch");
}
catch(Exception e)
{
    Console.Error.WriteLine(e.Message); Environment.ExitCode=1;
    if(args.Length==7&&args[0]=="apply")
        try { Directory.CreateDirectory(args[3]); File.WriteAllText(Path.Combine(args[3],"resultado-actualizacion.txt"),"ERROR: "+e.Message+"\nLa versión anterior y los datos se conservan."); } catch(IOException) { } catch(UnauthorizedAccessException) { }
}
