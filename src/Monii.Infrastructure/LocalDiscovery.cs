using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;
namespace Monii.Infrastructure;
public sealed record DiscoveredServer(string Name,string Address,string Fingerprint)
{
    public string Display => Name+" · "+new Uri(Address).Host+" · Código "+LocalDiscovery.PairingCode(Fingerprint);
}
public sealed record DiscoveryQuery(string Protocol,Guid Nonce);
public sealed record DiscoveryReply(string Protocol,Guid Nonce,string Name,int Port,string Fingerprint);
public static class LocalDiscovery
{
    public const int Port=58444;
    public const string Protocol="Monii-LAN-1";
    public static bool LocalAddress(IPAddress address)
    {
        if(address.AddressFamily!=AddressFamily.InterNetwork)return false;
        var b=address.GetAddressBytes();return IPAddress.IsLoopback(address)||b[0]==10||b[0]==192&&b[1]==168||b[0]==172&&b[1]>=16&&b[1]<=31||b[0]==169&&b[1]==254;
    }
    public static string PairingCode(string fingerprint) => fingerprint.Length==64?string.Join("-",Enumerable.Range(0,3).Select(i=>fingerprint.Substring(i*4,4).ToUpperInvariant())):"No disponible";
    public static DiscoveredServer? ParseReply(byte[] data,IPEndPoint source,Guid nonce)
    {
        if(data.Length>1024||!LocalAddress(source.Address))return null;
        try {
            var reply=JsonSerializer.Deserialize<DiscoveryReply>(data);
            if(reply is null||reply.Protocol!=Protocol||reply.Nonce!=nonce||string.IsNullOrWhiteSpace(reply.Name)||reply.Name.Length>80||reply.Name.Any(char.IsControl)||reply.Port<1||reply.Port>65535||reply.Fingerprint is null||reply.Fingerprint.Length!=64||!reply.Fingerprint.All(Uri.IsHexDigit))return null;
            return new(reply.Name,$"https://{source.Address}:{reply.Port}",reply.Fingerprint.ToUpperInvariant());
        } catch(JsonException) { return null; }
    }
    public static async Task<IReadOnlyList<DiscoveredServer>> FindAsync(CancellationToken cancellation=default,IPEndPoint? target=null)
    {
        using var udp=new UdpClient(AddressFamily.InterNetwork);udp.EnableBroadcast=true;udp.Client.Bind(new IPEndPoint(IPAddress.Any,0));
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancellation);timeout.CancelAfter(TimeSpan.FromSeconds(3));
        var nonce=Guid.NewGuid();var query=JsonSerializer.SerializeToUtf8Bytes(new DiscoveryQuery(Protocol,nonce));
        var destinations=new HashSet<IPAddress> { IPAddress.Broadcast };
        if(target is null)foreach(var network in NetworkInterface.GetAllNetworkInterfaces().Where(n=>n.OperationalStatus==OperationalStatus.Up))foreach(var ip in network.GetIPProperties().UnicastAddresses.Where(i=>LocalAddress(i.Address)&&!IPAddress.IsLoopback(i.Address)))
        { var address=ip.Address.GetAddressBytes();var mask=ip.IPv4Mask.GetAddressBytes();destinations.Add(new IPAddress(address.Select((b,i)=>(byte)(b|~mask[i])).ToArray())); }
        foreach(var endpoint in target is null?destinations.Select(ip=>new IPEndPoint(ip,Port)):new[]{target})
        { try { await udp.SendAsync(query,endpoint,timeout.Token); }catch(SocketException) { } }
        var servers=new Dictionary<string,DiscoveredServer>();
        try {
            while(servers.Count<16) { var packet=await udp.ReceiveAsync(timeout.Token);var server=ParseReply(packet.Buffer,packet.RemoteEndPoint,nonce);if(server is not null)servers[server.Address]=server; }
        } catch(OperationCanceledException) when(!cancellation.IsCancellationRequested) { }
        cancellation.ThrowIfCancellationRequested();return servers.Values.OrderBy(s=>s.Name).ToList();
    }
}
