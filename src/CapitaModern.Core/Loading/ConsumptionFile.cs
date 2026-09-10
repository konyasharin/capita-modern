using CapitaModern.Core.Economy;

namespace CapitaModern.Core.Loading;

/// <summary>Ставки потребления из data/economy/consumption.json: сколько товара
/// население съедает за сутки на миллион человек при обычном доходе.</summary>
/// <param name="IncomeElasticity">Насколько потребление идёт за доходом, в сотых.</param>
public record ConsumptionFile
(
    Dictionary<GoodType, GoodAmount> UnitPerMillionPeople,
    Dictionary<GoodType, int> IncomeElasticity
);
