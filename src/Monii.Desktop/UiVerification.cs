using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Monii.Domain;

namespace Monii.Desktop;

public partial class MainWindow
{
    // Opt-in verification against a separate database. Uses actual WPF controls.
    internal void VerifyUi(string output)
    {
        foreach(var file in new[]{"ui-error.txt","ui-verification.txt"}) { var path=Path.Combine(output,file); if(File.Exists(path)) File.Delete(path); }
        var log = new List<string>();
        void Assert(bool condition, string name)
        {
            if (!condition) throw new InvalidOperationException("UI: " + name);
            log.Add("OK: " + name);
        }
        void Click(string label)
        {
            var button = Descendants(this).OfType<Button>().First(b => b.Content?.ToString() == label);
            button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            UpdateLayout();
        }
        void Capture(string file)
        {
            UpdateLayout();
            Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            UpdateLayout();
            var root = (FrameworkElement)Content;
            var bitmap = new RenderTargetBitmap((int)root.ActualWidth, (int)root.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(root);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(Path.Combine(output, file)); encoder.Save(stream);
        }

        Assert(service.Products(includeInactive: true).Count == 0, "Base de prueba inicialmente vacía");
        Click("Configuración");
        var settingsFields = Descendants(PageContent).OfType<TextBox>().ToList();
        settingsFields[0].Text = "Monii · Negocio de prueba";
        var profile = Descendants(PageContent).OfType<ComboBox>().Single(); profile.SelectedIndex = 2;
        var checks = Descendants(PageContent).OfType<CheckBox>().ToList();
        Click("Guardar configuración");
        Assert(service.Settings.Name == "Monii · Negocio de prueba" && service.Settings.Profile == BusinessProfile.Parts && service.Settings.VehicleCompatibility, "Guardar perfil y nombre desde controles WPF");
        var settingsTabs=Descendants(PageContent).OfType<TabControl>().Single();
        settingsTabs.SelectedItem=settingsTabs.Items.Cast<TabItem>().Single(t=>t.Header?.ToString()=="Monedas y tasas"); UpdateLayout();
        var currencyChecks=Descendants(PageContent).OfType<CheckBox>().ToList();
        var rateFields=Descendants(PageContent).OfType<TextBox>().ToList();
        currencyChecks.First(c=>c.Content.ToString()!.StartsWith("Mostrar bolívares a referencia")).IsChecked=true;
        currencyChecks.First(c=>c.Content.ToString()=="Mostrar bolívares a tasa manual").IsChecked=true;
        currencyChecks.First(c=>c.Content.ToString()=="Mostrar pesos colombianos").IsChecked=true;
        rateFields[0].Text="50,25"; rateFields[1].Text="60"; rateFields[2].Text="4200";
        Click("Guardar monedas y tasas");
        Assert(service.Settings.BcvRate==50.25m&&service.Settings.ShowBcv&&service.Settings.ShowCop,"Guardar tasas desde sección independiente con coma decimal");
        settingsTabs.SelectedItem=settingsTabs.Items.Cast<TabItem>().Single(t=>t.Header?.ToString()=="Negocio"); UpdateLayout();
        var inventory = checks.First(c => c.Content.ToString() == "Inventario");
        inventory.IsChecked = false; Click("Guardar configuración");
        Assert(!Descendants(Navigation).OfType<Button>().Any(b => b.Content?.ToString() == "Inventario"), "Desactivar módulo lo oculta del menú");
        inventory.IsChecked = true; Click("Guardar configuración");
        Capture("configuracion.png");
        storage.SaveCategory(new Category(Guid.NewGuid(),"Filtros")); Click("Productos");
        Exception? dialogFailure = null;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            var dialog = OwnedWindows.Cast<Window>().Single();
            try
            {
                var fields = Descendants(dialog).OfType<TextBox>().ToList();
                fields[0].Text = "Filtro de aceite · PRUEBA UI";
                fields[1].Text = "UI-001";
                var categoryBox=Descendants(dialog).OfType<ComboBox>().First(); categoryBox.SelectedItem=categoryBox.Items.Cast<Category>().Single(c=>c.Name=="Filtros");
                fields[2].Text = "3,25";
                fields[3].Text = "8.75";
                fields[4].Text = "Marca de prueba";
                fields[5].Text = "REF-UI";
                fields[6].Text = "Vehículo de prueba";
                Descendants(dialog).OfType<Button>().First(b => b.Content?.ToString() == "Guardar producto").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            }
            catch (Exception error) { dialogFailure = error; dialog.Close(); }
            // Close if validation failed, so the verification cannot remain blocked.
            if (dialog.IsVisible) { dialogFailure ??= new InvalidOperationException("No se guardó el formulario de producto."); dialog.Close(); }
        };
        timer.Start(); Click("+ Nuevo producto");
        if (dialogFailure is not null) throw dialogFailure;
        Assert(service.Products().Single().PriceUsd == 8.75m && service.Products().Single().Compatibility == "Vehículo de prueba", "Crear producto mediante formulario WPF");
        var search = Descendants(PageContent).OfType<TextBox>().Single(); search.Text = "no-existe";
        var grid = Descendants(PageContent).OfType<DataGrid>().Single();
        Assert(grid.Items.Count == 0, "Buscar sin coincidencias actualiza tabla");
        search.Text = "UI-001";
        Assert(grid.Items.Count == 1, "Buscar por código actualiza tabla");
        Capture("productos.png");
        Click("Ventas");
        Click("Abrir demostración");
        var demo = Descendants(PageContent).OfType<Button>().First(b => b.Content?.ToString()?.StartsWith("Arroz") == true);
        demo.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent)); demo.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        Assert(demoCart.Count == 1 && demoCart[0].Quantity == 2 && demoCart[0].Total == 2.50m, "Carrito demo acumula cantidades y totales");
        Assert(Descendants(PageContent).OfType<TextBlock>().Any(t => t.Text.Contains("Bs BCV (manual)")), "Conversión visible y BCV identificado como manual");
        Assert(service.Products().Count == 1, "Demo no modifica catálogo real");
        Capture("ventas-demo.png");
        Click("Vaciar carrito"); Assert(demoCart.Count == 0, "Vaciar carrito demo");
        Click("Inicio"); Capture("inicio.png");
        Width = 960; Height = 640; UpdateLayout(); Capture("inicio-960.png");
        Assert(Descendants(this).OfType<ScrollViewer>().Any(s => ReferenceEquals(s.Content, Navigation)), "Navegación desplazable mantiene acceso a configuración en ventanas pequeñas");
        Click("Ventas"); Capture("ventas-960.png");
        Assert(PageContent.ActualWidth > 600, "Área útil a resolución mínima");
        log.Add("Imágenes generadas mediante renderizado WPF, no capturas del escritorio.");
        VerifyOperationsUi(output, log);
        VerifyNextUi(output,log);
        VerifyDesignUi(output,log);
        VerifyImportUi(output,log);
        VerifyUpdateCheckUi(output,log);
        File.WriteAllLines(Path.Combine(output, "ui-verification.txt"), log);
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}
