using Monii.Domain;

namespace Monii.Application;

public interface IOperationsStore
{
    OperationsState ReadOperations();
    T Transact<T>(Func<OperationsState, IReadOnlyList<Product>, BusinessSettings, T> operation, string action);
}

public sealed partial class OperationsService(IOperationsStore store)
{
    public OperationsState State => store.ReadOperations();
    public static decimal Money(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);
    public static decimal Margin(Sale sale) => sale.Voided ? 0 : sale.Total - sale.Lines.Sum(l => Money(l.Quantity * l.Cost));
    public static decimal Stock(OperationsState state, Guid product) => state.Stock.Where(m => m.ProductId == product).Sum(m => m.Quantity);
    public static decimal Debt(OperationsState state, Sale sale) => sale.Voided ? 0 : sale.InitialDebt - state.Abonos.Where(a => a.SaleId == sale.Id && !a.Voided).Sum(a => a.Usd) - state.Returns.Where(r=>r.SaleId==sale.Id).Sum(r=>r.DebtReduction);
    public static RateSnapshot Rates(BusinessSettings settings) => new(settings.BcvRate, settings.ManualVesRate, settings.CopRate);
    public static void Amount(decimal value, bool allowZero = false)
    {
        if (value < 0 || (!allowZero && value == 0) || value > 999999999m || Money(value) != value) throw new ArgumentException("Importe inválido; hasta dos decimales, positivo y máximo 999.999.999.");
    }
    public static void Quantity(Product product, decimal quantity)
    {
        if (quantity <= 0 || quantity > 1000000 || decimal.Round(quantity, 3) != quantity || ((product.Unit is "Unidad" or "Caja") && quantity != decimal.Truncate(quantity)))
            throw new ArgumentException("Cantidad inválida: unidad/caja admite enteros; otras unidades hasta tres decimales.");
    }
    public static decimal Usd(Payment payment, BusinessSettings settings, bool allowZero = false)
    {
        Amount(payment.Amount, allowZero);
        if (!Enum.IsDefined(payment.Currency) || !Enum.IsDefined(payment.Method)) throw new ArgumentException("Moneda o medio inválido.");
        var enabled = payment.Currency switch { Currency.USD => true, Currency.VES_BCV => settings.ShowBcv, Currency.VES_Manual => settings.ShowManualVes, Currency.COP => settings.ShowCop, _ => false };
        if (!enabled) throw new ArgumentException("Activa esta moneda en configuración.");
        var rate = Rates(settings).For(payment.Currency);
        if (rate <= 0) throw new ArgumentException("Configura una tasa positiva.");
        var usd = Money(payment.Amount / rate);
        if (usd <= 0 && !allowZero) throw new ArgumentException("El pago convertido debe alcanzar 0,01 USD.");
        return usd;
    }
    private static Product Product(IReadOnlyList<Product> products, Guid id) => products.FirstOrDefault(p => p.Id == id && p.Active) ?? throw new ArgumentException("Producto inexistente o inactivo.");
    private static void Reason(string reason) { if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("Escribe un motivo o referencia."); }
    private static void Move(OperationsState state, Guid product, decimal quantity, string reason, Guid? doc)
    {
        if (Stock(state, product) + quantity < 0) throw new ArgumentException("Existencias insuficientes. No se permite stock negativo.");
        state.Stock.Add(new(Guid.NewGuid(), product, DateTimeOffset.UtcNow, quantity, reason, doc));
    }

    public void Adjust(Guid productId, decimal delta, string reason) => store.Transact((state, products, settings) =>
    {
        if (!settings.Inventory) throw new ArgumentException("Activa inventario para ajustar existencias.");
        Reason(reason); Quantity(Product(products, productId), Math.Abs(delta)); Move(state, productId, delta, reason.Trim(), null); return true;
    }, "Ajuste de inventario");

    public Sale Sell(IReadOnlyList<(Guid ProductId, decimal Quantity)> items, decimal discount, IReadOnlyList<Payment> payments, Guid? customerId = null, DateOnly? due = null) => store.Transact((state, products, settings) =>
    {
        if (items.Count == 0) throw new ArgumentException("Agrega productos a la venta.");
        var lines = items.GroupBy(i => i.ProductId).Select(group =>
        {
            var p = Product(products, group.Key); var qty = group.Sum(i => i.Quantity);
            foreach (var item in group) Quantity(p, item.Quantity);
            Quantity(p, qty); return new DocumentLine(p.Id, p.Name, p.Code, p.Unit, qty, p.PriceUsd, p.CostUsd);
        }).ToList();
        Amount(discount, true); var subtotal = lines.Sum(l => l.Total);
        if (discount > subtotal) throw new ArgumentException("El descuento supera el subtotal.");
        var total = subtotal - discount; var paid = payments.Sum(p => Usd(p, settings));
        var debt = Math.Max(0, total - paid); var change = Math.Max(0, paid - total);
        Contact? customer = null;
        if (customerId is { } id) customer = state.Customers.FirstOrDefault(c => c.Id == id && c.Active) ?? throw new ArgumentException("Cliente inexistente o inactivo.");
        if (debt > 0)
        {
            if (!settings.Credit || !settings.Customers || customer is null || due is null || due < DateOnly.FromDateTime(DateTime.Today)) throw new ArgumentException("Para crédito selecciona cliente activo, vencimiento válido y activa créditos/clientes.");
            var current = state.Sales.Where(s => s.CustomerId == customer.Id).Sum(s => Debt(state, s));
            if (current + debt > customer.CreditLimit) throw new ArgumentException("La venta supera el límite de crédito del cliente.");
        }
        var sale = new Sale { Number = state.Sales.Count + 1L, Lines = lines, Discount = discount, Total = total, Payments = payments.ToList(), ChangeUsd = change, InitialDebt = debt, CustomerId = customer?.Id, CustomerName = customer?.Name ?? "", Due = debt > 0 ? due : null, Rates = Rates(settings), StockAffected = settings.Inventory };
        if (settings.Inventory) foreach (var line in lines) Move(state, line.ProductId, -line.Quantity, "Venta", sale.Id);
        if (settings.Cash)
        {
            RequireCash(state);
            foreach (var payment in payments) Cash(state, payment, Usd(payment, settings), "Venta", sale.Id);
            if (change > 0) Cash(state, new(Currency.USD, PaymentMethod.Efectivo, -change), -change, "Cambio de venta", sale.Id);
        }
        state.Sales.Add(sale); return sale;
    }, "Venta registrada");

    public void VoidSale(Guid id, string reason) => store.Transact((state, _, settings) =>
    {
        Reason(reason); var sale = state.Sales.SingleOrDefault(s => s.Id == id) ?? throw new ArgumentException("Venta inexistente.");
        if (sale.Voided) throw new ArgumentException("La venta ya está anulada.");
        if (state.Returns.Any(r=>r.SaleId==id)) throw new ArgumentException("Esta venta tiene devoluciones. Devuelve las cantidades restantes en lugar de anularla.");
        if (state.Abonos.Any(a => a.SaleId == id && !a.Voided)) throw new ArgumentException("Anula los abonos antes de anular esta venta.");
        if (sale.StockAffected) foreach (var line in sale.Lines) Move(state, line.ProductId, line.Quantity, "Anulación de venta: " + reason, id);
        ReverseCash(state, id, settings, reason);
        state.Sales[state.Sales.IndexOf(sale)] = sale with { Voided = true, VoidReason = reason.Trim() }; return true;
    }, "Venta anulada");

    public static CashSession? OpenSession(OperationsState state) => state.Sessions.SingleOrDefault(s => s.ClosedAt is null);
    private static CashSession RequireCash(OperationsState state) => OpenSession(state) ?? throw new ArgumentException("Abre caja antes de registrar esta operación.");
    public static decimal Expected(OperationsState state, CashSession session, string currency)
    {
        var opening = currency switch { "USD" => session.OpeningUsd, "VES" => session.OpeningVes, "COP" => session.OpeningCop, _ => throw new ArgumentException("Moneda inválida.") };
        return opening + state.Cash.Where(e => e.SessionId == session.Id && e.Payment.Method == PaymentMethod.Efectivo && PhysicalCurrency(e.Payment.Currency) == currency).Sum(e => e.Payment.Amount);
    }
    public static string PhysicalCurrency(Currency currency) => currency switch { Currency.USD => "USD", Currency.COP => "COP", _ => "VES" };
    private static void Cash(OperationsState state, Payment payment, decimal usd, string reason, Guid? doc)
    {
        var session = RequireCash(state);
        if (payment.Method == PaymentMethod.Efectivo && Expected(state, session, PhysicalCurrency(payment.Currency)) + payment.Amount < 0) throw new ArgumentException("Efectivo insuficiente en caja para el egreso o cambio.");
        state.Cash.Add(new(Guid.NewGuid(), session.Id, DateTimeOffset.UtcNow, payment, usd, reason, doc));
    }
    private static void ReverseCash(OperationsState state, Guid doc, BusinessSettings settings, string reason)
    {
        var entries = state.Cash.Where(e => e.DocumentId == doc).ToList();
        if (entries.Count == 0) return;
        if (!settings.Cash) throw new ArgumentException("Activa y abre caja para anular una operación con pagos registrados.");
        // Return original change first, then refund payments; avoids an artificial transient shortage.
        foreach (var entry in entries.OrderBy(e => e.Payment.Amount)) Cash(state, entry.Payment with { Amount = -entry.Payment.Amount }, -entry.Usd, "Anulación: " + reason, doc);
    }
    public void OpenCash(decimal usd, decimal ves, decimal cop) => store.Transact((state, _, settings) =>
    {
        if (!settings.Cash) throw new ArgumentException("Activa caja.");
        Amount(usd, true); Amount(ves, true); Amount(cop, true);
        if (OpenSession(state) is not null) throw new ArgumentException("Ya hay una caja abierta.");
        state.Sessions.Add(new() { OpeningUsd = usd, OpeningVes = ves, OpeningCop = cop }); return true;
    }, "Caja abierta");
    public void CloseCash(decimal usd, decimal ves, decimal cop) => store.Transact((state, _, _) =>
    {
        Amount(usd, true); Amount(ves, true); Amount(cop, true); var session = RequireCash(state);
        state.Sessions[state.Sessions.IndexOf(session)] = session with { ClosedAt = DateTimeOffset.UtcNow, CountedUsd = usd, CountedVes = ves, CountedCop = cop, ExpectedUsd = Expected(state, session, "USD"), ExpectedVes = Expected(state, session, "VES"), ExpectedCop = Expected(state, session, "COP") }; return true;
    }, "Caja cerrada");
    public void CashMovement(Payment payment, bool expense, string reason) => store.Transact((state, _, settings) =>
    {
        if (!settings.Cash) throw new ArgumentException("Activa caja.");
        Reason(reason); var usd = Usd(payment, settings); Cash(state, expense ? payment with { Amount = -payment.Amount } : payment, expense ? -usd : usd, reason.Trim(), null); return true;
    }, "Movimiento de caja");

    public void SaveContact(Contact contact, bool supplier) => store.Transact((state, _, settings) =>
    {
        if (supplier ? !settings.Purchases : !settings.Customers) throw new ArgumentException("Activa el módulo correspondiente.");
        Reason(contact.Name); Amount(contact.CreditLimit, true); var list = supplier ? state.Suppliers : state.Customers;
        var old = list.FirstOrDefault(c => c.Id == contact.Id);
        var balance = state.Sales.Where(s => s.CustomerId == contact.Id).Sum(s => Debt(state, s));
        if (!supplier && ((!contact.Active && balance > 0) || contact.CreditLimit < balance)) throw new ArgumentException("El cliente tiene deuda: no se puede desactivar ni reducir el límite por debajo del saldo.");
        if (old is null) list.Add(contact with { Name = contact.Name.Trim() }); else list[list.IndexOf(old)] = contact with { Name = contact.Name.Trim() }; return true;
    }, supplier ? "Proveedor guardado" : "Cliente guardado");

    public Purchase Buy(Guid supplierId, string reference, IReadOnlyList<(Guid ProductId, decimal Quantity, decimal Cost)> items, Payment payment) => store.Transact((state, products, settings) =>
    {
        if (!settings.Purchases || !settings.Inventory) throw new ArgumentException("Activa compras e inventario para recibir mercancía.");
        Reason(reference); if (items.Count == 0 || items.Select(i => i.ProductId).Distinct().Count() != items.Count) throw new ArgumentException("Agrega líneas sin productos duplicados.");
        var supplier = state.Suppliers.FirstOrDefault(s => s.Id == supplierId && s.Active) ?? throw new ArgumentException("Selecciona proveedor activo.");
        var lines = items.Select(i => { var p = Product(products, i.ProductId); Quantity(p, i.Quantity); Amount(i.Cost, true); return new DocumentLine(p.Id, p.Name, p.Code, p.Unit, i.Quantity, i.Cost, i.Cost); }).ToList();
        var total = lines.Sum(l => l.Total); if (Usd(payment, settings, total == 0) != total) throw new ArgumentException("El pago debe coincidir con el total de la compra convertido a USD.");
        var purchase = new Purchase { SupplierId = supplierId, SupplierName = supplier.Name, Reference = reference.Trim(), Lines = lines, Total = total, Payment = payment, Rates = Rates(settings) };
        foreach (var line in lines) Move(state, line.ProductId, line.Quantity, "Compra", purchase.Id);
        if (settings.Cash) { RequireCash(state); if (total > 0) Cash(state, payment with { Amount = -payment.Amount }, -total, "Compra", purchase.Id); }
        state.Purchases.Add(purchase); return purchase;
    }, "Compra registrada");
    public void VoidPurchase(Guid id, string reason) => store.Transact((state, _, settings) =>
    {
        Reason(reason); var purchase = state.Purchases.SingleOrDefault(p => p.Id == id) ?? throw new ArgumentException("Compra inexistente.");
        if (purchase.Voided) throw new ArgumentException("La compra ya está anulada.");
        foreach (var line in purchase.Lines) Move(state, line.ProductId, -line.Quantity, "Anulación de compra: " + reason, id);
        ReverseCash(state, id, settings, reason); state.Purchases[state.Purchases.IndexOf(purchase)] = purchase with { Voided = true, VoidReason = reason.Trim() }; return true;
    }, "Compra anulada");
    public CreditPayment PayDebt(Guid saleId, Payment payment) => store.Transact((state, _, settings) =>
    {
        if (!settings.Credit || !settings.Customers) throw new ArgumentException("Activa clientes y créditos.");
        var sale = state.Sales.SingleOrDefault(s => s.Id == saleId) ?? throw new ArgumentException("Venta inexistente.");
        var usd = Usd(payment, settings);
        if (usd > Debt(state, sale)) throw new ArgumentException("El abono supera la deuda pendiente.");
        var abono = new CreditPayment { SaleId = saleId, Payment = payment, Rates = Rates(settings), Usd = usd };
        if (settings.Cash) Cash(state, payment, usd, "Abono de crédito", abono.Id);
        state.Abonos.Add(abono); return abono;
    }, "Abono registrado");
    public void VoidDebtPayment(Guid id, string reason) => store.Transact((state, _, settings) =>
    {
        Reason(reason); var abono = state.Abonos.SingleOrDefault(a => a.Id == id) ?? throw new ArgumentException("Abono inexistente.");
        if (abono.Voided) throw new ArgumentException("El abono ya está anulado.");
        if(state.Returns.Any(r=>r.SaleId==abono.SaleId)) throw new ArgumentException("Esta venta tiene devoluciones; no se puede alterar un abono que ya participó en su liquidación.");
        ReverseCash(state, id, settings, reason); state.Abonos[state.Abonos.IndexOf(abono)] = abono with { Voided = true, VoidReason = reason.Trim() }; return true;
    }, "Abono anulado");
}
