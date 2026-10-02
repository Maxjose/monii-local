using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Monii.Domain;
using Monii.Infrastructure;

namespace Monii.Desktop;
public partial class MainWindow
{
    private bool checkingUpdates;
    private UpdateChecker updateChecker=new();
    private UpdateCheckResult? latestUpdateCheck;
    private string checkedUpdateFeed="";
    private TextBlock? updateDetails;
    private Button? updateCheckButton;
    private Button? updateInstallButton;
    private readonly DispatcherTimer updateTimer=new() { Interval=TimeSpan.FromHours(6) };
    private readonly CancellationTokenSource updateCancellation=new();
    internal void StartUpdateChecks()
    {
        Loaded+=async(_,_)=> { if(service.Settings.AutoCheckUpdates) await CheckUpdates(false); };
        updateTimer.Tick+=async(_,_)=> { if(service.Settings.AutoCheckUpdates) await CheckUpdates(false); };
        updateTimer.Start();
        Closed+=(_,_)=>updateCancellation.Cancel();
    }
    private string UpdateFeed()=>string.IsNullOrWhiteSpace(service.Settings.UpdateFeedUrl)?UpdateChecker.DefaultFeed:service.Settings.UpdateFeedUrl;
    private void ShowUpdateResult()
    {
        if(updateCheckButton is not null) updateCheckButton.IsEnabled=!checkingUpdates;
        if(updateDetails is not null)
        {
            if(checkingUpdates) updateDetails.Text="Buscando actualizaciones…";
            else if(latestUpdateCheck is { } result&&checkedUpdateFeed==UpdateFeed())
            {
                updateDetails.Text=UpdateSummary(result)+"\nÚltima comprobación: "+result.At.ToLocalTime().ToString("dd/MM/yyyy HH:mm");
                if(result.Status==UpdateCheckStatus.Available&&result.Latest is { } release) updateDetails.Text+="\n"+release.Notes+"\nLa instalación todavía no está habilitada.";
            }
            else updateDetails.Text="Pulsa Buscar actualizaciones para comprobar si hay una nueva versión.";
        }
        if(updateInstallButton is not null) updateInstallButton.Visibility=latestUpdateCheck?.Status==UpdateCheckStatus.Available&&checkedUpdateFeed==UpdateFeed()?Visibility.Visible:Visibility.Collapsed;
        UpdateNotice.Visibility=latestUpdateCheck?.Status==UpdateCheckStatus.Available&&checkedUpdateFeed==UpdateFeed()?Visibility.Visible:Visibility.Collapsed;
        if(UpdateNotice.Visibility==Visibility.Visible) UpdateNotice.Text="Disponible Monii "+latestUpdateCheck!.Latest!.Version+" · Revisa Configuración → Actualizaciones.";
    }
    private static string UpdateSummary(UpdateCheckResult result)=>result.Status switch
    {
        UpdateCheckStatus.Available=>"Hay una actualización disponible: Monii "+result.Latest?.Version,
        UpdateCheckStatus.Current=>"Monii está actualizado.",
        UpdateCheckStatus.NoReleases=>"Todavía no hay actualizaciones publicadas.",
        UpdateCheckStatus.NotPublished=>"La información de actualizaciones todavía no está disponible. Inténtalo más tarde.",
        _=>"No se pudo comprobar si hay actualizaciones. Revisa tu conexión e inténtalo de nuevo."
    };
    private async Task CheckUpdates(bool manual)
    {
        if(checkingUpdates) return;
        if(manual) storage.Require(Permission.Updates);
        var source=UpdateFeed(); checkingUpdates=true; ShowUpdateResult();
        try
        {
            var result=await updateChecker.CheckAsync(source,typeof(MainWindow).Assembly.GetName().Version!,updateCancellation.Token);
            if(updateCancellation.IsCancellationRequested||source!=UpdateFeed()) return;
            latestUpdateCheck=result; checkedUpdateFeed=source;
            if(manual||result.Status==UpdateCheckStatus.Available) Status.Text=UpdateSummary(result);
        }
        catch(Exception) { latestUpdateCheck=new(UpdateCheckStatus.Error,DateTimeOffset.UtcNow,"No se pudo comprobar la actualización."); checkedUpdateFeed=source; }
        finally { checkingUpdates=false; ShowUpdateResult(); }
    }
    private UIElement BuildUpdatesPage()
    {
        var panel=new StackPanel { MaxWidth=840 };
        panel.Children.Add(Text("Actualizaciones de Monii",24));
        panel.Children.Add(Text("Versión instalada: "+typeof(MainWindow).Assembly.GetName().Version!.ToString(3)));
        panel.Children.Add(Text("Comprueba si hay una nueva versión de Monii.",14,"#64748B"));
        var automatic=Check(panel,"Comprobar actualizaciones al abrir Monii",service.Settings.AutoCheckUpdates);
        void SaveAutomatic()=>Safe(()=> { service.SaveSettings(service.Settings with { AutoCheckUpdates=automatic.IsChecked==true }); Status.Text="Preferencia de actualización guardada."; });
        automatic.Checked+=(_,_)=>SaveAutomatic(); automatic.Unchecked+=(_,_)=>SaveAutomatic();
        updateDetails=Text("",15); panel.Children.Add(updateDetails);
        updateCheckButton=Button("Buscar actualizaciones",async()=> { try { await CheckUpdates(true); } catch(Exception) { updateDetails.Text="No se pudo comprobar la actualización. Inténtalo de nuevo."; } });
        updateInstallButton=Button("Actualizar programa",()=>{}); updateInstallButton.IsEnabled=false;
        var actions=new WrapPanel(); actions.Children.Add(updateCheckButton); actions.Children.Add(updateInstallButton); panel.Children.Add(actions);
        ShowUpdateResult(); return Scroll(panel);
    }
}
