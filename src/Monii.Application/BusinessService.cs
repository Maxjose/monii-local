using Monii.Domain;

namespace Monii.Application;

public interface IStore
{
    IReadOnlyList<Product> GetProducts();
    void SaveProduct(Product product);
    BusinessSettings GetSettings();
    void SaveSettings(BusinessSettings settings);
    IReadOnlyList<AuditEntry> GetAudit();
}

public sealed class BusinessService(IStore store)
{
    public IReadOnlyList<Product> Products(string search = "", bool includeInactive = false) =>
        store.GetProducts().Where(p => (includeInactive || p.Active) &&
            string.Join(" ", p.Name, p.Code, p.Category, p.Brand, p.Reference)
                .Contains(search.Trim(), StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p.Name).ToList();

    public void Save(Product product)
    {
        ValidateProduct(product);
        if (store.GetProducts().Any(p => p.Id != product.Id && p.Code.Equals(product.Code.Trim(), StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Ese código ya pertenece a otro producto, incluso si está inactivo.");
        store.SaveProduct(product with { Name = product.Name.Trim(), Code = product.Code.Trim(), Category = product.Category.Trim() });
    }

    public static void ValidateProduct(Product product)
    {
        if (string.IsNullOrWhiteSpace(product.Name)) throw new ArgumentException("Escribe el nombre del producto.");
        if (string.IsNullOrWhiteSpace(product.Code)) throw new ArgumentException("Escribe un código para el producto.");
        if (product.MinimumStock < 0 || decimal.Round(product.MinimumStock, 3) != product.MinimumStock) throw new ArgumentException("Stock mínimo inválido; hasta tres decimales.");
        if (product.CostUsd < 0 || product.PriceUsd < 0) throw new ArgumentException("Los importes no pueden ser negativos.");
        if (product.PriceUsd > 999999999m || product.CostUsd > 999999999m) throw new ArgumentException("El importe supera el límite permitido.");
        if (decimal.Round(product.PriceUsd, 2) != product.PriceUsd || decimal.Round(product.CostUsd, 2) != product.CostUsd)
            throw new ArgumentException("Los precios y costos admiten hasta dos decimales.");
    }

    public BusinessSettings Settings => store.GetSettings();
    public IReadOnlyList<AuditEntry> Audit => store.GetAudit();
    public void SaveSettings(BusinessSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.Name)) throw new ArgumentException("Escribe el nombre del negocio.");
        if (!Enum.IsDefined(settings.Profile)) throw new ArgumentException("Perfil de negocio inválido.");
        if(!System.Text.RegularExpressions.Regex.IsMatch(settings.AccentColor,"^#[0-9A-Fa-f]{6}$")) throw new ArgumentException("El color debe tener formato #RRGGBB.");
        if (settings.Credit && !settings.Customers) throw new ArgumentException("Los créditos requieren activar clientes.");
        if (store is IOperationsStore operations)
        {
            var state = operations.ReadOperations();
            if (!settings.Cash && OperationsService.OpenSession(state) is not null) throw new ArgumentException("Cierra caja antes de desactivar el módulo.");
            if ((!settings.Credit || !settings.Customers) && state.Sales.Any(s => OperationsService.Debt(state, s) > 0)) throw new ArgumentException("Hay créditos pendientes; conserva clientes y créditos activos.");
        }
        ValidateRate(settings.BcvRate, settings.ShowBcv&&!settings.AutoBcv, "BCV");
        ValidateRate(settings.ManualVesRate, settings.ShowManualVes, "bolívares manuales");
        ValidateRate(settings.CopRate, settings.ShowCop&&!settings.AutoCop, "pesos colombianos");
        var old = Settings;
        if(!string.IsNullOrWhiteSpace(settings.UpdateFeedUrl)&&(!Uri.TryCreate(settings.UpdateFeedUrl,UriKind.Absolute,out var feed)||feed.Scheme!=Uri.UriSchemeHttps||feed.UserInfo.Length>0)) throw new ArgumentException("La dirección de actualizaciones debe usar HTTPS sin credenciales.");
        var changed = old.BcvRate != settings.BcvRate || old.ManualVesRate != settings.ManualVesRate || old.CopRate != settings.CopRate;
        store.SaveSettings(settings with { Name = settings.Name.Trim(), RatesUpdatedAt = changed ? DateTimeOffset.UtcNow : old.RatesUpdatedAt, BcvSource=settings.BcvRate!=old.BcvRate?"Manual":old.BcvSource,CopSource=settings.CopRate!=old.CopRate?"Manual":old.CopSource,BcvEffectiveDate=settings.BcvRate!=old.BcvRate?null:old.BcvEffectiveDate,CopEffectiveDate=settings.CopRate!=old.CopRate?null:old.CopEffectiveDate });
    }

    private static void ValidateRate(decimal rate, bool enabled, string label)
    {
        if (rate < 0 || rate > 1000000000m || (enabled && rate == 0) || decimal.Round(rate, 8) != rate)
            throw new ArgumentException($"La tasa de {label} debe ser positiva al activarla y admitir hasta ocho decimales.");
    }

    public static decimal ConvertPrice(decimal usd, decimal rate) => decimal.Round(usd * rate, 2, MidpointRounding.AwayFromZero);
}
