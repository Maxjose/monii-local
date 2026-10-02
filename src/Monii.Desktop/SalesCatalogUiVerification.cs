using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Monii.Domain;

namespace Monii.Desktop;
public partial class MainWindow
{
    private void VerifySalesCatalogUi(Action<bool,string> assert,Action<string> capture)
    {
        var unit=new Product { Code="CAT-UNIT",Name="Unidad catálogo",PriceUsd=2,Unit="Unidad" };
        var weight=new Product { Code="CAT-KG",Name="Peso catálogo",PriceUsd=4,Unit="Kg" };
        var inactive=new Product { Code="CAT-INACTIVE",Name="Inactivo catálogo",PriceUsd=9,Active=false };
        service.Save(unit);service.Save(weight);service.Save(inactive);Navigate("Ventas");UpdateLayout();
        assert(!Descendants(PageContent).OfType<ComboBox>().Any(),"Venta no tiene desplegable de productos ni cantidad externa");
        Exception? failure=null;var timer=new DispatcherTimer { Interval=TimeSpan.FromMilliseconds(120) };
        timer.Tick+=(_,_)=>
        {
            timer.Stop();var dialog=OwnedWindows.Cast<Window>().Single();
            try
            {
                dialog.UpdateLayout();var search=Descendants(dialog).OfType<TextBox>().Single(t=>t.Name=="CatalogSearch");
                var grid=Descendants(dialog).OfType<DataGrid>().Single();search.Text="catálogo";dialog.UpdateLayout();
                assert(grid.Items.Count==2,"Catálogo filtra al escribir y excluye productos inactivos");
                grid.SelectAll();Descendants(dialog).OfType<Button>().Single(b=>b.Content?.ToString()=="Agregar seleccionados").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                assert(dialog.IsVisible&&saleCart.Count==2,"Selección múltiple agrega productos y mantiene catálogo abierto");
                search.Text="CAT-KG";assert(grid.Items.Count==1,"Catálogo busca también por código");grid.SelectedIndex=0;
                Descendants(dialog).OfType<Button>().Single(b=>b.Content?.ToString()=="Agregar seleccionados").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                assert(saleCart.Single(l=>l.ProductId==weight.Id).Quantity==2,"Volver a elegir producto aumenta su misma línea");
            }
            catch(Exception error) { failure=error; }
            finally { dialog.Close(); }
        };
        timer.Start();Descendants(PageContent).OfType<Button>().Single(b=>b.Name=="BrowseSaleProducts").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        if(failure is not null)throw failure;UpdateLayout();
        SaleQuantityCell Cell(Guid id)
        {
            var grid=Descendants(PageContent).OfType<DataGrid>().First(g=>g.ItemsSource==saleCart);grid.ScrollIntoView(saleCart.Single(l=>l.ProductId==id));UpdateLayout();
            return Descendants(grid).OfType<SaleQuantityCell>().Single(c=>c.DataContext is CartLine row&&row.ProductId==id);
        }
        void Press(Guid id,string label) { Descendants(Cell(id)).OfType<Button>().Single(b=>b.Content?.ToString()==label).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));UpdateLayout(); }
        void Write(Guid id,string value)
        {
            var input=Descendants(Cell(id)).OfType<TextBox>().Single();input.Text=value;
            input.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(input)!,0,Key.Enter) { RoutedEvent=Keyboard.KeyDownEvent });UpdateLayout();
        }
        Press(unit.Id,"+");assert(saleCart.Single(l=>l.ProductId==unit.Id).Quantity==2,"Más aumenta cantidad en la tabla");
        Press(unit.Id,"−");assert(saleCart.Single(l=>l.ProductId==unit.Id).Quantity==1,"Menos disminuye cantidad en la tabla");
        Write(unit.Id,"3");assert(saleCart.Single(l=>l.ProductId==unit.Id).Quantity==3,"Cantidad escrita se confirma con Enter");
        Write(unit.Id,"0,5");assert(saleCart.Single(l=>l.ProductId==unit.Id).Quantity==3,"Unidad rechaza cantidades fraccionarias sin modificar carrito");
        Write(weight.Id,"1,25");assert(saleCart.Single(l=>l.ProductId==weight.Id).Quantity==1.25m&&saleCart.Sum(l=>l.Total)==11,"Cantidad decimal actualiza total de línea y subtotal");
        capture("ventas-cantidades-catalogo.png");
        Press(unit.Id,"−");Press(unit.Id,"−");Press(unit.Id,"−");assert(saleCart.All(l=>l.ProductId!=unit.Id),"Menos retira línea al llegar a cero");
        saleCart.Clear();service.Save(unit with { Active=false });service.Save(weight with { Active=false });Navigate("Ventas");UpdateLayout();
    }
}
