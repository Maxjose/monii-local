using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using Monii.Application;
using Monii.Domain;
using Monii.Infrastructure;
using Monii.Server;
using System.Text.Json;
using System.Security.Cryptography;

internal static class LegacyPasswordChecks
{
    public static async Task Run(string executable)
    {
        executable=Path.GetFullPath(executable);
        if(!File.Exists(executable))throw new FileNotFoundException("Indica un runtime anterior de servidor.");
        var root=Path.GetFullPath(Path.Combine("artifacts","legacy-password-"+Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(root);var data=Path.Combine(root,"server");
        var local=new SqliteStore(Path.Combine(root,"source.db"));local.SaveUser(null,"admin","Administrador",UserRole.Administrador,true,"12345678");
        local.Authenticate("admin","12345678");var user=local.Users().Single();
        ServerHost.Prepare(data,local.Backup(Path.Combine(root,"initial.db")));
        var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();var port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();
        File.WriteAllText(Path.Combine(data,"server.json"),JsonSerializer.Serialize(new ServerConfiguration(port,0)));
        var config=ConnectionSettings.Load(root) with { Address=$"https://localhost:{port}" };
        var start=new ProcessStartInfo(executable) { UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true };
        start.ArgumentList.Add("--data-dir");start.ArgumentList.Add(data);
        using var process=Process.Start(start)!;var output=process.StandardOutput.ReadToEndAsync();var errors=process.StandardError.ReadToEndAsync();
        var count=0;void Check(bool ok,string message){if(!ok)throw new Exception("FALLO LEGACY: "+message);count++;Console.WriteLine("OK LEGACY: "+message);}
        try
        {
            var ready=false;
            for(var attempt=0;attempt<60&&!ready;attempt++)
            {
                if(process.HasExited)throw new Exception("El servidor anterior no inició.");
                try { using var socket=new TcpClient();await socket.ConnectAsync(IPAddress.Loopback,port);ready=true; }catch(SocketException){await Task.Delay(100);}
            }
            Check(ready,"Runtime anterior iniciado con base aislada");
            using var handler=new HttpClientHandler { UseProxy=false,ServerCertificateCustomValidationCallback=(_,cert,_,_)=>cert is not null&&Convert.ToHexString(SHA256.HashData(cert.RawData))==config.Fingerprint };
            using var raw=new HttpClient(handler) { BaseAddress=new Uri(config.Address) };
            var login=await raw.PostAsJsonAsync("api/login",new LoginRequest("admin","12345678",Guid.NewGuid(),"Legacy"),NetworkJson.Options);login.EnsureSuccessStatusCode();
            var reply=(await login.Content.ReadFromJsonAsync<LoginReply>(NetworkJson.Options))!;raw.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",reply.Token);
            var saved=await raw.PostAsJsonAsync("api/write",NetworkJson.Command("User",new { id=user.Id,username=user.Username,name=user.Name,role=user.Role,active=true,password="abcdefgh" }),NetworkJson.Options);
            Check(saved.IsSuccessStatusCode,"Servidor anterior guarda contraseña");
            var reload=await raw.PostAsJsonAsync("api/read",NetworkJson.Command("Users",new{}),NetworkJson.Options);
            Check(reload.StatusCode==HttpStatusCode.Unauthorized,"Reproduce error original: recarga rechazada después del cambio confirmado");
            using var client=new RemoteStore(config,Path.Combine(root,"client"));client.Authenticate("admin","abcdefgh");
            client.SaveUser(user.Id,user.Username,user.Name,user.Role,true,"87654321");
            Check(client.Users().Any(u=>u.Id==user.Id),"Cliente corregido renueva sesión y recarga usuarios contra servidor anterior");
            using var fresh=new RemoteStore(config,Path.Combine(root,"fresh"));fresh.Authenticate("admin","87654321");
            Check(fresh.CurrentUser is not null,"Contraseña nueva permite un acceso independiente");
            Console.WriteLine($"LEGACY COMPLETA: {count} comprobaciones. Datos aislados: {root}");
        }
        finally
        {
            if(!process.HasExited)process.Kill(entireProcessTree:true);await process.WaitForExitAsync();
            File.WriteAllText(Path.Combine(root,"server.log"),await output+await errors);
        }
    }
}
