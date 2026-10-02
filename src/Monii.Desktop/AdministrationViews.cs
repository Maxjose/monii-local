using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Monii.Domain;
using Monii.Infrastructure;

namespace Monii.Desktop;

public partial class MainWindow
{
    private bool refreshingRates;
    private string rateMessage="";
    private readonly DispatcherTimer exchangeTimer=new() { Interval=TimeSpan.FromMinutes(60) };
    internal void StartExchangeRefresh()
    {
        exchangeTimer.Tick+=async(_,_)=>await RefreshRates(false); exchangeTimer.Start();
        Loaded+=async(_,_)=>await RefreshRates(false);
        Closed+=(_,_)=>exchangeTimer.Stop();
    }
    private async Task RefreshRates(bool force)
    {
        var settings=service.Settings;
        if(!force&&!(settings.ShowBcv&&settings.AutoBcv)&&!(settings.ShowCop&&settings.AutoCop)) return;
        if(refreshingRates) return; refreshingRates=true;
        try { var result=await new ExchangeRates(storage).RefreshAsync(force); rateMessage=result.Message; Status.Text=result.Message; }
        catch(Exception e) { rateMessage="No se actualizaron las tasas: "+e.Message; Status.Text=rateMessage; }
        finally { refreshingRates=false; }
    }
    private UIElement UsersPage()
    {
        var header=new StackPanel(); header.Children.Add(Text("Usuarios y permisos",22));
        header.Children.Add(Text("Administrador: todas las funciones. Cajero: ventas, clientes, abonos y caja. Vendedor: ventas y clientes. Anulaciones, ajustes y configuración requieren administrador.",14,"#64748B"));
        var grid=Table(("Usuario","Username",2),("Nombre","Name",3),("Rol","Role",2),("Activo","Active",1));
        void Reload()=>grid.ItemsSource=storage.Users();
        void Edit(UserAccount? account)
        {
            var panel=new StackPanel(); var username=Field(panel,"Usuario",account?.Username??""); var name=Field(panel,"Nombre",account?.Name??""); var role=Choice(panel,"Rol",Enum.GetNames<UserRole>(),(account?.Role??UserRole.Cajero).ToString()); var active=Check(panel,"Cuenta activa",account?.Active??true);
            panel.Children.Add(Text(account is null?"Contraseña (mínimo 12 caracteres)":"Nueva contraseña (vacío conserva la actual)")); var password=new PasswordBox { Padding=new Thickness(10),Margin=new Thickness(0,8,0,8) }; panel.Children.Add(password);
            Form("Cuenta de usuario",panel,()=> { storage.SaveUser(account?.Id,username.Text,name.Text,Enum.Parse<UserRole>(role.SelectedItem!.ToString()!),active.IsChecked==true,password.Password.Length==0?null:password.Password); password.Clear(); Reload(); BuildNavigation(); });
        }
        var actions=new WrapPanel(); actions.Children.Add(Button("Nuevo usuario",()=>Edit(null))); actions.Children.Add(Button("Editar usuario",()=> { if(grid.SelectedItem is UserAccount a) Edit(a); else Status.Text="Selecciona una cuenta."; })); header.Children.Add(actions); Reload(); return Page(header,grid);
    }
    private UIElement UpdatesPage() => BuildUpdatesPage();
}
