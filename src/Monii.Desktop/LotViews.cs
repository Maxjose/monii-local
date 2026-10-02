using System.Windows;
using System.Windows.Controls;
using Monii.Application;
using Monii.Domain;
namespace Monii.Desktop;
public partial class MainWindow
{
    private (TextBox? Code,DatePicker? Expiry) LotFields(StackPanel panel)
    {
        if(!service.Settings.Lots)return (null,null);
        var code=Field(panel,"Código de lote (opcional; obligatorio si tiene vencimiento)","");
        panel.Children.Add(Text("Fecha de vencimiento (opcional)"));var expiry=new DatePicker { Margin=new Thickness(0,4,0,8) };panel.Children.Add(expiry);
        return (code,expiry);
    }
    private static LotReceipt ReadLot((TextBox? Code,DatePicker? Expiry) fields) => new(fields.Code?.Text.Trim()??"",fields.Expiry?.SelectedDate is { } date?DateOnly.FromDateTime(date):null);
    private string SaleLotDetails(Sale sale)
    {
        var moves=operations.State.Stock.Where(m=>m.DocumentId==sale.Id&&m.Quantity<0&&m.Reason=="Venta"&&m.LotCode.Length>0).ToList();
        if(moves.Count==0)return "";
        return "\nLotes entregados:\n"+string.Join("\n",moves.Select(m=>$"{sale.Lines.Single(l=>l.ProductId==m.ProductId).Name} · Lote {m.LotCode} · {-m.Quantity:0.###} · Vence {m.Expiry?.ToString("dd/MM/yyyy")??"Sin vencimiento"}"));
    }
    private void ShowLots()
    {
        var panel=new StackPanel();panel.Children.Add(Text("Los vencidos no se venden. Se consumen primero los lotes con vencimiento más próximo.",14));
        var search=Field(panel,"Buscar producto, código o lote","");
        var grid=Table(("Producto","Name",3),("Código","ProductCode",1),("Lote","Code",1),("Vence","Expiry",1),("Cantidad","Quantity",1),("Estado","Status",1));grid.Height=350;
        var includeEmpty=Check(panel,"Incluir lotes sin existencias",false);
        void Reload()
        {
            var state=operations.State;var rows=service.Products(includeInactive:true).SelectMany(p=>OperationsService.Lots(state,p.Id).Select(l=>new { p.Name,ProductCode=p.Code,Code=l.Code.Length==0?"Sin lote":l.Code,ExpiryDate=l.Expiry,Expiry=l.Expiry?.ToString("dd/MM/yyyy")??"—",l.Quantity,Status=l.Code.Length==0?"Sin clasificar":l.Status }));
            grid.ItemsSource=rows.Where(l=>(includeEmpty.IsChecked==true||l.Quantity>0)&&$"{l.Name} {l.ProductCode} {l.Code}".Contains(search.Text,StringComparison.OrdinalIgnoreCase)).OrderBy(l=>l.ExpiryDate??DateOnly.MaxValue).ToList();
        }
        search.TextChanged+=(_,_)=>Reload();includeEmpty.Checked+=(_,_)=>Reload();includeEmpty.Unchecked+=(_,_)=>Reload();panel.Children.Add(grid);Reload();
        Form("Lotes y vencimientos",panel,()=>{},"Cerrar",1000);
    }
}
