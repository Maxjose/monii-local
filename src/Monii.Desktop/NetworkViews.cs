using System.IO;
using System.Text.Json;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Monii.Application;
using Monii.Domain;
using Monii.Infrastructure;
namespace Monii.Desktop;

public sealed class ConnectionWindow : Window
{
    public ConnectionWindow(string directory,Func<CancellationToken,Task<IReadOnlyList<DiscoveredServer>>>? finder=null)
    {
        Title="Monii · Conexión";Width=560;Height=620;WindowStartupLocation=WindowStartupLocation.CenterScreen;
        var existing=ConnectionSettings.Load(directory);var lifetime=new CancellationTokenSource();Closed+=(_,_)=> { lifetime.Cancel();lifetime.Dispose(); };
        var panel=new StackPanel { Margin=new Thickness(28) };Content=new ScrollViewer { Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto };
        panel.Children.Add(new TextBlock { Text="¿Cómo usarás Monii en este equipo?",FontSize=23,TextWrapping=TextWrapping.Wrap });
        panel.Children.Add(new TextBlock { Text="Trabaja en este equipo o busca el principal de tu negocio en la red local.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,12,0,12) });
        var mode=new ComboBox { ItemsSource=new[]{"Una computadora","Conectar al equipo principal"},SelectedIndex=existing.Mode=="Local"?0:1 };panel.Children.Add(mode);
        TextBox Field(string label,string value,Panel? target=null) { target??=panel;target.Children.Add(new TextBlock { Text=label,Margin=new Thickness(0,12,0,6) });var input=new TextBox { Text=value };target.Children.Add(input);return input; }
        var name=Field("Nombre de este equipo",existing.TerminalName);
        var connectionFields=new StackPanel();panel.Children.Add(connectionFields);
        connectionFields.Children.Add(new TextBlock { Text="Equipo principal",Margin=new Thickness(0,16,0,8) });
        var servers=new ComboBox { DisplayMemberPath="Display" };connectionFields.Children.Add(servers);
        var find=new Button { Content="Buscar servidor en la red" };connectionFields.Children.Add(find);
        var discoveryStatus=new TextBlock { TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,8) };connectionFields.Children.Add(discoveryStatus);
        var manual=new StackPanel();var advanced=new Expander { Header="Conexión manual (si no aparece el servidor)",Content=manual,Margin=new Thickness(0,12,0,12) };connectionFields.Children.Add(advanced);
        var address=Field("Dirección del principal",existing.Address,manual);var fingerprint=Field("Huella de conexión",existing.Fingerprint,manual);
        servers.SelectionChanged+=(_,_)=> { if(servers.SelectedItem is DiscoveredServer server) { address.Text=server.Address;fingerprint.Text=server.Fingerprint; } };
        var searching=false;
        async Task Search()
        {
            if(searching)return;searching=true;find.IsEnabled=false;find.Content="Buscando servidor…";discoveryStatus.Text="Buscando equipos principales en esta red…";
            try {
                var found=await (finder?.Invoke(lifetime.Token)??LocalDiscovery.FindAsync(lifetime.Token));servers.ItemsSource=found;
                servers.SelectedItem=found.FirstOrDefault(s=>s.Fingerprint.Equals(existing.Fingerprint,StringComparison.OrdinalIgnoreCase))??(found.Count==1?found[0]:null);
                discoveryStatus.Text=found.Count==0?"No se encontró un servidor. Comprueba que esté actualizado y encendido, y que los equipos estén en la misma red privada.":found.Count==1?"Servidor encontrado. Comprueba su código de conexión la primera vez.":"Selecciona el equipo principal de tu negocio.";
            } catch(OperationCanceledException) { }catch(Exception error) { discoveryStatus.Text="No se pudo buscar el servidor: "+error.Message; }
            finally { searching=false;find.IsEnabled=true;find.Content="Buscar servidor en la red"; }
        }
        find.Click+=async(_,_)=>await Search();
        void ModeChanged()=>connectionFields.Visibility=mode.SelectedIndex==0?Visibility.Collapsed:Visibility.Visible;
        mode.SelectionChanged+=async(_,_)=> { ModeChanged();if(mode.SelectedIndex==1)await Search(); };ModeChanged();Loaded+=async(_,_)=> { if(mode.SelectedIndex==1)await Search(); };
        var error=new TextBlock { TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,12,0,12) };panel.Children.Add(error);
        var save=new Button { Content="Guardar y continuar" };panel.Children.Add(save);
        save.Click+=(_,_)=>
        {
            try {
                if(searching&&mode.SelectedIndex==1) { error.Text="Espera a que termine la búsqueda.";return; }
                var settings=existing with { Mode=mode.SelectedIndex==0?"Local":"Cliente",Address=address.Text.Trim(),Fingerprint=fingerprint.Text.Trim(),TerminalName=name.Text.Trim() };
                if(settings.Mode!="Local") {
                    using var probe=new RemoteStore(settings,directory);
                    if(servers.SelectedItem is DiscoveredServer selected&&settings.Fingerprint.Equals(selected.Fingerprint,StringComparison.OrdinalIgnoreCase)&&!settings.Fingerprint.Equals(existing.Fingerprint,StringComparison.OrdinalIgnoreCase))
                        if(MessageBox.Show("Comprueba que el equipo principal muestra este mismo código en Configuración > Conexión:\n\n"+LocalDiscovery.PairingCode(selected.Fingerprint)+"\n\n¿Coincide?", "Confirmar equipo principal",MessageBoxButton.YesNo,MessageBoxImage.Question,MessageBoxResult.No)!=MessageBoxResult.Yes)return;
                }
                settings.Save(directory);DialogResult=true;
            } catch(Exception exception) { error.Text=exception.Message; }
        };
    }

}
public partial class MainWindow
{
    private UIElement NetworkPage()
    {
        var configuration=ConnectionSettings.Load(App.DataDirectory);var panel=new StackPanel { MaxWidth=820 };
        panel.Children.Add(Text("Conexión y servidor",22));
        panel.Children.Add(Text(configuration.Mode switch { "Principal"=>"Este equipo administra el servidor. El servicio sigue activo al cerrar Monii.","Cliente"=>"Caja conectada: los datos y las operaciones se guardan en el principal.",_=>"Modo de una computadora: datos locales." }));
        panel.Children.Add(Text("Equipo: "+configuration.TerminalName));
        if(configuration.Mode=="Principal")panel.Children.Add(Text("Código de conexión: "+LocalDiscovery.PairingCode(configuration.Fingerprint),20));
        if(storage is RemoteStore remote)
        {
            var status=Text("");panel.Children.Add(status);
            void Refresh() { try { var value=remote.Status();status.Text=$"Servidor en línea · {value.GetProperty("ActiveSessions").GetInt32()} sesiones · {value.GetProperty("OpenCash").GetInt32()} cajas abiertas"; } catch(Exception e) { status.Text=e.Message; } }
            Refresh();panel.Children.Add(Button("Comprobar conexión",Refresh));
            panel.Children.Add(Text("Operación pendiente: "+remote.PendingDescription));
            panel.Children.Add(Button("Reconciliar operación pendiente",()=>Safe(()=>
            {
                ResolvePendingOperation();
            })));
            var independent=Check(panel,"Cajas independientes (desmarca para caja compartida)",service.Settings.IndependentCash);
            panel.Children.Add(Text("Cada equipo tendrá su apertura, cierre y efectivo. En modo compartido todos utilizan la misma caja. Cierra todas las cajas antes de cambiar."));
            panel.Children.Add(Button("Guardar modalidad de caja",()=>Safe(()=> { service.SaveSettings(service.Settings with { IndependentCash=independent.IsChecked==true });Status.Text="Modalidad guardada."; })));
        }
        if(configuration.Mode=="Local")
            panel.Children.Add(Button("Preparar este equipo como principal",()=>Safe(InstallPrincipal)));
        if(configuration.Mode=="Principal")
        {
            panel.Children.Add(Text("Dirección para las cajas: https://"+Environment.MachineName+":58443\nHuella: "+configuration.Fingerprint,13));
            panel.Children.Add(Button("Iniciar servidor",()=>ControlServer("Start")));
            panel.Children.Add(Button("Reiniciar servidor",()=>ControlServer("Restart")));
            panel.Children.Add(Button("Detener servidor",()=>ControlServer("Stop")));
            panel.Children.Add(Button("Actualizar servidor instalado",()=>Safe(UpdatePrincipal)));
            panel.Children.Add(Button("Desinstalar servidor",()=>Safe(UninstallPrincipal)));
            panel.Children.Add(Button("Guardar instrucciones de conexión",()=>ExportText("conexion-monii.txt","Dirección: https://"+Environment.MachineName+":58443\nHuella: "+configuration.Fingerprint+"\nCada caja debe usar un nombre distinto e iniciar sesión con su usuario.")));
            panel.Children.Add(Button("Ver registros del servidor",()=>Safe(()=>Process.Start(new ProcessStartInfo("explorer.exe",Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),"MoniiServer","logs")) { UseShellExecute=true }))));
        }
        panel.Children.Add(Button("Cambiar conexión de este equipo",()=>Safe(()=>
        {
            storage.Require(Permission.Settings);
            if(storage is RemoteStore pending&&pending.HasPending)throw new ArgumentException("Resuelve la operación pendiente antes de cambiar de conexión.");
            if(MessageBox.Show("Monii se cerrará para aplicar la conexión. Los datos locales y centrales se conservan en sus respectivas bases. ¿Continuar?","Conexión",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)return;
            if(new ConnectionWindow(App.DataDirectory) { Owner=this }.ShowDialog()==true)Close();
        })));
        return Scroll(panel);
    }
    private void ResolvePendingOperation()
    {
        if(storage is not RemoteStore remote||!remote.HasPending) { Status.Text="No hay operaciones pendientes.";return; }
        remote.ResolvePending();saleCart.Clear();Navigate("Inicio");Status.Text="Respuesta recuperada. Revisa el historial antes de registrar otra operación.";
    }
    private void InstallPrincipal()
    {
        storage.Require(Permission.Settings);
        if(storage.ReadOperations().Sessions.Any(s=>s.ClosedAt is null))throw new ArgumentException("Cierra todas las cajas antes de preparar el principal.");
        var script=Path.Combine(AppContext.BaseDirectory,"server","Install-Server.ps1");
        if(!File.Exists(script))throw new ArgumentException("Usa la compilación portable 0.7, que incluye el servidor.");
        if(MessageBox.Show("Se instalará un servicio Windows que inicia con el equipo y una regla para conexiones en la red privada. Windows solicitará permisos de administrador. Se importará un respaldo de tus datos si el servidor no tiene base. Si ya existe, se actualizarán sus archivos conservando los datos del servidor. ¿Continuar?","Equipo principal",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)return;
        var staging=Path.Combine(App.DataDirectory,"server-setup",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(staging);
        var backup=storage.Backup(Path.Combine(staging,"initial.db"));
        var start=new ProcessStartInfo("powershell.exe") { UseShellExecute=true,Verb="runas",WindowStyle=ProcessWindowStyle.Hidden };
        start.Arguments="-NoProfile -ExecutionPolicy Bypass -File "+Quote(script)+" -Import "+Quote(backup);
        using var process=Process.Start(start)!;process.WaitForExit();if(process.ExitCode!=0)throw new ArgumentException("No se instaló el servidor. Revisa el registro de instalación; tu base local se conserva.");
        var info=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),"MoniiServer","connection.json");
        var settings=JsonSerializer.Deserialize<ConnectionSettings>(File.ReadAllText(info))!;
        (settings with { TerminalId=ConnectionSettings.Load(App.DataDirectory).TerminalId,TerminalName=Environment.MachineName }).Save(App.DataDirectory);
        MessageBox.Show("Servidor preparado. Abre Monii nuevamente para trabajar conectado y configurar las otras cajas.","Monii");Close();
    }
    private void UpdatePrincipal()
    {
        storage.Require(Permission.Settings);
        if(storage is RemoteStore remote&&remote.HasPending)throw new ArgumentException("Reconcilia la operación pendiente antes de actualizar el servidor.");
        if(storage.ReadOperations().Sessions.Any(s=>s.ClosedAt is null))throw new ArgumentException("Cierra todas las cajas antes de actualizar el servidor.");
        var script=Path.Combine(AppContext.BaseDirectory,"server","Update-Server.ps1");
        if(!File.Exists(script))throw new ArgumentException("Usa el portable actualizado que incluye mantenimiento del servidor.");
        if(MessageBox.Show("Se actualizará el servidor instalado con los archivos de este portable. Se creará un respaldo previo y se reiniciará el servicio; las cajas perderán temporalmente la conexión. Windows solicitará permisos de administrador. ¿Continuar?","Actualizar servidor",MessageBoxButton.YesNo,MessageBoxImage.Question,MessageBoxResult.No)!=MessageBoxResult.Yes)return;
        using var process=Process.Start(new ProcessStartInfo("powershell.exe") { UseShellExecute=true,Verb="runas",WindowStyle=ProcessWindowStyle.Hidden,Arguments="-NoProfile -ExecutionPolicy Bypass -File "+Quote(script) })!;process.WaitForExit();
        if(process.ExitCode!=0)throw new ArgumentException("No se completó el mantenimiento. Revisa maintenance.log en la carpeta del servidor; se conservan los respaldos.");
        MessageBox.Show("Servidor actualizado. Abre Monii nuevamente e inicia sesión para aplicar el perfil Básico.","Monii");Close();
    }
    private void UninstallPrincipal()
    {
        storage.Require(Permission.Settings);
        if(storage is RemoteStore remote&&remote.HasPending)throw new ArgumentException("Reconcilia la operación pendiente antes de desinstalar.");
        if(storage.ReadOperations().Sessions.Any(s=>s.ClosedAt is null))throw new ArgumentException("Cierra todas las cajas antes de desinstalar el servidor.");
        var script=Path.Combine(AppContext.BaseDirectory,"server","Uninstall-Server.ps1");
        if(!File.Exists(script))throw new ArgumentException("Usa el portable actualizado que incluye la desinstalación.");
        if(MessageBox.Show("Se detendrá y desinstalará el servidor de este equipo. Las otras cajas perderán la conexión. Este equipo continuará en modo local con los datos actuales; se conservarán los datos y respaldos del servidor y una copia de la base local anterior. Monii se cerrará al terminar. ¿Continuar?","Desinstalar servidor",MessageBoxButton.YesNo,MessageBoxImage.Warning,MessageBoxResult.No)!=MessageBoxResult.Yes)return;
        var start=new ProcessStartInfo("powershell.exe") { UseShellExecute=true,Verb="runas",WindowStyle=ProcessWindowStyle.Hidden,Arguments="-NoProfile -ExecutionPolicy Bypass -File "+Quote(script)+" -LocalDirectory "+Quote(App.DataDirectory) };
        using var process=Process.Start(start)!;process.WaitForExit();
        if(process.ExitCode!=0)
        {
            if(ConnectionSettings.Load(App.DataDirectory).Mode=="Local") { MessageBox.Show("Los datos se recuperaron en modo local, pero quedaron componentes del servidor pendientes de retirar. Revisa el registro de desinstalación.","Monii");Close();return; }
            throw new ArgumentException("No se completó la desinstalación. Los datos se conservan; revisa uninstall.log en la carpeta del servidor.");
        }
        MessageBox.Show("Servidor desinstalado. Abre Monii nuevamente para continuar en modo local con los datos actuales.","Monii");Close();
    }
    private static string Quote(string value)=>"\""+value.Replace("\"","")+"\"";
    private void ControlServer(string action)=>Safe(()=>
    {
        if(storage.CurrentUser?.Role!=UserRole.Administrador)throw new UnauthorizedAccessException("Solo el administrador puede controlar el servidor.");
        if(action!="Start"&&MessageBox.Show("Las cajas perderán la conexión. Confirma que no estén cobrando. ¿Continuar?","Servidor",MessageBoxButton.YesNo,MessageBoxImage.Warning)!=MessageBoxResult.Yes)return;
        var script=Path.Combine(AppContext.BaseDirectory,"server","Control-Server.ps1");
        var start=new ProcessStartInfo("powershell.exe") { UseShellExecute=true,Verb="runas",WindowStyle=ProcessWindowStyle.Hidden,Arguments="-NoProfile -ExecutionPolicy Bypass -File "+Quote(script)+" -Action "+action };
        using var process=Process.Start(start)!;process.WaitForExit();Status.Text=process.ExitCode==0?"Acción del servidor completada.":"No se completó la acción. Revisa permisos y registros.";
    });
}
