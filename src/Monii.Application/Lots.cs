using Monii.Domain;
namespace Monii.Application;
public sealed partial class OperationsService
{
    public static IReadOnlyList<LotBalance> Lots(OperationsState state,Guid product) => state.Stock.Where(m=>m.ProductId==product).GroupBy(m=>(Code:m.LotCode.ToUpperInvariant(),m.Expiry)).Select(g=>new LotBalance(product,g.Key.Code,g.Key.Expiry,g.Sum(m=>m.Quantity))).ToList();
    private static LotReceipt ValidateLot(OperationsState state,Guid product,string code,DateOnly? expiry)
    {
        code=code.Trim().ToUpperInvariant();
        if(code.Length>80||code.Any(char.IsControl)||expiry is not null&&code.Length==0)throw new ArgumentException("Indica un código de lote válido (máximo 80 caracteres) para registrar vencimiento.");
        if(code.Length>0&&state.Stock.Any(m=>m.ProductId==product&&m.LotCode.Equals(code,StringComparison.OrdinalIgnoreCase)&&m.Expiry!=expiry))throw new ArgumentException("Este lote ya tiene otra fecha de vencimiento. Conserva la fecha registrada o usa otro código.");
        return new(code,expiry);
    }
    private static void MoveExact(OperationsState state,Guid product,decimal quantity,string reason,Guid? doc,string code,DateOnly? expiry,Guid? source=null)
    {
        var lot=ValidateLot(state,product,code,expiry);
        var balance=Lots(state,product).FirstOrDefault(l=>l.Code==lot.Code&&l.Expiry==lot.Expiry)?.Quantity??0;
        if(balance+quantity<0)throw new ArgumentException("Existencias insuficientes en el lote seleccionado.");
        state.Stock.Add(new(Guid.NewGuid(),product,DateTimeOffset.UtcNow,quantity,reason,doc) { LotCode=lot.Code,Expiry=lot.Expiry,SourceMoveId=source });
    }
    private static void ConsumeLots(OperationsState state,Guid product,decimal quantity,string reason,Guid? doc,bool sale)
    {
        var today=DateOnly.FromDateTime(DateTime.Today);
        var lots=Lots(state,product).Where(l=>l.Quantity>0&&(!sale||l.Expiry is null||l.Expiry>=today)).OrderBy(l=>l.Expiry??DateOnly.MaxValue).ThenBy(l=>l.Code.Length==0?1:0).ThenBy(l=>l.Code).ToList();
        if(lots.Sum(l=>l.Quantity)<quantity)throw new ArgumentException(sale?"Existencias vendibles insuficientes. Las unidades vencidas no pueden venderse.":"Existencias insuficientes.");
        foreach(var lot in lots) { var take=Math.Min(quantity,lot.Quantity);MoveExact(state,product,-take,reason,doc,lot.Code,lot.Expiry);quantity-=take;if(quantity==0)break; }
    }
    private static void RestoreSaleLots(OperationsState state,Sale sale,Guid product,decimal quantity,string reason,Guid document)
    {
        var sources=state.Stock.Where(m=>m.DocumentId==sale.Id&&m.ProductId==product&&m.Quantity<0&&m.Reason=="Venta").ToList();
        foreach(var move in sources)
        {
            var available=-move.Quantity-state.Stock.Where(m=>m.SourceMoveId==move.Id).Sum(m=>m.Quantity);
            var take=Math.Min(quantity,available);if(take<=0)continue;
            MoveExact(state,product,take,reason,document,move.LotCode,move.Expiry,move.Id);quantity-=take;if(quantity==0)return;
        }
        throw new ArgumentException("No se pudo reconstruir el lote original de la venta.");
    }
    public void ClassifyLot(Guid productId,decimal quantity,string code,DateOnly? expiry,string reason) => store.Transact((state,products,settings)=>
    {
        if(!settings.Inventory||!settings.Lots)throw new ArgumentException("Activa inventario y lotes.");
        Reason(reason);Quantity(Product(products,productId),quantity);var lot=ValidateLot(state,productId,code,expiry);
        if(lot.Code.Length==0)throw new ArgumentException("Indica el código del lote.");
        MoveExact(state,productId,-quantity,"Clasificación: "+reason,null,"",null);MoveExact(state,productId,quantity,"Clasificación: "+reason,null,lot.Code,lot.Expiry);return true;
    },"Ajuste de inventario",NetworkJson.Command("ClassifyLot",new { productId,quantity,code,expiry,reason }));
}
