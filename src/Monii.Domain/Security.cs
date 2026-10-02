namespace Monii.Domain;

public enum UserRole { Administrador, Cajero, Vendedor }
public enum Permission { Products, Inventory, Sell, Void, Customers, Purchases, Cash, Expenses, Credit, Reports, Settings, Backup, Restore, Users, Updates }
public sealed record SessionUser(Guid Id, string Username, string Name, UserRole Role);
public sealed record UserAccount(Guid Id, string Username, string Name, UserRole Role, bool Active);
public sealed record SaleReturn
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid SaleId { get; init; }
    public DateTimeOffset At { get; init; } = DateTimeOffset.UtcNow;
    public string Reason { get; init; } = "";
    public List<DocumentLine> Lines { get; init; } = [];
    public decimal Total { get; init; }
    public decimal CostReduction { get; init; }
    public decimal DebtReduction { get; init; }
    public decimal RefundUsd { get; init; }
    public PaymentMethod RefundMethod { get; init; }
    public string ActorName { get; init; } = "";
}
