using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using System.Windows.Threading;
using Monii.Application;
using Monii.Domain;

namespace Monii.Desktop;

public partial class MainWindow
{
    private void VerifyOperationsUi(string output, List<string> log)
    {
        void Assert(bool condition, string name) { if (!condition) throw new InvalidOperationException("UI operaciones: " + name); log.Add("OK OPERACIONES UI: " + name); }
        void Click(string name) => Descendants(this).OfType<Button>().First(b => b.Content?.ToString() == name).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        void Modal(string trigger, string save, Action<Window> fill)
        {
            Exception? failure = null; var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            timer.Tick += (_, _) =>
            {
                timer.Stop(); var dialog = OwnedWindows.Cast<Window>().Single();
                try { dialog.UpdateLayout(); fill(dialog); Descendants(dialog).OfType<Button>().First(b => b.Content?.ToString() == save).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent)); }
                catch (Exception error) { failure = error; }
                if (dialog.IsVisible) { failure ??= new InvalidOperationException("No se completó " + trigger + ": " + string.Join(" ", Descendants(dialog).OfType<TextBlock>().Select(t => t.Text))); dialog.Close(); }
            };
            timer.Start(); Click(trigger); if (failure is not null) throw failure;
        }
        void Capture(string name)
        {
            UpdateLayout(); Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle); UpdateLayout();
            var root = (FrameworkElement)Content; var image = new RenderTargetBitmap((int)root.ActualWidth, (int)root.ActualHeight, 96, 96, PixelFormats.Pbgra32); image.Render(root);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); using var stream = File.Create(Path.Combine(output, name)); encoder.Save(stream);
        }
        Width = 1280; Height = 820;
        Navigate("Inventario"); UpdateLayout(); var product = service.Products().Single();
        Descendants(PageContent).OfType<DataGrid>().Single().SelectedIndex = 0;
        Modal("Registrar ajuste", "Guardar", dialog => { var fields = Descendants(dialog).OfType<TextBox>().ToList(); fields[0].Text = "20"; fields[1].Text = "Carga inicial UI"; });
        Assert(OperationsService.Stock(operations.State, product.Id) == 20, "Entrada desde formulario de inventario"); Capture("inventario-real.png");
        Navigate("Caja"); UpdateLayout();
        Modal("Abrir caja", "Guardar", dialog => { var fields = Descendants(dialog).OfType<TextBox>().ToList(); fields[0].Text = "100"; });
        Assert(OperationsService.OpenSession(operations.State) is not null, "Apertura desde interfaz de caja");
        VerifySalesCatalogUi(Assert,Capture);
        Navigate("Ventas"); UpdateLayout(); Descendants(PageContent).OfType<TextBox>().Single(t=>t.Name=="SaleSearch").Text=product.Code;Click("Agregar al carrito");
        Modal("Cobrar / registrar crédito", "Confirmar venta", _ => { });
        Assert(operations.State.Sales.Count == 1 && OperationsService.Stock(operations.State, product.Id) == 19, "Cobro real guarda venta y descuenta inventario"); Capture("ventas-reales.png");
        Navigate("Caja"); Capture("caja-real.png");
        Assert(OperationsService.Expected(operations.State, OperationsService.OpenSession(operations.State)!, "USD") == 108.75m, "Venta real concilia con efectivo de caja");

        Navigate("Compras"); UpdateLayout(); var tabs = Descendants(PageContent).OfType<TabControl>().Single(); tabs.SelectedIndex = 1; UpdateLayout();
        Modal("Nuevo proveedor", "Guardar", dialog => Descendants(dialog).OfType<TextBox>().First().Text = "Proveedor UI");
        tabs.SelectedIndex = 0; UpdateLayout();
        Modal("Nueva compra", "Registrar compra", dialog =>
        {
            var combos = Descendants(dialog).OfType<ComboBox>().Where(c => c.DisplayMemberPath == "Name").ToList(); combos[0].SelectedIndex = 0; combos[1].SelectedIndex = 0;
            var fields = Descendants(dialog).OfType<TextBox>().ToList(); fields[0].Text = "COMPRA UI"; fields[1].Text = "2"; fields[2].Text = "4";
            Descendants(dialog).OfType<Button>().First(b => b.Content?.ToString() == "Agregar línea").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        });
        Assert(operations.State.Purchases.Count == 1 && OperationsService.Stock(operations.State, product.Id) == 21 && service.Products().Single().CostUsd == 4, "Compra por formulario actualiza recepción y costo"); Capture("compras-reales.png");

        Navigate("Clientes"); UpdateLayout();
        Modal("Nuevo cliente", "Guardar", dialog => { var fields = Descendants(dialog).OfType<TextBox>().ToList(); fields[0].Text = "Cliente UI"; fields[3].Text = "100"; });
        Assert(operations.State.Customers.Count == 1, "Cliente guardado desde formulario");
        Navigate("Ventas"); UpdateLayout(); Descendants(PageContent).OfType<TextBox>().Single(t=>t.Name=="SaleSearch").Text=product.Code;Click("Agregar al carrito");
        Modal("Cobrar / registrar crédito", "Confirmar venta", dialog =>
        {
            var fields = Descendants(dialog).OfType<TextBox>().ToList(); fields[1].Text = "5";
            Descendants(dialog).OfType<CheckBox>().Single().IsChecked = true;
            Descendants(dialog).OfType<ComboBox>().Single(c => c.DisplayMemberPath == "Name").SelectedIndex = 0;
        });
        Assert(operations.State.Sales.Last().InitialDebt == 3.75m, "Venta a crédito desde pantalla de cobro");
        Navigate("Créditos"); UpdateLayout(); Descendants(PageContent).OfType<DataGrid>().First().SelectedIndex = 0;
        Capture("creditos-reales.png"); Modal("Registrar abono", "Guardar", _ => { });
        Assert(operations.State.Sales.Sum(s => OperationsService.Debt(operations.State, s)) == 0 && operations.State.Abonos.Count == 1, "Abono desde interfaz cancela deuda");
        Navigate("Reportes"); Capture("reportes-reales.png");
        Assert(Descendants(PageContent).OfType<TextBlock>().Any(t => t.Text.StartsWith("Ventas: 2")), "Reporte muestra ventas reales");
        Navigate("Caja"); UpdateLayout();
        var expectedState = operations.State; var openSession = OperationsService.OpenSession(expectedState)!;
        Modal("Cerrar caja", "Guardar", dialog =>
        {
            var fields = Descendants(dialog).OfType<TextBox>().ToList(); fields[0].Text = OperationsService.Expected(expectedState, openSession, "USD").ToString("0.00", UiCulture);
            fields[1].Text = OperationsService.Expected(expectedState, openSession, "VES").ToString("0.00", UiCulture); fields[2].Text = OperationsService.Expected(expectedState, openSession, "COP").ToString("0.00", UiCulture);
        });
        Assert(operations.State.Sessions.Single().ClosedAt is not null && operations.State.Sessions.Single().ExpectedUsd == operations.State.Sessions.Single().CountedUsd, "Cierre de caja desde interfaz concilia efectivo");
        Navigate("Respaldos"); Capture("respaldos.png");
        Assert(Descendants(PageContent).OfType<Button>().Any(b => b.Content?.ToString() == "Restaurar respaldo"), "Acceso a respaldos y restauración");
        // Exercise reversal buttons through their real selection and reason forms.
        operations.OpenCash(1000,0,0);
        var creditSale=operations.State.Sales.Last(); var creditedAbono=operations.State.Abonos.Single();
        Navigate("Créditos"); UpdateLayout();
        Descendants(PageContent).OfType<TabControl>().Single().SelectedIndex=1; UpdateLayout();
        Descendants(PageContent).OfType<DataGrid>().Single().SelectedIndex=0;
        Modal("Anular abono","Confirmar anulación",dialog=>Descendants(dialog).OfType<TextBox>().Single().Text="Corrección de prueba UI");
        Assert(operations.State.Abonos.Single(a=>a.Id==creditedAbono.Id).Voided&&OperationsService.Debt(operations.State,creditSale)==3.75m,"Anular abono desde historial restaura deuda");
        Assert(OperationsService.Expected(operations.State,OperationsService.OpenSession(operations.State)!,"USD")==996.25m,"Anular abono descuenta efectivo de la sesión actual");
        Navigate("Ventas"); UpdateLayout();
        Descendants(PageContent).OfType<TabControl>().Single().SelectedIndex=1; UpdateLayout();
        var saleGrid=Descendants(PageContent).OfType<DataGrid>().Single(); saleGrid.SelectedItem=saleGrid.Items.Cast<Sale>().Single(s=>s.Id==creditSale.Id);
        Modal("Anular venta","Confirmar anulación",dialog=>Descendants(dialog).OfType<TextBox>().Single().Text="Corrección de crédito UI");
        Assert(operations.State.Sales.Single(s=>s.Id==creditSale.Id).Voided&&OperationsService.Debt(operations.State,operations.State.Sales.Single(s=>s.Id==creditSale.Id))==0,"Anular crédito por formulario elimina deuda");
        Assert(OperationsService.Stock(operations.State,product.Id)==21,"Anular venta por formulario repone inventario");
        Navigate("Compras"); UpdateLayout(); Descendants(PageContent).OfType<DataGrid>().Single().SelectedIndex=0;
        Modal("Anular compra","Confirmar anulación",dialog=>Descendants(dialog).OfType<TextBox>().Single().Text="Corrección de compra UI");
        Assert(operations.State.Purchases.Single().Voided&&OperationsService.Stock(operations.State,product.Id)==19,"Anular compra por formulario revierte recepción");
        var finalState=operations.State; var finalSession=OperationsService.OpenSession(finalState)!;
        operations.CloseCash(OperationsService.Expected(finalState,finalSession,"USD"),OperationsService.Expected(finalState,finalSession,"VES"),OperationsService.Expected(finalState,finalSession,"COP"));
        Assert(operations.State.Sessions.All(s=>s.ClosedAt is not null),"Ciclo de anulaciones termina con caja conciliada");
        Navigate("Inicio"); Capture("inicio-operativo.png");
    }
}
