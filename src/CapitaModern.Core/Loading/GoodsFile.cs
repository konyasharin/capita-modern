using CapitaModern.Core.Economy;

namespace CapitaModern.Core.Loading;

/// <summary>Товар из data/economy/goods.json. Ядру нужна пока одна колонка —
/// эластичность; остальное там для инструментов и интерфейса.</summary>
public record GoodDto(GoodType Id, int Elasticity);
