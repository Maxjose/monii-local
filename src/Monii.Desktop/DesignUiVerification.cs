using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Monii.Domain;
using Monii.Application;

namespace Monii.Desktop;
public partial class MainWindow
{
    private void VerifyDesignUi(string output,List<string> log)
    {
        void Assert(bool okay,string label) { if(!okay) throw new InvalidOperationException("UI 0.4: "+label); log.Add("OK UI 0.4: "+label); }
        void Click(DependencyObject root,string text)=>Descendants(root).OfType<Button>().First(b=>b.Content?.ToString()==text).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        void Capture(FrameworkElement root,string name)
        {
            root.UpdateLayout(); Dispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle); root.UpdateLayout(); var image=new RenderTargetBitmap((int)root.ActualWidth,(int)root.ActualHeight,96,96,PixelFormats.Pbgra32);
            var background=new DrawingVisual(); using(var drawing=background.RenderOpen()) drawing.DrawRectangle(Window.GetWindow(root)?.Background??Theme.Resource("ThemeCanvas"),null,new Rect(0,0,root.ActualWidth,root.ActualHeight)); image.Render(background); image.Render(root);
            var encoder=new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); using var stream=File.Create(Path.Combine(output,name)); encoder.Save(stream);
        }
        void Modal(Action open,string save,Action<Window> fill)
        {
            Exception? failure=null; var timer=new DispatcherTimer { Interval=TimeSpan.FromMilliseconds(120) };
            timer.Tick+=(_,_)=> { timer.Stop(); var window=OwnedWindows.Cast<Window>().Single(); try { window.UpdateLayout(); fill(window); Click(window,save); } catch(Exception e) { failure=e; } if(window.IsVisible) { failure??=new InvalidOperationException("No se cerró "+save); window.Close(); } };
            timer.Start(); open(); if(failure is not null) throw failure;
        }
        Navigate("Configuración"); UpdateLayout();
        Assert(Descendants(PageContent).OfType<TabControl>().Single().Items.Cast<TabItem>().Select(t=>t.Header?.ToString()).Contains("Respaldos"),"Configuración agrupa Respaldos, Usuarios y Actualizaciones");
        Assert(!Descendants(Navigation).OfType<Button>().Any(b=>b.Content?.ToString() is "Respaldos" or "Usuarios" or "Actualizaciones"),"Herramientas administrativas retiradas del menú lateral");
        Assert(Descendants(Navigation).OfType<Button>().All(b=>b.Tag is string glyph&&glyph.Length>0),"Menú lateral tiene iconos en todas sus opciones");
        Navigate("Categorías"); UpdateLayout();
        Modal(()=>Click(PageContent,"Nueva categoría"),"Guardar",window=>Descendants(window).OfType<TextBox>().Single().Text="Captura de códigos");
        Assert(storage.GetCategories().Any(c=>c.Name=="Captura de códigos"),"Crear categoría mediante formulario real"); Capture((FrameworkElement)Content,"categorias.png");
        Navigate("Apariencia"); UpdateLayout();
        Descendants(PageContent).OfType<CheckBox>().Single().IsChecked=true; Descendants(PageContent).OfType<TextBox>().Single().Text="#2563EB"; Click(PageContent,"Guardar apariencia"); UpdateLayout();
        Assert(service.Settings.DarkMode&&service.Settings.AccentColor=="#2563EB"&&Theme.IsDark,"Guardar modo oscuro y color azul desde Configuración"); Capture((FrameworkElement)Content,"apariencia-oscura.png");
        Navigate("Productos"); UpdateLayout(); Capture((FrameworkElement)Content,"productos-oscuros.png");
        Modal(()=>Click(PageContent,"+ Nuevo producto"),"Guardar producto",window=>
        {
            var fields=Descendants(window).OfType<TextBox>().ToList(); fields[0].Text="Producto con escáner · PRUEBA"; fields[2].Text="2"; fields[3].Text="5";
            var category=Descendants(window).OfType<ComboBox>().First(); Assert(!category.IsEditable,"Categoría se selecciona sin escritura manual"); category.SelectedItem=category.Items.Cast<Category>().Single(c=>c.Name=="Captura de códigos");
            Exception? failure=null; var timer=new DispatcherTimer { Interval=TimeSpan.FromMilliseconds(120) };
            timer.Tick+=(_,_)=>
            {
                timer.Stop(); var scanner=window.OwnedWindows.Cast<Window>().Single();
                try
                {
                    scanner.UpdateLayout(); var input=Descendants(scanner).OfType<TextBox>().Single();
                    void Enter()=>input.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(input)!,0,Key.Enter) { RoutedEvent=Keyboard.PreviewKeyDownEvent });
                    Enter(); Assert(scanner.IsVisible,"Escaneo vacío no confirma ni guarda un producto"); input.Text="7891234567890"; Capture((FrameworkElement)scanner.Content,"escaner-oscuro.png"); Enter();
                }
                catch(Exception e) { failure=e; scanner.Close(); }
            };
            timer.Start(); Click(window,"Escanear"); if(failure is not null) throw failure;
            Assert(fields[1].Text=="7891234567890","Enter del lector coloca automáticamente el código en el producto"); Capture((FrameworkElement)window.Content,"producto-oscuro.png");
        });
        Assert(service.Products().Any(p=>p.Code=="7891234567890"&&p.Category=="Captura de códigos"),"Producto con código capturado y categoría seleccionada persiste");
        Navigate("Categorías"); UpdateLayout(); var grid=Descendants(PageContent).OfType<DataGrid>().Single(); grid.SelectedItem=grid.Items.Cast<Category>().Single(c=>c.Name=="Captura de códigos"); Click(PageContent,"Activar / desactivar categoría");
        Assert(!storage.GetCategories().Single(c=>c.Name=="Captura de códigos").Active&&service.Products().Any(p=>p.Code=="7891234567890"),"Desactivar categoría desde UI conserva su producto");
        Navigate("Ventas"); UpdateLayout(); Capture((FrameworkElement)Content,"ventas-oscuras.png"); Navigate("Reportes"); UpdateLayout(); Capture((FrameworkElement)Content,"reportes-oscuros.png");
        Navigate("Apariencia"); UpdateLayout(); Descendants(PageContent).OfType<CheckBox>().Single().IsChecked=false; Click(PageContent,"Guardar apariencia");
        Assert(!service.Settings.DarkMode&&!Theme.IsDark,"Volver a modo claro mantiene el color personalizado");
        Width=960; Height=640; Navigate("Configuración"); UpdateLayout(); Capture((FrameworkElement)Content,"configuracion-960.png");
        var existingSession=OperationsService.OpenSession(operations.State); if(existingSession is not null) operations.CloseCash(OperationsService.Expected(operations.State,existingSession,"USD"),OperationsService.Expected(operations.State,existingSession,"VES"),OperationsService.Expected(operations.State,existingSession,"COP"));
        var previous=service.Settings;
        service.SaveSettings(previous with { ShowBcv=false,ShowManualVes=false,ShowCop=false });
        Navigate("Caja"); UpdateLayout();
        Modal(()=>Click(PageContent,"Abrir caja"),"Guardar",window=> {
            Assert(Descendants(window).OfType<TextBox>().Count()==1,"Apertura solo muestra USD con monedas alternativas desactivadas");
        });
        Navigate("Caja"); UpdateLayout();
        Modal(()=>Click(PageContent,"Cerrar caja"),"Guardar",window=> {
            Assert(Descendants(window).OfType<TextBox>().Count()==1,"Cierre sin saldos alternativos solo muestra USD");
        });
        Navigate("Ventas"); UpdateLayout();
        var paymentPanel=new StackPanel(); var payment=PaymentFields(paymentPanel,"Prueba");
        Assert(payment.Currency.Items.Count==1&&payment.Currency.SelectedItem?.ToString()=="USD","Cobros solo ofrecen monedas activas");
        var before=Theme.Resource("ThemeSidebar").Color;
        service.SaveSettings(service.Settings with { AccentColor="#BE185D" }); Theme.Apply(service.Settings);
        Assert(before!=Theme.Resource("ThemeSidebar").Color&&Theme.Resource("ThemeSelected").Color.R>Theme.Resource("ThemeSelected").Color.G,"Color principal armoniza menú y selecciones");
        Width=960; Navigate("Configuración"); UpdateLayout();
        var configTabs=Descendants(PageContent).OfType<TabControl>().Single();
        Assert(configTabs.TabStripPlacement==Dock.Top&&configTabs.Items.Cast<TabItem>().Any(t=>t.Header?.ToString()=="Monedas y tasas"),"Secciones superiores completas y monedas independientes");
        var headers=configTabs.Items.Cast<TabItem>().ToList();
        Assert(headers.All(tab=> { var position=tab.TranslatePoint(new Point(),configTabs); return position.X>=0&&position.X+tab.ActualWidth<=configTabs.ActualWidth&&tab.ActualHeight>=36; }),"Botones de configuración con márgenes y cuatro esquinas dentro del espacio disponible");
        Assert(headers.Select(tab=>Math.Round(tab.TranslatePoint(new Point(),configTabs).Y)).Distinct().Count()==1,"Secciones en una fila superior a 960 píxeles");
        Capture((FrameworkElement)Content,"configuracion-armonia.png");
        foreach(var page in new[]{"Ventas","Créditos","Caja","Compras","Reportes"})
        {
            Navigate(page); UpdateLayout(); Dispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle); UpdateLayout();
            foreach(var submenu in Descendants(PageContent).OfType<TabControl>())
                Assert(submenu.Items.Cast<TabItem>().All(tab=> { var position=tab.TranslatePoint(new Point(),submenu); return position.X>=0&&position.X+tab.ActualWidth<=submenu.ActualWidth&&tab.ActualHeight>=36; }),"Submenús completos con márgenes en "+page);
            Capture((FrameworkElement)Content,"submenu-"+page+".png");
        }
        service.SaveSettings(previous); Theme.Apply(previous);
        Assert(Descendants(Navigation).OfType<Button>().Any(b=>b.Content?.ToString()=="Cerrar sesión")&&!Descendants(Navigation).OfType<Button>().Any(b=>b.Content?.ToString()=="Cambiar usuario"),"Menú muestra Cerrar sesión");
        var signedUser=storage.CurrentUser; var cartCount=saleCart.Count;
        Modal(()=>Navigate("Cerrar sesión"),"Cancelar",window=> {
            Assert(storage.CurrentUser==signedUser,"Confirmación aparece antes de cerrar sesión");
            Capture(window,"cerrar-sesion.png");
        });
        Assert(storage.CurrentUser==signedUser&&saleCart.Count==cartCount,"Cancelar conserva sesión y carrito");
        bool approved=false;
        Modal(()=>approved=ConfirmSignOut(),"Cerrar sesión",window=> {
            Assert(Descendants(window).OfType<Button>().Single(b=>b.Content?.ToString()=="Cancelar").IsDefault,"Cancelar es la opción predeterminada");
        });
        Assert(approved,"Confirmar autoriza el cierre de sesión");
        var logoutPassword="Monii-Logout-Test-8472";
        storage.SaveUser(null,"logout-ui","Prueba de sesión",UserRole.Administrador,true,logoutPassword);
        Exception? logoutFailure=null;var logoutStage=0;
        var logoutTimer=new DispatcherTimer { Interval=TimeSpan.FromMilliseconds(150) };
        logoutTimer.Tick+=(_,_)=> {
            try {
                if(logoutStage==0) { logoutStage=1;Click(OwnedWindows.Cast<Window>().Single(),"Cerrar sesión"); }
                else {
                    logoutTimer.Stop();var login=System.Windows.Application.Current.Windows.OfType<LoginWindow>().Single();
                    Assert(!IsVisible&&PageContent.Content is null&&storage.CurrentUser is null,"Al cerrar sesión solo permanece visible la ventana de acceso");
                    Capture(login,"acceso-sin-ventana-fondo.png");
                    Descendants(login).OfType<TextBox>().Single().Text="logout-ui";
                    Descendants(login).OfType<PasswordBox>().Single().Password=logoutPassword;var loginButton=Descendants(login).OfType<Button>().Single(b=>b.IsDefault);var loginHeight=loginButton.ActualHeight;Click(login,"Ingresar");
                    Assert(!Descendants(login).OfType<Button>().Single(b=>b.IsDefault).IsEnabled&&Descendants(login).OfType<Button>().Single(b=>b.IsDefault).Content is Grid loadingPanel&&loadingPanel.Children.OfType<System.Windows.Shapes.Path>().Any()&&loginButton.Height==loginHeight&&loadingPanel.HorizontalAlignment==HorizontalAlignment.Center,"Acceso muestra círculo centrado sin texto, conserva altura y evita envíos duplicados");
                    login.UpdateLayout();Capture(login,"acceso-carga-circular.png");
                }
            } catch(Exception error) { logoutTimer.Stop();logoutFailure=error;foreach(var dialog in System.Windows.Application.Current.Windows.Cast<Window>().Where(w=>w!=this).ToList())dialog.Close(); }
        };
        logoutTimer.Start();Navigate("Cerrar sesión");if(logoutFailure is not null)throw logoutFailure;
        Assert(IsVisible&&storage.CurrentUser?.Username=="logout-ui","Nuevo inicio de sesión recupera ventana principal");
        Navigate("Inicio"); UpdateLayout(); Capture((FrameworkElement)Content,"inicio-04.png");
    }
}
