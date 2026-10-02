using System.IO;
using Monii.Application;
using System.Windows;
using System.Windows.Controls;
using Monii.Domain;
using Monii.Infrastructure;

namespace Monii.Desktop;

public sealed class LoginWindow : Window
{
    public LoginWindow(IMoniiStore store)
    {
        var setup=!store.HasUsers; Title=setup?"Monii · Crear administrador":"Monii · Iniciar sesión"; Width=440; Height=500; ResizeMode=ResizeMode.NoResize; WindowStartupLocation=WindowStartupLocation.CenterScreen;
        SetResourceReference(BackgroundProperty,"ThemeCanvas"); var panel=new StackPanel { Margin=new Thickness(32) }; Content=panel;
        panel.Children.Add(new TextBlock { Text="monii",FontSize=36,FontWeight=FontWeights.Bold,Margin=new Thickness(0,0,0,24) });
        panel.Children.Add(new TextBlock { Text=setup?"Crea tu primera cuenta de administrador.":"Ingresa tus credenciales.",TextWrapping=TextWrapping.Wrap });
        panel.Children.Add(new TextBlock { Text="Usuario",Margin=new Thickness(0,16,0,0) }); var username=new TextBox(); panel.Children.Add(username);
        TextBox? name=null;
        if(setup) { panel.Children.Add(new TextBlock { Text="Nombre" }); name=new TextBox(); panel.Children.Add(name); }
        panel.Children.Add(new TextBlock { Text=setup?"Contraseña (al menos 12 caracteres)":"Contraseña" }); var password=new PasswordBox { Padding=new Thickness(10),Margin=new Thickness(0,8,0,8) }; panel.Children.Add(password);
        var error=new TextBlock { TextWrapping=TextWrapping.Wrap,Foreground=Theme.Resource("ThemeError") }; panel.Children.Add(error);
        var submit=new Button { Content=setup?"Crear cuenta e ingresar":"Ingresar",IsDefault=true }; panel.Children.Add(submit);
        submit.Click+=(_,_)=>
        {
            try { if(setup) store.SaveUser(null,username.Text,name!.Text,UserRole.Administrador,true,password.Password); store.Authenticate(username.Text,password.Password); password.Clear(); DialogResult=true; }
            catch(Exception e) { error.Text=e.Message; password.Clear(); }
        };
        if(store is RemoteStore)
        {
            var connection=ConnectionSettings.Load(App.DataDirectory);
            panel.Children.Add(new TextBlock { Text="Equipo: "+connection.TerminalName,Margin=new Thickness(0,12,0,0) });
            var edit=new Button { Content="Configurar conexión" };panel.Children.Add(edit);
            edit.Click+=(_,_)=> { if(File.Exists(Path.Combine(App.DataDirectory,"pending-operation.json"))) { error.Text="Hay una operación pendiente. Conserva esta conexión y accede con su usuario para reconciliarla.";return; } if(new ConnectionWindow(App.DataDirectory) { Owner=this }.ShowDialog()==true) { MessageBox.Show("Abre Monii nuevamente para aplicar la conexión.","Monii");DialogResult=false; } };
            if(connection.Mode=="Principal")
            {
                var start=new Button { Content="Iniciar servidor" };panel.Children.Add(start);
                start.Click+=(_,_)=>
                {
                    try { var script=Path.Combine(AppContext.BaseDirectory,"server","Control-Server.ps1");var process=System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("powershell.exe") { UseShellExecute=true,Verb="runas",WindowStyle=System.Diagnostics.ProcessWindowStyle.Hidden,Arguments="-NoProfile -ExecutionPolicy Bypass -File \""+script+"\" -Action Start" })!;process.WaitForExit();error.Text=process.ExitCode==0?"Servidor iniciado. Puedes ingresar.":"No se pudo iniciar el servidor.";process.Dispose(); }
                    catch(Exception e) { error.Text=e.Message; }
                };
            }
            Height=640;
        }
        Loaded+=(_,_)=>username.Focus();
    }
}
