using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Monii.Application;
using Monii.Domain;
using Monii.Infrastructure;

namespace Monii.Desktop;
public partial class MainWindow
{
    private void StartProductImport(string? initialFile=null)
    {
        storage.Require(Permission.Products);
        var dialog=new Window { Title="Monii · Importar productos",Owner=this,Width=Math.Min(1120,SystemParameters.WorkArea.Width-40),Height=Math.Min(780,SystemParameters.WorkArea.Height-40),MinWidth=860,MinHeight=560,WindowStartupLocation=WindowStartupLocation.CenterOwner,Background=Theme.Resource("ThemeCanvas") };
        var layout=new DockPanel { Margin=new Thickness(24) }; var header=new StackPanel();
        header.Children.Add(Text("Importar productos",24));
        header.Children.Add(Text("Excel .xlsx: primera hoja visible. CSV UTF-8: separador ; , o tabulador. Máximo 5.000 productos. Usa las columnas de la plantilla.",13,"#64748B"));
        header.Children.Add(Text("Costos y precios en USD. No modifica existencias. Los códigos de Excel deben estar como texto; no se admiten fórmulas ni cambios de unidad de productos existentes.",13,"#64748B"));
        var source=Text("Selecciona un archivo para revisar sus productos.",13); header.Children.Add(source);
        var options=new WrapPanel();
        var update=new CheckBox { Content="Actualizar códigos existentes",Margin=new Thickness(0,6,24,6) };
        var create=new CheckBox { Content="Crear categorías faltantes",Margin=new Thickness(0,6,0,6) }; options.Children.Add(update); options.Children.Add(create); header.Children.Add(options);
        var summary=Text("",14); header.Children.Add(summary);
        var error=Text("",13,"#B91C1C"); header.Children.Add(error);
        var grid=Table(("Fila","Number",.5),("Acción","Action",1),("Código","Code",1),("Producto","Name",2),("Categoría","Category",1),("Costo USD","Cost",1),("Precio USD","Price",1),("Error","Error",3));
        grid.RowHeight=64;
        var minimums=new[]{50d,75,100,160,120,80,80,220};
        for(var i=0;i<grid.Columns.Count;i++)
        {
            var column=(DataGridTextColumn)grid.Columns[i]; column.MinWidth=minimums[i];
            column.ElementStyle=new Style(typeof(TextBlock)) { Setters={new Setter(TextBlock.TextTrimmingProperty,TextTrimming.CharacterEllipsis),new Setter(TextBlock.TextWrappingProperty,i==7?TextWrapping.Wrap:TextWrapping.NoWrap),new Setter(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center),new Setter(FrameworkElement.MarginProperty,new Thickness(4,0,4,0))} };
            column.CellStyle=new Style(typeof(DataGridCell)) { Setters={new Setter(UIElement.ClipToBoundsProperty,true),new Setter(ToolTipProperty,new System.Windows.Data.Binding(((System.Windows.Data.Binding)column.Binding).Path.Path))} };
        }
        ProductImportFile? file=null; ProductImportPreview? preview=null;
        var actions=new WrapPanel(); var save=Button("Importar productos",()=>
        {
            try
            {
                if(preview is null||!preview.Valid) throw new ArgumentException("Corrige el archivo y vuelve a validarlo.");
                var backupDirectory=Path.Combine(Path.GetDirectoryName(databasePath)!,"backups"); Directory.CreateDirectory(backupDirectory);
                var backup=storage.Backup(Path.Combine(backupDirectory,"monii-before-import-"+DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N")+".db"));
                var count=storage.ImportProducts(preview); dialog.Close(); Navigate("Productos"); Status.Text=$"{count} productos importados. Respaldo previo: {backup}";
            }
            catch(Exception e) { error.Text=FriendlyError(e); ((Button)actions.Children[0]).IsEnabled=false; }
        });

        void Validate()
        {
            save.IsEnabled=false; preview=null; error.Text="";
            if(file is null) return;
            try
            {
                preview=ProductImport.Preview(file,storage.GetProducts(),storage.GetCategories(),update.IsChecked==true,create.IsChecked==true);
                grid.ItemsSource=preview.Rows; var failures=preview.Rows.Count(r=>r.Error.Length>0);
                summary.Text=$"{preview.Rows.Count} filas · {preview.Rows.Count(r=>r.Action=="Crear")} nuevos · {preview.Rows.Count(r=>r.Action=="Actualizar")} existentes · {preview.NewCategories.Count} categorías nuevas · {failures} errores";
                save.IsEnabled=preview.Valid;
                if(failures>0) error.Text="No se guardará ninguna fila mientras haya errores. Corrige el archivo y vuelve a seleccionarlo.";
            }
            catch(Exception e) { grid.ItemsSource=null; summary.Text=""; error.Text=FriendlyError(e); }
        }
        void Load(string path)
        {
            file=null; preview=null; grid.ItemsSource=null; save.IsEnabled=false; summary.Text="";
            try { file=ProductImportReader.Read(path); source.Text=Path.GetFileName(path)+" · "+file.Sheet; Validate(); }
            catch(Exception e) { source.Text=Path.GetFileName(path); error.Text=FriendlyError(e); }
        }
        var fileButtons=new WrapPanel();
        fileButtons.Children.Add(Button("Seleccionar archivo",()=> { var picker=new OpenFileDialog { Filter="Productos (*.xlsx;*.csv)|*.xlsx;*.csv",Title="Importar catálogo" }; if(picker.ShowDialog(dialog)==true) Load(picker.FileName); }));
        fileButtons.Children.Add(Button("Guardar plantilla CSV",()=>
        {
            var picker=new SaveFileDialog { Filter="CSV UTF-8 (*.csv)|*.csv",FileName="plantilla-productos-monii.csv" };
            if(picker.ShowDialog(dialog)==true) try { File.WriteAllText(picker.FileName,ProductImport.Template,new UTF8Encoding(true)); error.Text=""; source.Text="Plantilla guardada. Sustituye el producto de ejemplo y conserva los códigos como texto."; } catch(Exception e) { error.Text=FriendlyError(e); }
        }));
        fileButtons.Children.Add(Button("Volver a validar",Validate)); header.Children.Insert(2,fileButtons);
        update.Checked+=(_,_)=>Validate(); update.Unchecked+=(_,_)=>Validate(); create.Checked+=(_,_)=>Validate(); create.Unchecked+=(_,_)=>Validate();
        actions.Children.Add(save); actions.Children.Add(Button("Cancelar",()=>dialog.Close()));
        var footer=new StackPanel(); footer.Children.Add(Text("Al importar se guardan todas las filas juntas y se crea un respaldo previo. Actualizar conserva el identificador y el historial; los campos opcionales ausentes se conservan.",12,"#64748B")); footer.Children.Add(actions);
        DockPanel.SetDock(header,Dock.Top); DockPanel.SetDock(footer,Dock.Bottom); layout.Children.Add(header); layout.Children.Add(footer); layout.Children.Add(grid); dialog.Content=layout;
        save.IsEnabled=false; if(initialFile is not null) Load(initialFile); dialog.ShowDialog();
    }
}
