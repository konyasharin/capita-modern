using CapitaModern.Core.Economy;

namespace CapitaModern.Core.Loading;

/// <summary>Ставки потребления из data/economy/consumption.json: сколько товара
/// население съедает за сутки на миллион человек.</summary>
public record ConsumptionFile
(
    Dictionary<GoodType, GoodAmount> UnitPerMillionPeople
);
