namespace Monii.Domain;

public enum Currency { USD, VES_BCV, VES_Manual, COP }
public enum PaymentMethod { Efectivo, Transferencia, Tarjeta }
public sealed record Payment(Currency Currency, PaymentMethod Method, decimal Amount);
public sealed record RateSnapshot(decimal Bcv, decimal Manual, decimal Cop)
{
    public decimal For(Currency currency) => currency switch { Currency.USD => 1, Currency.VES_BCV => Bcv, Currency.VES_Manual => Manual, Currency.COP => Cop, _ => throw new ArgumentException("Moneda inválida.") };
}
public sealed record StockMove(Guid Id, Guid ProductId, DateTimeOffset At, decimal Quantity, string Reason, Guid? DocumentId);
public sealed record Contact
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = "";
    public string Identification { get; init; } = "";
    public string Phone { get; init; } = "";
    public decimal CreditLimit { get; init; }
    public bool Active { get; init; } = true;
}
public sealed record DocumentLine(Guid ProductId, string Name, string Code, string Unit, decimal Quantity, decimal Price, decimal Cost)
{
    public decimal Total => decimal.Round(Quantity * Price, 2, MidpointRounding.AwayFromZero);
}
public sealed record Sale
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public long Number { get; init; }
    public DateTimeOffset At { get; init; } = DateTimeOffset.UtcNow;
    public List<DocumentLine> Lines { get; init; } = [];
    public decimal Discount { get; init; }
    public decimal Total { get; init; }
    public List<Payment> Payments { get; init; } = [];
    public decimal ChangeUsd { get; init; }
    public RateSnapshot Rates { get; init; } = new(0,0,0);
    public Guid? CustomerId { get; init; }
    public string CustomerName { get; init; } = "";
    public decimal InitialDebt { get; init; }
    public DateOnly? Due { get; init; }
    public bool StockAffected { get; init; }
    public bool Voided { get; init; }
    public string VoidReason { get; init; } = "";
    public Guid? SellerId { get; init; }
    public string SellerName { get; init; } = "";
}
public sealed record Purchase
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public DateTimeOffset At { get; init; } = DateTimeOffset.UtcNow;
    public Guid SupplierId { get; init; }
    public string SupplierName { get; init; } = "";
    public string Reference { get; init; } = "";
    public List<DocumentLine> Lines { get; init; } = [];
    public decimal Total { get; init; }
    public Payment Payment { get; init; } = new(Currency.USD, PaymentMethod.Efectivo, 0);
    public RateSnapshot Rates { get; init; } = new(0,0,0);
    public bool Voided { get; init; }
    public string VoidReason { get; init; } = "";
}
public sealed record CreditPayment
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid SaleId { get; init; }
    public DateTimeOffset At { get; init; } = DateTimeOffset.UtcNow;
    public Payment Payment { get; init; } = new(Currency.USD, PaymentMethod.Efectivo, 0);
    public RateSnapshot Rates { get; init; } = new(0,0,0);
    public decimal Usd { get; init; }
    public bool Voided { get; init; }
    public string VoidReason { get; init; } = "";
}
public sealed record CashEntry(Guid Id, Guid SessionId, DateTimeOffset At, Payment Payment, decimal Usd, string Reason, Guid? DocumentId);
public sealed record CashSession
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public DateTimeOffset OpenedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ClosedAt { get; init; }
    public decimal OpeningUsd { get; init; }
    public decimal OpeningVes { get; init; }
    public decimal OpeningCop { get; init; }
    public decimal CountedUsd { get; init; }
    public decimal CountedVes { get; init; }
    public decimal CountedCop { get; init; }
    public decimal ExpectedUsd { get; init; }
    public decimal ExpectedVes { get; init; }
    public decimal ExpectedCop { get; init; }
}
public sealed class OperationsState
{
    public List<StockMove> Stock { get; set; } = [];
    public List<Sale> Sales { get; set; } = [];
    public List<Purchase> Purchases { get; set; } = [];
    public List<Contact> Customers { get; set; } = [];
    public List<Contact> Suppliers { get; set; } = [];
    public List<CreditPayment> Abonos { get; set; } = [];
    public List<CashSession> Sessions { get; set; } = [];
    public List<CashEntry> Cash { get; set; } = [];
    public List<SaleReturn> Returns { get; set; } = [];
}
