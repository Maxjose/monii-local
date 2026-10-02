using System.Windows.Controls;
using Monii.Domain;
using Monii.Application;

namespace Monii.Desktop;

public partial class MainWindow
{
    private void ReturnForm(Sale sale,Action refresh)
    {
        var state=operations.State; var panel=new StackPanel(); panel.Children.Add(Text($"Venta #{sale.Number} · {sale.CustomerName}\nSelecciona cantidades a devolver. Primero se reduce deuda pendiente; el excedente se reintegra en USD.",15));
        var fields=new List<(DocumentLine Line,TextBox Quantity)>();
        foreach(var line in sale.Lines)
        {
            var returned=state.Returns.Where(r=>r.SaleId==sale.Id).SelectMany(r=>r.Lines).Where(l=>l.ProductId==line.ProductId).Sum(l=>l.Quantity);
            var box=Field(panel,$"{line.Name} · pendiente {line.Quantity-returned:0.###} {line.Unit}","0"); fields.Add((line,box));
        }
        var reason=Field(panel,"Motivo de devolución",""); var method=Choice(panel,"Medio de reintegro USD",Enum.GetNames<PaymentMethod>(),"Efectivo");
        var preview=Text("Selecciona las cantidades a devolver.",18,"#0F766E"); panel.Children.Add(preview);
        void Preview()
        {
            try { var items=fields.Select(f=>(f.Line.ProductId,Quantity:ParseNumber(f.Quantity.Text,"cantidad"))).Where(i=>i.Quantity!=0).ToList(); var quote=operations.PreviewReturn(sale.Id,items); preview.Text=$"Total USD {quote.Total:N2} · Reducir deuda USD {quote.DebtReduction:N2} · Reintegrar USD {quote.RefundUsd:N2}"; }
            catch(ArgumentException e) { preview.Text=e.Message; }
        }
        foreach(var field in fields) field.Quantity.TextChanged+=(_,_)=>Preview();
        var previous=Text(string.Join("\n",state.Returns.Where(r=>r.SaleId==sale.Id).Select(r=>$"{r.At.ToLocalTime():g} · devolución USD {r.Total:N2} · deuda reducida {r.DebtReduction:N2} · reintegro {r.RefundUsd:N2} · {r.Reason}")),13,"#64748B"); panel.Children.Add(previous);
        Form("Devolución parcial",panel,()=>
        {
            var items=fields.Select(f=>(f.Line.ProductId,Quantity:ParseNumber(f.Quantity.Text,"cantidad"))).Where(i=>i.Quantity!=0).ToList();
            var result=operations.ReturnSale(sale.Id,items,reason.Text,Enum.Parse<PaymentMethod>(method.SelectedItem!.ToString()!)); refresh(); Status.Text=$"Devolución guardada · USD {result.Total:N2} · Deuda reducida {result.DebtReduction:N2} · Reintegro USD {result.RefundUsd:N2}";
        },"Registrar devolución");
    }
}
