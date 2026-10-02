using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Monii.Application;
using Monii.Domain;
using Monii.Infrastructure;

namespace Monii.Desktop;
public partial class MainWindow
{
    private void VerifyNextUi(string output,List<string> log)
    {
        void Assert(bool okay,string label) { if(!okay) throw new InvalidOperationException("UI 0.3: "+label); log.Add("OK UI 0.3: "+label); }
        void Capture(FrameworkElement root,string name)
        {
            root.UpdateLayout(); var image=new RenderTargetBitmap((int)root.ActualWidth,(int)root.ActualHeight,96,96,PixelFormats.Pbgra32);
            var background=new DrawingVisual(); using(var drawing=background.RenderOpen()) drawing.DrawRectangle(Window.GetWindow(root)?.Background??Brushes.White,null,new Rect(0,0,root.ActualWidth,root.ActualHeight)); image.Render(background); image.Render(root);
            var encoder=new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); using var stream=File.Create(Path.Combine(output,name)); encoder.Save(stream);
        }
        void Modal(Action open,string save,Action<Window> fill,string? capture=null)
        {
            Exception? failure=null; var timer=new DispatcherTimer { Interval=TimeSpan.FromMilliseconds(150) };
            timer.Tick+=(_,_)=>
            {
                timer.Stop(); var window=OwnedWindows.Cast<Window>().Single();
                try { window.UpdateLayout(); fill(window); window.UpdateLayout(); if(capture is not null) Capture((FrameworkElement)window.Content,capture); Descendants(window).OfType<Button>().First(b=>b.Content?.ToString()==save).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent)); }
                catch(Exception e) { failure=e; }
                if(window.IsVisible) { failure??=new InvalidOperationException("Formulario sin cerrar: "+save); window.Close(); }
            }; timer.Start(); open(); if(failure is not null) throw failure;
        }
        operations.OpenCash(100,0,0);
        var product=service.Products().Single(); var sale=operations.Sell([(product.Id,2)],1,[new(Currency.USD,PaymentMethod.Efectivo,16.50m)]);
        Modal(()=>ReturnForm(sale,()=>{}),"Registrar devolución",window=>
        {
            var boxes=Descendants(window).OfType<TextBox>().ToList(); boxes[0].Text="1"; boxes[1].Text="Devolución UI";
            Assert(Descendants(window).OfType<TextBlock>().Any(t=>t.Text.Contains("Total USD 8,25")),"Vista previa muestra reintegro descontado antes de confirmar");
        },"devolucion.png");
        Assert(operations.State.Returns.Count==1&&operations.State.Returns.Single().Total==8.25m,"Devolución por formulario conserva parte de la venta");
        Navigate("Ventas"); UpdateLayout(); var tabs=Descendants(PageContent).OfType<TabControl>().Single(); tabs.SelectedIndex=1; UpdateLayout();
        Assert(Descendants(PageContent).OfType<TextBlock>().Any(t=>t.Text.Contains("Página 1")),"Historial paginado de ventas visible"); Capture((FrameworkElement)Content,"historial-paginado.png");
        Navigate("Usuarios"); UpdateLayout();
        var password=Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(20));
        Modal(()=>Descendants(PageContent).OfType<Button>().First(b=>b.Content?.ToString()=="Nuevo usuario").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent)),"Guardar",window=>
        {
            var boxes=Descendants(window).OfType<TextBox>().ToList(); boxes[0].Text="vendedor-ui"; boxes[1].Text="Vendedor de prueba";
            Descendants(window).OfType<ComboBox>().Single().SelectedItem="Vendedor"; Descendants(window).OfType<PasswordBox>().Single().Password=password;
        });
        Assert(storage.Users().Any(u=>u.Username=="vendedor-ui"&&u.Role==UserRole.Vendedor),"Cuenta de vendedor creada mediante controles WPF"); Capture((FrameworkElement)Content,"usuarios.png");
        storage.SaveUser(null,"admin-ui","Administrador UI",UserRole.Administrador,true,password);
        void Login(string username)
        {
            storage.SignOut(); Modal(()=> { var window=new LoginWindow(storage) { Owner=this }; window.ShowDialog(); },"Ingresar",window=> { Descendants(window).OfType<TextBox>().Single().Text=username; Descendants(window).OfType<PasswordBox>().Single().Password=password; },"inicio-sesion.png");
        }
        Login("vendedor-ui"); Navigate("Inicio"); UpdateLayout();
        Assert(!Descendants(Navigation).OfType<Button>().Any(b=>b.Content?.ToString() is "Configuración" or "Usuarios" or "Inventario"),"Navegación respeta permisos del vendedor");
        Navigate("Productos"); UpdateLayout();
        Assert(!Descendants(PageContent).OfType<DataGrid>().Single().Columns.Any(c=>c.Header?.ToString()=="Costo USD"),"Vendedor consulta precios sin columna de costos"); Capture((FrameworkElement)Content,"productos-vendedor.png");
        Login("admin-ui"); Navigate("Actualizaciones"); UpdateLayout();
        Assert(Descendants(PageContent).OfType<Button>().Any(b=>b.Content?.ToString()=="Buscar actualizaciones")&&!Descendants(PageContent).OfType<Button>().Any(b=>b.Content?.ToString() is "Aplicar actualización y reiniciar" or "Consultar y descargar"),"Actualizaciones permite detectar sin descargar o instalar"); Capture((FrameworkElement)Content,"actualizaciones.png");
        var initial=new SqliteStore(Path.Combine(output,"setup-test-"+Guid.NewGuid().ToString("N"),"monii.db"));
        Modal(()=> { var window=new LoginWindow(initial) { Owner=this }; window.ShowDialog(); },"Crear cuenta e ingresar",window=> { var boxes=Descendants(window).OfType<TextBox>().ToList(); boxes[0].Text="administrador"; boxes[1].Text="Mi negocio"; Descendants(window).OfType<PasswordBox>().Single().Password=password; },"primer-administrador.png");
        Assert(initial.HasUsers&&initial.CurrentUser?.Role==UserRole.Administrador,"Primera apertura crea administrador mediante formulario real");
        Navigate("Inicio");
    }
}
