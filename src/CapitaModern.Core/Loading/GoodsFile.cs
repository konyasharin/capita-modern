using CapitaModern.Core.Economy;

namespace CapitaModern.Core.Loading;

/// <summary>Товар из data/economy/goods.json. Ядру нужны пока две колонки —
/// эластичности; остальное там для инструментов и интерфейса.</summary>
public record GoodDto(GoodType Id, int DemandElasticity, int SupplyElasticity);
