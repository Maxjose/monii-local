namespace Monii.Domain;

public enum BusinessProfile { General, Groceries, Parts, Basic }
public sealed record Category(Guid Id,string Name,bool Active=true);

public sealed record Product
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = "";
    public string Code { get; init; } = "";
    public string Category { get; init; } = "";
    public string Brand { get; init; } = "";
    public string Reference { get; init; } = "";
    public string Compatibility { get; init; } = "";
    public string Unit { get; init; } = "Unidad";
    public decimal CostUsd { get; init; }
    public decimal PriceUsd { get; init; }
    public bool Active { get; init; } = true;
    public decimal MinimumStock { get; init; }
}

public sealed record BusinessSettings
{
    public bool IndependentCash { get; init; } = true;
    public string Name { get; init; } = "Mi negocio";
    public string TaxId { get; init; } = "";
    public BusinessProfile Profile { get; init; }
    public bool Inventory { get; init; } = true;
    public bool Purchases { get; init; } = true;
    public bool Customers { get; init; } = true;
    public bool Credit { get; init; } = true;
    public bool Cash { get; init; } = true;
    public bool Reports { get; init; } = true;
    public bool Lots { get; init; }
    public bool VehicleCompatibility { get; init; }
    public bool Tickets { get; init; }
    public bool AutoBackups { get; init; } = true;
    public string BackupDirectory { get; init; } = "";
    public bool ShowBcv { get; init; }
    public bool ShowManualVes { get; init; }
    public bool ShowCop { get; init; }
    public decimal BcvRate { get; init; }
    public decimal ManualVesRate { get; init; }
    public decimal CopRate { get; init; }
    public DateTimeOffset? RatesUpdatedAt { get; init; }
    public bool AutoBcv { get; init; }
    public bool AutoCop { get; init; }
    public DateOnly? BcvEffectiveDate { get; init; }
    public DateOnly? CopEffectiveDate { get; init; }
    public string BcvSource { get; init; } = "Manual";
    public string CopSource { get; init; } = "Manual";
    public string UpdateFeedUrl { get; init; } = "";
    public bool AutoCheckUpdates { get; init; } = true;
    public bool DarkMode { get; init; }
    public string AccentColor { get; init; } = "#0F766E";

    public BusinessSettings WithProfile(BusinessProfile profile) => profile==BusinessProfile.Basic ? this with { Profile=profile } : this with
    {
        Profile = profile,
        Lots = profile == BusinessProfile.Groceries,
        VehicleCompatibility = profile == BusinessProfile.Parts
    };
}

public sealed record AuditEntry(DateTimeOffset At, string Action, string Details)
{
    public string ActorName { get; init; } = "Histórico sin usuario";
}
