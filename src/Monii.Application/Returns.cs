using Monii.Domain;

namespace Monii.Application;

public sealed partial class OperationsService
{
    public static decimal NetTotal(OperationsState state,Sale sale) => sale.Voided?0:sale.Total-state.Returns.Where(r=>r.SaleId==sale.Id).Sum(r=>r.Total);
    public static decimal NetMargin(OperationsState state,Sale sale) => sale.Voided?0:Margin(sale)-state.Returns.Where(r=>r.SaleId==sale.Id).Sum(r=>r.Total-r.CostReduction);
    public SaleReturn PreviewReturn(Guid saleId,IReadOnlyList<(Guid ProductId,decimal Quantity)> items) => CalculateReturn(State,saleId,items,"Vista previa",PaymentMethod.Efectivo);
    private static SaleReturn CalculateReturn(OperationsState state,Guid saleId,IReadOnlyList<(Guid ProductId,decimal Quantity)> items,string reason,PaymentMethod refundMethod)
    {
        Reason(reason); if(!Enum.IsDefined(refundMethod)) throw new ArgumentException("Medio de devolución inválido.");
        var sale=state.Sales.SingleOrDefault(s=>s.Id==saleId)??throw new ArgumentException("Venta inexistente.");
        if(sale.Voided||items.Count==0||items.Select(i=>i.ProductId).Distinct().Count()!=items.Count) throw new ArgumentException("Selecciona cantidades de una venta vigente, sin productos duplicados.");
        var previous=state.Returns.Where(r=>r.SaleId==saleId).ToList(); var lines=new List<DocumentLine>(); decimal total=0,costReduction=0;
        // Cumulative allocation guarantees that all partial returns sum to the exact discounted total.
        var allocations=new Dictionary<Guid,decimal>(); decimal allocated=0,gross=0; var subtotal=sale.Lines.Sum(l=>l.Total);
        for(var index=0;index<sale.Lines.Count;index++)
        {
            var original=sale.Lines[index]; gross+=original.Total; var amount=index==sale.Lines.Count-1?sale.Total-allocated:subtotal==0?0:Money(sale.Total*gross/subtotal)-allocated;
            allocations[original.ProductId]=amount; allocated+=amount;
        }
        foreach(var item in items)
        {
            var line=sale.Lines.SingleOrDefault(l=>l.ProductId==item.ProductId)??throw new ArgumentException("Producto ajeno a la venta.");
            Quantity(new Product { Unit=line.Unit },item.Quantity);
            var already=previous.SelectMany(r=>r.Lines).Where(l=>l.ProductId==item.ProductId).Sum(l=>l.Quantity);
            if(already+item.Quantity>line.Quantity) throw new ArgumentException("La cantidad supera lo pendiente de devolución.");
            var before=Money(allocations[item.ProductId]*already/line.Quantity); var after=Money(allocations[item.ProductId]*(already+item.Quantity)/line.Quantity);
            total+=after-before; costReduction+=Money(line.Cost*(already+item.Quantity))-Money(line.Cost*already); lines.Add(line with { Quantity=item.Quantity });
        }
        var reduction=Math.Min(Debt(state,sale),total); var refund=total-reduction;
        var document=new SaleReturn { SaleId=saleId,Reason=reason.Trim(),Lines=lines,Total=total,CostReduction=costReduction,DebtReduction=reduction,RefundUsd=refund,RefundMethod=refundMethod };
        return document;
    }
    public SaleReturn ReturnSale(Guid saleId,IReadOnlyList<(Guid ProductId,decimal Quantity)> items,string reason,PaymentMethod refundMethod=PaymentMethod.Efectivo) => store.Transact((state,products,settings)=>
    {
        var document=CalculateReturn(state,saleId,items,reason,refundMethod);
        var sale=state.Sales.Single(s=>s.Id==saleId); var lines=document.Lines; var refund=document.RefundUsd;
        if(sale.StockAffected) foreach(var line in lines) Move(state,line.ProductId,line.Quantity,"Devolución: "+reason,document.Id);
        if(refund>0)
        {
            if(!settings.Cash) throw new ArgumentException("Activa y abre caja para registrar el reintegro USD de una devolución.");
            Cash(state,new(Currency.USD,refundMethod,-refund),-refund,"Devolución de venta",document.Id);
        }
        state.Returns.Add(document); return document;
    },"Devolución de venta",NetworkJson.Command("ReturnSale",new { saleId, items, reason, refundMethod }));
}
