using System.IO;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Animation;
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
        panel.Children.Add(new TextBlock { Text=setup?"Contraseña (al menos 8 caracteres)":"Contraseña" }); var password=new PasswordBox { Padding=new Thickness(10),Margin=new Thickness(0,8,0,8) }; panel.Children.Add(password);
        var error=new TextBlock { TextWrapping=TextWrapping.Wrap,Foreground=Theme.Resource("ThemeError") }; panel.Children.Add(error);
        var submit=new Button { Content=setup?"Crear cuenta e ingresar":"Ingresar",IsDefault=true }; panel.Children.Add(submit);
        var busy=false;
        Closing+=(_,args)=> { if(busy)args.Cancel=true; };
        submit.Click+=async (_,_)=>
        {
            if(busy)return;
            var loginName=username.Text;var displayName=name?.Text;var secret=password.Password;
            submit.Height=submit.ActualHeight;
            busy=true;error.Text="";
            username.IsEnabled=false;password.IsEnabled=false;if(name is not null)name.IsEnabled=false;
            foreach(var button in panel.Children.OfType<Button>())button.IsEnabled=false;
            var rotation=new RotateTransform();
            var spinner=new Grid { Width=20,Height=20,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,RenderTransformOrigin=new Point(.5,.5),RenderTransform=rotation };
            spinner.Children.Add(new System.Windows.Shapes.Ellipse { Stroke=Brushes.White,StrokeThickness=2.5,Opacity=.25,Margin=new Thickness(1.5) });
            spinner.Children.Add(new System.Windows.Shapes.Path { Data=Geometry.Parse("M 10,1.5 A 8.5,8.5 0 1 1 1.5,10"),Stroke=Brushes.White,StrokeThickness=2.5,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round });
            submit.HorizontalContentAlignment=HorizontalAlignment.Center;submit.VerticalContentAlignment=VerticalAlignment.Center;
            submit.Content=spinner;
            System.Windows.Automation.AutomationProperties.SetName(submit,"Iniciando sesión, espera");
            rotation.BeginAnimation(RotateTransform.AngleProperty,new DoubleAnimation(0,360,TimeSpan.FromSeconds(1)) { RepeatBehavior=RepeatBehavior.Forever });
            var succeeded=false;
            try
            {
                await Task.Run(()=> { if(setup)store.SaveUser(null,loginName,displayName!,UserRole.Administrador,true,secret);store.Authenticate(loginName,secret); });
                succeeded=true;
            }
            catch(Exception e) { error.Text=e.Message; }
            finally
            {
                busy=false;password.Clear();rotation.BeginAnimation(RotateTransform.AngleProperty,null);
                submit.Content=setup?"Crear cuenta e ingresar":"Ingresar";
                System.Windows.Automation.AutomationProperties.SetName(submit,submit.Content.ToString());
                username.IsEnabled=true;password.IsEnabled=true;if(name is not null)name.IsEnabled=true;
                foreach(var button in panel.Children.OfType<Button>())button.IsEnabled=true;
            }
            if(succeeded)DialogResult=true;else password.Focus();
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
