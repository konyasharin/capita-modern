using CapitaModern.Core.Economy;

namespace CapitaModern.Core.Loading;

/// <summary>Стартовые цены из data/economy/prices.json в тысячах долларов за единицу.
/// Только отправная точка: дальше цену двигает игра.</summary>
public record PricesFile
(
    Dictionary<GoodType, Price> Prices
);
