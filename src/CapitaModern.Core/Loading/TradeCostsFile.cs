namespace CapitaModern.Core.Loading;

/// <summary>Надбавки к ввозу из data/economy/trade-costs.json. Доля перевозки лежит у
/// товара в goods.json: она про сам товар, а не про страну.</summary>
public record TradeCostsFile
(
    int LandlockedFactor,
    string[] Landlocked,
    int DefaultTariff,
    Dictionary<string, int> TariffByIso
);
