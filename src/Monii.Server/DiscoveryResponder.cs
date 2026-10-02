using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Monii.Infrastructure;
namespace Monii.Server;
public sealed class DiscoveryResponder(IHostApplicationLifetime lifetime,IServer server,ServerConfiguration configuration,string fingerprint,ILogger<DiscoveryResponder> logger) : BackgroundService
{
    public int BoundPort { get; private set; }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var started=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration=lifetime.ApplicationStarted.Register(()=>started.TrySetResult());
        await started.Task.WaitAsync(stoppingToken);
        try {
            using var udp=new UdpClient(new IPEndPoint(IPAddress.Any,configuration.DiscoveryPort));BoundPort=((IPEndPoint)udp.Client.LocalEndPoint!).Port;
            while(!stoppingToken.IsCancellationRequested)
            {
                var packet=await udp.ReceiveAsync(stoppingToken);
                if(packet.Buffer.Length>256||!LocalDiscovery.LocalAddress(packet.RemoteEndPoint.Address))continue;
                DiscoveryQuery? query;try { query=JsonSerializer.Deserialize<DiscoveryQuery>(packet.Buffer); }catch(JsonException) { continue; }
                if(query is null||query.Protocol!=LocalDiscovery.Protocol||query.Nonce==Guid.Empty)continue;
                var port=configuration.Port>0?configuration.Port:new Uri(server.Features.Get<IServerAddressesFeature>()!.Addresses.Single()).Port;
                var reply=JsonSerializer.SerializeToUtf8Bytes(new DiscoveryReply(LocalDiscovery.Protocol,query.Nonce,Environment.MachineName,port,fingerprint));
                await udp.SendAsync(reply,packet.RemoteEndPoint,stoppingToken);
            }
        } catch(OperationCanceledException) when(stoppingToken.IsCancellationRequested) { }
        catch(SocketException error) { logger.LogWarning(error,"Búsqueda automática no disponible. El servidor HTTPS sigue activo."); }
    }
}
