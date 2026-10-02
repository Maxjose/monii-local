using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Monii.Infrastructure;

namespace Monii.Desktop;
public partial class MainWindow
{
    private void VerifyUpdateCheckUi(string output,List<string> log)
    {
        void Assert(bool okay,string label) { if(!okay) throw new InvalidOperationException("UI detección: "+label); log.Add("OK DETECCIÓN UI: "+label); }
        void Click(string label)=>Descendants(PageContent).OfType<Button>().Single(b=>b.Content?.ToString()==label).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        void Capture(string name)
        {
            UpdateLayout(); Dispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle); UpdateLayout(); var root=(FrameworkElement)Content;
            var image=new RenderTargetBitmap((int)root.ActualWidth,(int)root.ActualHeight,96,96,PixelFormats.Pbgra32); image.Render(root); var encoder=new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); using var stream=File.Create(Path.Combine(output,name)); encoder.Save(stream);
        }
        using var rsa=RSA.Create(2048);
        string Sign(UpdateCatalog catalog) { var payload=JsonSerializer.SerializeToUtf8Bytes(catalog); return JsonSerializer.Serialize(new SignedRelease(Convert.ToBase64String(payload),Convert.ToBase64String(rsa.SignData(payload,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1)))); }
        var original=service.Settings; var checker=updateChecker;
        Navigate("Actualizaciones"); UpdateLayout();
        Assert(!Descendants(PageContent).OfType<TextBox>().Any(),"Actualizaciones no muestra URL ni campos técnicos");
        Assert(Descendants(PageContent).OfType<Button>().Where(b=>b.Visibility==Visibility.Visible).All(b=>b.Content?.ToString()=="Buscar actualizaciones"),"Solo se muestra el botón para comprobar cuando no hay actualización");
        var automatic=Descendants(PageContent).OfType<CheckBox>().Single();
        Assert(automatic.Content?.ToString()=="Comprobar actualizaciones al abrir Monii","Solo ofrece comprobación al abrir, sin intervalos periódicos");
        automatic.IsChecked=false;
        Assert(!service.Settings.AutoCheckUpdates,"Desactivar comprobación al abrir se guarda automáticamente");
        var catalog=new UpdateCatalog(1,"Monii","stable","win-x64",null);
        using var handler=new UpdateUiHandler(Sign(catalog)); using var client=new HttpClient(handler);
        updateChecker=new(client,rsa.ExportSubjectPublicKeyInfoPem());
        Click("Buscar actualizaciones"); UpdateLayout();
        Assert(latestUpdateCheck?.Status==UpdateCheckStatus.NoReleases&&updateDetails!.Text.Contains("Todavía no hay actualizaciones"),"Botón consulta aunque la comprobación al abrir esté desactivada"); Capture("actualizaciones-sin-versiones.png");
        var available=new ReleaseManifest("9.0.0",new string('A',64),123,"https://github.com/Maxjose/monii-local/releases/download/demo/not-real.zip","VERSIÓN DE PRUEBA UI · No se descarga.");
        handler.Body=Sign(catalog with { Latest=available }); Click("Buscar actualizaciones"); UpdateLayout();
        Assert(UpdateNotice.Visibility==Visibility.Visible&&updateDetails!.Text.Contains("9.0.0"),"Versión disponible muestra aviso y notas");
        Assert(handler.Count==2&&!Descendants(PageContent).OfType<Button>().Any(b=>b.Content?.ToString() is "Aplicar actualización y reiniciar" or "Consultar y descargar"),"No existen controles de descarga o instalación"); Capture("actualizacion-disponible-demo.png");
        Assert(updateInstallButton!.Visibility==Visibility.Visible&&!updateInstallButton.IsEnabled,"Botón Actualizar programa preparado sin habilitar instalación");
        Assert(!Descendants(PageContent).OfType<TextBlock>().Any(t=>t.Text.Contains("https://")||t.Text.Contains("manifiesto",StringComparison.OrdinalIgnoreCase)),"Información de actualización no expone direcciones internas");
        handler.Body=""; handler.Status=HttpStatusCode.NotFound; Click("Buscar actualizaciones"); UpdateLayout();
        Assert(latestUpdateCheck?.Status==UpdateCheckStatus.NotPublished&&UpdateNotice.Visibility==Visibility.Collapsed&&updateDetails!.Text.Contains("todavía no está disponible"),"404 se muestra como pendiente, sin anuncio falso"); Capture("actualizaciones-no-publicado.png");
        handler.Status=HttpStatusCode.OK; handler.Body="{}"; Click("Buscar actualizaciones"); UpdateLayout();
        Assert(latestUpdateCheck?.Status==UpdateCheckStatus.Error&&updateCheckButton!.IsEnabled,"Firma inválida muestra error y permite reintentar");
        latestUpdateCheck=new(UpdateCheckStatus.Error,DateTimeOffset.UtcNow,"Error en https://raw.githubusercontent.com/privado"); ShowUpdateResult();
        Assert(!updateDetails!.Text.Contains("https://")&&!Status.Text.Contains("https://"),"Los errores de consulta tampoco muestran URLs");
        service.SaveSettings(original); updateChecker=checker; latestUpdateCheck=null; checkedUpdateFeed=""; ShowUpdateResult(); Navigate("Inicio");
    }
    private sealed class UpdateUiHandler(string body):HttpMessageHandler
    {
        public string Body { get; set; }=body;
        public HttpStatusCode Status { get; set; }=HttpStatusCode.OK;
        public int Count { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)
        { Count++; return Task.FromResult(new HttpResponseMessage(Status) { RequestMessage=request,Content=new StringContent(Body) }); }
    }
}
