using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Monii.Application;
using Monii.Domain;
using Monii.Infrastructure;
namespace Monii.Desktop;
public partial class MainWindow
{
    internal void VerifyNetworkUi(string output)
    {
        var log=new List<string>();
        void Assert(bool ok,string message) { if(!ok)throw new InvalidOperationException(message);log.Add("OK RED UI: "+message); }
        void Click(string label)=>Descendants(this).OfType<Button>().First(b=>b.Content?.ToString()==label).RaiseEvent(new RoutedEventArgs(ButtonBaseClick()));
        static RoutedEvent ButtonBaseClick()=>System.Windows.Controls.Button.ClickEvent;
        void Capture(string name)
        {
            UpdateLayout();var root=(FrameworkElement)Content;var image=new RenderTargetBitmap((int)root.ActualWidth,(int)root.ActualHeight,96,96,PixelFormats.Pbgra32);image.Render(root);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using var stream=File.Create(Path.Combine(output,name));encoder.Save(stream);
        }
        void Modal(string trigger,string save,Action<Window> fill)
        {
            Exception? error=null;var timer=new DispatcherTimer { Interval=TimeSpan.FromMilliseconds(120) };
            timer.Tick+=(_,_)=> { timer.Stop();var dialog=OwnedWindows.Cast<Window>().Single();try { dialog.UpdateLayout();fill(dialog);Descendants(dialog).OfType<Button>().First(b=>b.Content?.ToString()==save).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent)); } catch(Exception e) { error=e; }if(dialog.IsVisible) { error??=new InvalidOperationException("No se completó formulario: "+trigger);dialog.Close(); } };
            timer.Start();Click(trigger);if(error is not null)throw error;UpdateLayout();
        }
        Assert(storage is RemoteStore&&ConnectionLabel.Text.Contains("principal"),"Interfaz identifica conexión al principal");
        Width=960;Navigate("Configuración");UpdateLayout();var tabs=Descendants(PageContent).OfType<TabControl>().Single();
        tabs.SelectedItem=tabs.Items.Cast<TabItem>().Single(t=>t.Header?.ToString()=="Conexión");UpdateLayout();
        Assert(Descendants(PageContent).OfType<Button>().Any(b=>b.Content?.ToString()=="Comprobar conexión"),"Configuración presenta estado y comprobación de servidor");
        Assert(tabs.Items.Cast<TabItem>().All(t=>t.TranslatePoint(new Point(),tabs).X+t.ActualWidth<=tabs.ActualWidth),"Botones de conexión y configuración completos a 960 píxeles");Capture("conexion-red.png");
        foreach(var page in new[]{"Productos","Inventario","Compras","Clientes","Créditos","Reportes"}) { Navigate(page);UpdateLayout();Assert(PageTitle.Text==page,"Navegación remota: "+page); }
        Navigate("Caja");UpdateLayout();Modal("Abrir caja","Guardar",window=>Descendants(window).OfType<TextBox>().First().Text="10");
        Assert(operations.CurrentCash is not null,"Apertura desde formulario WPF se guarda en servidor");
        Navigate("Ventas");UpdateLayout();var selector=Descendants(PageContent).OfType<ComboBox>().Single(c=>c.DisplayMemberPath=="Name");selector.SelectedItem=selector.Items.Cast<Product>().Single(p=>p.Code=="UI-RED");Click("Agregar al carrito");
        Modal("Cobrar / registrar crédito","Confirmar venta",_=>{});
        Assert(operations.State.Sales.Any(s=>s.Lines.Any(l=>l.Code=="UI-RED"))&&saleCart.Count==0,"Cobro WPF remoto confirma venta y limpia carrito");Capture("venta-red.png");
        Navigate("Caja");UpdateLayout();Assert(OperationsService.Expected(operations.State,operations.CurrentCash!,"USD")==14,"Efectivo remoto concilia venta de interfaz");
        Modal("Cerrar caja","Guardar",window=>Descendants(window).OfType<TextBox>().First().Text="14");
        Assert(operations.CurrentCash is null,"Cierre WPF se persiste en servidor");
        File.WriteAllLines(Path.Combine(output,"ui-verification.txt"),log);
    }
}
