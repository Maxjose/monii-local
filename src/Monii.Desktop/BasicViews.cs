using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Monii.Domain;
namespace Monii.Desktop;
public partial class MainWindow
{
    private UIElement BasicHome()
    {
        var panel=new StackPanel();
        if(storage.BackupWarning is { } warning)panel.Children.Add(Text(warning,14,"#B91C1C"));
        var intro=Text("Registra tus productos, organiza sus categorías y consulta los precios.",14,"#64748B");intro.Margin=new Thickness(0,0,0,8);panel.Children.Add(intro);
        var cards=new UniformGrid { Columns=2,Margin=new Thickness(0,20,0,20) };
        cards.Children.Add(Card("PRODUCTOS ACTIVOS",service.Products().Count.ToString(),"Tu catálogo de productos"));
        cards.Children.Add(Card("CATEGORÍAS",storage.GetCategories().Count(c=>c.Active).ToString(),"Organiza tu listado"));panel.Children.Add(cards);
        var actions=new WrapPanel();
        if(storage.Can(Permission.Products)) {
            actions.Children.Add(Button("Registrar un producto",()=>EditProduct(null,()=>Navigate("Inicio"))));
            actions.Children.Add(Button("Gestionar categorías",()=>Navigate("Categorías")));
        }
        actions.Children.Add(Button("Ver listado de precios",()=>Navigate("Listado de precios")));panel.Children.Add(actions);
        return Scroll(panel);
    }
    private UIElement PriceList()
    {
        var header=new StackPanel();header.Children.Add(Text("Consulta los precios de tus productos activos.",14,"#64748B"));
        var search=Field(header,"Buscar por nombre o código","");header.Children.Add(Text("Categoría"));
        var categories=new ComboBox { ItemsSource=service.Products().Select(p=>p.Category.Length==0?"Sin categoría":p.Category).Distinct().OrderBy(c=>c).Prepend("Todas las categorías").ToList(),SelectedIndex=0 };header.Children.Add(categories);
        var grid=new DataGrid();var count=Text("",12,"#64748B");header.Children.Add(count);
        void Reload()
        {
            var settings=service.Settings;grid.Columns.Clear();grid.Columns.Add(Column("Producto","Name",3));grid.Columns.Add(Column("Código","Code"));grid.Columns.Add(Column("Categoría","Category"));grid.Columns.Add(Column("Precio USD","PriceUsd",1,"{0:N2}"));
            if(settings.ShowBcv)grid.Columns.Add(ConvertedColumn("Bs BCV",settings.BcvRate));if(settings.ShowManualVes)grid.Columns.Add(ConvertedColumn("Bs manual",settings.ManualVesRate));if(settings.ShowCop)grid.Columns.Add(ConvertedColumn("COP",settings.CopRate));
            var category=categories.SelectedItem?.ToString();var rows=service.Products(search.Text).Where(p=>category is null or "Todas las categorías"||(p.Category.Length==0?"Sin categoría":p.Category)==category).ToList();grid.ItemsSource=rows;count.Text=$"{rows.Count} productos · Precios actualizados con las tasas configuradas";
        }
        search.TextChanged+=(_,_)=>Reload();categories.SelectionChanged+=(_,_)=>Reload();header.Children.Add(Button("Actualizar listado",()=>Safe(Reload)));Reload();return Page(header,grid);
    }
    private void EditBasicProduct(Product? existing,Action saved)
    {
        var product=existing??new Product();var panel=new StackPanel();
        var name=Field(panel,"Nombre *",product.Name);panel.Children.Add(Text("Código / código de barras *"));
        var row=new Grid();row.ColumnDefinitions.Add(new ColumnDefinition());row.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto });
        var code=new TextBox { Text=product.Code,Margin=new Thickness(0,4,8,12) };row.Children.Add(code);var scan=Button("Escanear",()=>ScanProductCode(code));Grid.SetColumn(scan,1);row.Children.Add(scan);panel.Children.Add(row);
        panel.Children.Add(Text("Categoría"));var choices=storage.GetCategories().Where(c=>c.Active||c.Name==product.Category).Prepend(new Category(Guid.Empty,"Sin categoría")).ToList();
        var category=new ComboBox { ItemsSource=choices,DisplayMemberPath="Name",SelectedItem=choices.FirstOrDefault(c=>product.Category.Length==0?c.Id==Guid.Empty:c.Name==product.Category) };panel.Children.Add(category);
        var price=Field(panel,"Precio USD *",product.PriceUsd.ToString("0.00",UiCulture));
        var active=Check(panel,"Producto activo",product.Active);
        Form(existing is null?"Nuevo producto":"Editar producto",panel,()=> {
            service.Save(product with { Name=name.Text,Code=code.Text,Category=category.SelectedItem is Category selected&&selected.Id!=Guid.Empty?selected.Name:"",PriceUsd=ParseNumber(price.Text,"precio"),Active=active.IsChecked==true });saved();Status.Text="Producto guardado.";
        },"Guardar producto",520);
    }
}
