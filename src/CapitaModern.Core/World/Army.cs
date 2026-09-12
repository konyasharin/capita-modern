using CapitaModern.Core.Economy;

namespace CapitaModern.Core.World;

/// <summary>Арсенал страны: что у неё стоит на вооружении.</summary>
/// <remarks>
/// Нужен он не для войны — воевать модель пока не умеет, — а для того, чтобы у военных
/// товаров был покупатель. Без него ракеты, танки и дроны делали и складывали в никуда:
/// заказа ноль, цена держится только денежным якорем, и у 120 стран из 200 она упиралась
/// в потолок коридора.
///
/// Техника не вечна: за <see cref="ServiceYears"/> арсенал списывается целиком, и
/// обновлять его приходится каждый год. Отсюда и берётся ровный спрос, который в жизни
/// кормит оборонную промышленность.
/// </remarks>
public sealed class Army
{
    /// <summary>Сколько лет служит техника. Столько же в жизни: танк или самолёт живут
    /// два-три десятка лет, а потом идут на слом.</summary>
    public const int ServiceYears = 25;

    private readonly Dictionary<GoodType, GoodAmount> _kit = [];

    /// <summary>Сколько такого добра стоит на вооружении.</summary>
    public GoodAmount Of(GoodType good) => _kit.GetValueOrDefault(good);

    /// <summary>Всё вооружение разом. Для показа и замера.</summary>
    public IReadOnlyDictionary<GoodType, GoodAmount> Kit => _kit;

    public void Add(GoodType good, GoodAmount amount)
    {
        if (amount.Raw <= 0) return;

        _kit[good] = Of(good) + amount;
    }

    /// <summary>Списывает отслужившее за сутки и говорит, сколько списало.</summary>
    public GoodAmount Wear(GoodType good, int daysInYear)
    {
        var have = Of(good);
        if (have.Raw <= 0) return default;

        // Остатком, а не долей: по одной единице в сутки списать нечего, а за год должно
        // осыпаться ровно столько, сколько положено сроку службы.
        var gone = new GoodAmount(have.Raw / (ServiceYears * daysInYear));
        if (gone.Raw <= 0) return default;

        _kit[good] = have - gone;

        return gone;
    }
}
