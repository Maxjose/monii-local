using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Monii.Domain;

namespace Monii.Desktop;

public partial class MainWindow
{
    private void OpenSaleCatalog(string initialSearch,Action<Product> add)
    {
        var products=service.Products();
        var window=new Window { Title="Monii · Productos para la venta",Owner=this,Width=Math.Min(900,SystemParameters.WorkArea.Width-60),Height=Math.Min(700,SystemParameters.WorkArea.Height-60),WindowStartupLocation=WindowStartupLocation.CenterOwner };
        window.SetResourceReference(Window.BackgroundProperty,"ThemeSurface");
        var panel=new DockPanel { Margin=new Thickness(24) };window.Content=panel;
        var header=new StackPanel();header.Children.Add(Text("Listado de productos",24));
        header.Children.Add(Text("Selecciona uno o varios productos con Ctrl o Mayús y agrégalos al carrito. También puedes agregar uno con doble clic.",13));
        var search=Field(header,"Buscar nombre, código o categoría",initialSearch);search.Name="CatalogSearch";
        var count=Text("",13);header.Children.Add(count);DockPanel.SetDock(header,Dock.Top);panel.Children.Add(header);
        var grid=Table(("Código","Code",1),("Producto","Name",3),("Categoría","Category",2),("Precio USD","PriceUsd",1));grid.SelectionMode=DataGridSelectionMode.Extended;
        void Filter() { var query=search.Text.Trim();var rows=products.Where(p=>string.Join(" ",p.Code,p.Name,p.Category,p.Brand,p.Reference).Contains(query,StringComparison.OrdinalIgnoreCase)).ToList();grid.ItemsSource=rows;count.Text=$"{rows.Count} productos · {saleCart.Sum(l=>l.Quantity):0.###} en el carrito"; }
        var footer=new StackPanel();var status=Text("",13);footer.Children.Add(status);
        void AddSelected()
        {
            var selected=grid.SelectedItems.Cast<Product>().ToArray();if(selected.Length==0) { status.Text="Selecciona al menos un producto.";return; }
            foreach(var product in selected)add(product);
            grid.UnselectAll();status.Text=$"{selected.Length} productos agregados. Puedes seguir eligiendo o cerrar el listado.";count.Text=$"{grid.Items.Count} productos · {saleCart.Sum(l=>l.Quantity):0.###} en el carrito";
        }
        var actions=new WrapPanel();actions.Children.Add(Button("Agregar seleccionados",()=>Safe(AddSelected)));actions.Children.Add(Button("Cerrar listado",()=>window.Close()));footer.Children.Add(actions);
        DockPanel.SetDock(footer,Dock.Bottom);panel.Children.Add(footer);panel.Children.Add(grid);
        search.TextChanged+=(_,_)=>Filter();grid.MouseDoubleClick+=(_,e)=> { if(e.OriginalSource is DependencyObject source&&FindProductRow(source) is not null)Safe(AddSelected); };
        grid.KeyDown+=(_,e)=> { if(e.Key==Key.Enter) { Safe(AddSelected);e.Handled=true; } };
        Filter();window.Loaded+=(_,_)=>search.Focus();window.ShowDialog();
    }
    private static DataGridRow? FindProductRow(DependencyObject source)
    {
        while(source is not null) { if(source is DataGridRow row)return row;source=source is FrameworkContentElement content?content.Parent:System.Windows.Media.VisualTreeHelper.GetParent(source); }return null;
    }
}

public sealed class SaleQuantityCell : ContentControl
{
    public static readonly DependencyProperty ChangeProperty=DependencyProperty.Register("Change",typeof(Action<CartLine,decimal>),typeof(SaleQuantityCell));
    public static readonly DependencyProperty RemoveProperty=DependencyProperty.Register("Remove",typeof(Action<CartLine>),typeof(SaleQuantityCell));
    public SaleQuantityCell()
    {
        var panel=new StackPanel { Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center };
        var minus=new Button { Content="−",Width=32,Padding=new Thickness(0),Margin=new Thickness(2),ToolTip="Disminuir cantidad" };
        var input=new TextBox { Name="CartQuantity",Width=62,Padding=new Thickness(4),Margin=new Thickness(2),TextAlignment=TextAlignment.Center,VerticalContentAlignment=VerticalAlignment.Center };
        var plus=new Button { Content="+",Width=32,Padding=new Thickness(0),Margin=new Thickness(2),ToolTip="Aumentar cantidad" };
        System.Windows.Automation.AutomationProperties.SetName(input,"Cantidad del producto");
        panel.Children.Add(minus);panel.Children.Add(input);panel.Children.Add(plus);Content=panel;
        void Refresh()=>input.Text=DataContext is CartLine row?row.Quantity.ToString("0.###",CultureInfo.GetCultureInfo("es-VE")):"";
        void Commit()
        {
            if(DataContext is not CartLine row)return;
            if(decimal.TryParse(input.Text.Trim().Replace(',','.'),NumberStyles.AllowDecimalPoint|NumberStyles.AllowLeadingSign,CultureInfo.InvariantCulture,out var value)&&value>0)
                ((Action<CartLine,decimal>?)GetValue(ChangeProperty))?.Invoke(row,value);
            else input.ToolTip="Escribe una cantidad mayor que cero.";
            Refresh();
        }
        DataContextChanged+=(_,_)=>Refresh();Loaded+=(_,_)=>Refresh();
        input.LostKeyboardFocus+=(_,_)=>Commit();input.KeyDown+=(_,e)=> { if(e.Key==Key.Enter) { Commit();e.Handled=true; } };
        plus.Click+=(_,_)=> { if(DataContext is CartLine row)((Action<CartLine,decimal>?)GetValue(ChangeProperty))?.Invoke(row,row.Quantity+1);Refresh(); };
        minus.Click+=(_,_)=> { if(DataContext is CartLine row) { if(row.Quantity<=1)((Action<CartLine>?)GetValue(RemoveProperty))?.Invoke(row);else ((Action<CartLine,decimal>?)GetValue(ChangeProperty))?.Invoke(row,row.Quantity-1); }Refresh(); };
    }
}
