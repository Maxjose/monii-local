namespace Monii.Domain;

public sealed record ExchangeQuote(decimal Value,DateOnly EffectiveDate,DateOnly Until,string Source);
public sealed record ExchangeRefresh(bool BcvUpdated,bool CopUpdated,string Message);
