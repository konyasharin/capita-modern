namespace CapitaModern.Core.Loading;

/// <summary>Денежная масса из data/economy/money-supply.json, в процентах от ВВП.
/// Кого нет в списке — <paramref name="DefaultShareOfGdp"/>.</summary>
public record MoneySupplyFile
(
    Dictionary<string, int> ByIso,
    int DefaultShareOfGdp
);
