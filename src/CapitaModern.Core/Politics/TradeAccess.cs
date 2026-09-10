using CapitaModern.Core.Economy;

namespace CapitaModern.Core.Politics;

/// <summary>Пускают ли страну на мировой рынок по этому товару.</summary>
/// <remarks>Ради этого и выбран один рынок вместо пар: эмбарго, блокада порта и санкции
/// — это закрытый доступ, а не отдельная механика.</remarks>
public sealed class TradeAccess
{
    private readonly HashSet<GoodType> _blocked = [];

    public bool CanTrade(GoodType good) => !_blocked.Contains(good);

    public void Block(GoodType good) => _blocked.Add(good);

    public void Allow(GoodType good) => _blocked.Remove(good);
}
