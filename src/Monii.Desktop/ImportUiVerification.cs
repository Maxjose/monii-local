using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Monii.Desktop;
public partial class MainWindow
{
    private void VerifyImportUi(string output,List<string> log)
    {
        void Assert(bool okay,string label) { if(!okay) throw new InvalidOperationException("UI importación: "+label); log.Add("OK IMPORTACIÓN UI: "+label); }
        void Capture(Window window,string name)
        {
            window.UpdateLayout(); Dispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);
            var image=new RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,PixelFormats.Pbgra32); image.Render(window);
            var encoder=new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); using var stream=File.Create(Path.Combine(output,name)); encoder.Save(stream);
        }
        void Modal(string path,string button,Action<Window> inspect)
        {
            Exception? failure=null; var timer=new DispatcherTimer { Interval=TimeSpan.FromMilliseconds(120) };
            timer.Tick+=(_,_)=> { timer.Stop(); var window=OwnedWindows.Cast<Window>().Single(); try { window.UpdateLayout(); inspect(window); Descendants(window).OfType<Button>().Single(b=>b.Content?.ToString()==button).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent)); } catch(Exception e) { failure=e; } if(window.IsVisible) { failure??=new InvalidOperationException("Importación no se cerró: "+string.Join(" ",Descendants(window).OfType<TextBlock>().Select(t=>t.Text))); window.Close(); } };
            timer.Start(); StartProductImport(path); if(failure is not null) throw failure;
        }
        string Csv(string name,string data) { var path=Path.Combine(output,name); File.WriteAllText(path,data,new UTF8Encoding(true)); return path; }
        var source=Csv("importacion-ui.csv","Codigo;Nombre;Categoria;CostoUSD;PrecioUSD\n000UI01;Producto importado;Importación UI;2,25;5\n000UI02;Otro importado;Importación UI;1;3\n");
        var prior=storage.GetProducts().Count;
        Modal(source,"Cancelar",window=>
        {
            Assert(!Descendants(window).OfType<Button>().Single(b=>b.Content?.ToString()=="Importar productos").IsEnabled,"Categoría faltante bloquea importación por defecto");
            Assert(storage.GetProducts().Count==prior,"Vista previa no escribe productos");
        });
        Assert(storage.GetProducts().Count==prior,"Cancelar importación conserva catálogo");
        Modal(source,"Importar productos",window=>
        {
            Descendants(window).OfType<CheckBox>().Single(c=>c.Content?.ToString()=="Crear categorías faltantes").IsChecked=true;
            Assert(Descendants(window).OfType<Button>().Single(b=>b.Content?.ToString()=="Importar productos").IsEnabled,"Opción explícita permite crear categoría e importar");
            Assert(Descendants(window).OfType<DataGrid>().Single().Items.Count==2,"Vista previa presenta todas las filas"); Capture(window,"importacion-valida.png");
        });
        Assert(storage.GetProducts().Count==prior+2&&storage.GetProducts().Any(p=>p.Code=="000UI01"&&p.CostUsd==2.25m),"Importación por formulario conserva ceros y costo");
        Assert(Directory.GetFiles(Path.Combine(Path.GetDirectoryName(databasePath)!,"backups"),"monii-before-import-*.db").Length==1,"Importar crea respaldo previo real");
        var update=Csv("actualizacion-ui.csv","Codigo;Nombre;CostoUSD;PrecioUSD\n000UI01;Producto actualizado;3;8\n");
        var id=storage.GetProducts().Single(p=>p.Code=="000UI01").Id;
        Modal(update,"Importar productos",window=>
        {
            Assert(!Descendants(window).OfType<Button>().Single(b=>b.Content?.ToString()=="Importar productos").IsEnabled,"Actualizar códigos existentes requiere consentimiento explícito");
            Descendants(window).OfType<CheckBox>().Single(c=>c.Content?.ToString()=="Actualizar códigos existentes").IsChecked=true;
        });
        Assert(storage.GetProducts().Single(p=>p.Id==id).PriceUsd==8&&storage.GetProducts().Count==prior+2,"Actualizar desde interfaz conserva ID y no duplica");
        var invalid=Csv("errores-ui.csv","Codigo;Nombre;CostoUSD;PrecioUSD\nDUP;Uno;1;2\ndup;Dos;1;2\n");
        Modal(invalid,"Cancelar",window=>
        {
            Assert(!Descendants(window).OfType<Button>().Single(b=>b.Content?.ToString()=="Importar productos").IsEnabled&&Descendants(window).OfType<TextBlock>().Any(t=>t.Text.Contains("2 errores")),"Errores por fila bloquean importación completa"); Capture(window,"importacion-errores.png");
        });
        Navigate("Inicio");
    }
}
