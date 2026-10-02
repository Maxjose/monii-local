using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Monii.Domain;

namespace Monii.Infrastructure;

public sealed class ExchangeRates(Monii.Application.IMoniiStore store,HttpClient? httpClient=null)
{
    private static readonly HttpClient Shared = new() { Timeout=TimeSpan.FromSeconds(15) };
    private readonly HttpClient client=httpClient??Shared;
    public static ExchangeQuote ParseBcv(string html,DateOnly today)
    {
        var block=Regex.Match(html,"id\\s*=\\s*['\"]dolar['\"][\\s\\S]{0,3000}?<strong[^>]*>(?<value>[^<]+)</strong>",RegexOptions.IgnoreCase,TimeSpan.FromSeconds(1));
        var date=Regex.Match(html,"class\\s*=\\s*['\"][^'\"]*date-display-single[^'\"]*['\"][^>]*content\\s*=\\s*['\"](?<date>\\d{4}-\\d{2}-\\d{2})",RegexOptions.IgnoreCase,TimeSpan.FromSeconds(1));
        if(!date.Success) date=Regex.Match(html,"content\\s*=\\s*['\"](?<date>\\d{4}-\\d{2}-\\d{2})[^'\"]*['\"][^>]*class\\s*=\\s*['\"][^'\"]*date-display-single",RegexOptions.IgnoreCase,TimeSpan.FromSeconds(1));
        if(!Regex.IsMatch(WebUtility.HtmlDecode(block.Groups["value"].Value).Trim(),@"^\d+(?:\.\d{3})*,\d{1,8}$",RegexOptions.None,TimeSpan.FromSeconds(1))) throw new InvalidDataException("Formato de tasa BCV inesperado.");
        if(!block.Success||!date.Success||!decimal.TryParse(WebUtility.HtmlDecode(block.Groups["value"].Value).Trim().Replace(".","").Replace(',','.'),NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out var value)) throw new InvalidDataException("No se pudo identificar tasa y vigencia en la página BCV.");
        var effective=DateOnly.ParseExact(date.Groups["date"].Value,"yyyy-MM-dd",CultureInfo.InvariantCulture);
        var quote=new ExchangeQuote(value,effective,effective,"https://www.bcv.org.ve/"); Validate(quote,today,false); return quote;
    }
    public static ExchangeQuote ParseCop(string json,DateOnly today)
    {
        using var document=JsonDocument.Parse(json); var rows=document.RootElement.EnumerateArray();
        foreach(var row in rows)
        {
            var start=DateOnly.ParseExact(row.GetProperty("vigenciadesde").GetString()![..10],"yyyy-MM-dd",CultureInfo.InvariantCulture);
            var until=DateOnly.ParseExact(row.GetProperty("vigenciahasta").GetString()![..10],"yyyy-MM-dd",CultureInfo.InvariantCulture);
            if(start>today||until<today) continue;
            var quote=new ExchangeQuote(decimal.Parse(row.GetProperty("valor").GetString()!,CultureInfo.InvariantCulture),start,until,"https://www.datos.gov.co/resource/32sa-8pi3.json"); Validate(quote,today,true); return quote;
        }
        throw new InvalidDataException("No se encontró TRM vigente para hoy.");
    }
    private static void Validate(ExchangeQuote quote,DateOnly today,bool exact)
    {
        if(quote.Value<=0||quote.Value>1000000000m||decimal.Round(quote.Value,8)!=quote.Value||quote.EffectiveDate>today||quote.EffectiveDate<today.AddDays(-7)||(exact&&quote.Until<today)) throw new InvalidDataException("Tasa inválida o fuera de vigencia; se conserva la última disponible.");
    }
    public async Task<ExchangeRefresh> RefreshAsync(bool force=false,CancellationToken cancellation=default)
    {
        var settings=store.GetSettings(); var today=DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow,TimeZoneInfo.FindSystemTimeZoneById("America/Caracas")).DateTime);
        async Task<(ExchangeQuote? Quote,string Error)> Fetch(bool enabled,bool bcv)
        {
            if(!enabled) return(null,"");
            try
            {
                var url=bcv?"https://www.bcv.org.ve/":"https://www.datos.gov.co/resource/32sa-8pi3.json?$limit=10&$order=vigenciadesde%20DESC";
                using var response=await client.GetAsync(url,HttpCompletionOption.ResponseHeadersRead,cancellation); response.EnsureSuccessStatusCode();
                if(response.RequestMessage?.RequestUri is { } final&&final.Scheme!=Uri.UriSchemeHttps) throw new InvalidDataException("La fuente no respondió por HTTPS.");
                if(response.Content.Headers.ContentLength>2000000) throw new InvalidDataException("Respuesta demasiado grande.");
                await using var stream=await response.Content.ReadAsStreamAsync(cancellation); using var memory=new MemoryStream(); var buffer=new byte[8192]; int read;
                while((read=await stream.ReadAsync(buffer,cancellation))>0) { if(memory.Length+read>2000000) throw new InvalidDataException("Respuesta demasiado grande."); memory.Write(buffer,0,read); }
                var body=System.Text.Encoding.UTF8.GetString(memory.ToArray()); return(bcv?ParseBcv(body,today):ParseCop(body,today),"");
            }
            catch(Exception e) when(e is HttpRequestException or OperationCanceledException or InvalidDataException or JsonException or FormatException or KeyNotFoundException or ArgumentOutOfRangeException or InvalidOperationException or RegexMatchTimeoutException) { return(null,(bcv?"BCV":"COP")+": "+e.Message); }
        }
        var bcvTask=Fetch(settings.ShowBcv&&(settings.AutoBcv||force),true); var copTask=Fetch(settings.ShowCop&&(settings.AutoCop||force),false);
        await Task.WhenAll(bcvTask,copTask); var bcv=await bcvTask; var cop=await copTask;
        store.ApplyOfficialRates(bcv.Quote,cop.Quote);
        var errors=string.Join(" · ",new[]{bcv.Error,cop.Error}.Where(s=>s.Length>0));
        return new(bcv.Quote is not null,cop.Quote is not null,errors.Length>0?errors:"Tasas consultadas. La tasa manual de bolívares se conserva.");
    }
}

public sealed partial class SqliteStore
{
    public void ApplyOfficialRates(ExchangeQuote? bcv,ExchangeQuote? cop)
    {
        if(bcv is null&&cop is null) return;
        if(new[]{bcv,cop}.Any(q=>q is not null&&(q.Value<=0||q.Value>1000000000m||decimal.Round(q.Value,8)!=q.Value||q.Until<q.EffectiveDate))) throw new InvalidDataException("Tasa oficial inválida.");
        using var connection=Open(); using var tx=connection.BeginTransaction(deferred:false); using var cmd=connection.CreateCommand(); cmd.Transaction=tx;
        cmd.CommandText="SELECT payload FROM settings WHERE id=1";
        var settings=cmd.ExecuteScalar() is string payload?JsonSerializer.Deserialize<BusinessSettings>(payload)!:new();
        settings=settings with { BcvRate=bcv?.Value??settings.BcvRate,CopRate=cop?.Value??settings.CopRate,BcvEffectiveDate=bcv?.EffectiveDate??settings.BcvEffectiveDate,CopEffectiveDate=cop?.EffectiveDate??settings.CopEffectiveDate,BcvSource=bcv?.Source??settings.BcvSource,CopSource=cop?.Source??settings.CopSource,RatesUpdatedAt=DateTimeOffset.UtcNow };
        cmd.CommandText="INSERT INTO settings(id,payload) VALUES(1,$payload) ON CONFLICT(id) DO UPDATE SET payload=$payload"; cmd.Parameters.AddWithValue("$payload",JsonSerializer.Serialize(settings)); cmd.ExecuteNonQuery();
        cmd.Parameters.Clear(); cmd.CommandText="INSERT INTO audit(at,action,details,actor) VALUES($at,'Tasas oficiales actualizadas',$details,'Sistema')"; cmd.Parameters.AddWithValue("$at",DateTimeOffset.UtcNow.ToString("O")); cmd.Parameters.AddWithValue("$details",JsonSerializer.Serialize(new { Bcv=bcv,Cop=cop })); cmd.ExecuteNonQuery(); tx.Commit();
    }
}
