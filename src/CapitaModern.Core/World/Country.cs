using CapitaModern.Core.Economy;
using CapitaModern.Core.Politics;

namespace CapitaModern.Core.World;

/// <summary>Государство. Территории здесь нет — её знает карта, склад и деньги лежат
/// у <see cref="State"/>.</summary>
public sealed class Country
{
    /// <summary>Тот же байт, что лежит в world.bin для каждой ячейки.</summary>
    public byte Id { get; }
    public string Name { get; }
    public string Iso { get; }

    /// <summary>Государство как хозяйствующий субъект — пока единственный в стране.
    /// Компании появятся такими же продавцами рядом с ним.</summary>
    public Producer State { get; }

    public Priorities Priorities { get; }

    /// <summary>Пускают ли на мировой рынок. По умолчанию везде да.</summary>
    public TradeAccess TradeAccess { get; } = new();

    /// <summary>Сколько местных денег за одну мировую единицу. Дороже — валюта слабее.</summary>
    /// <remarks>
    /// Все стартуют с единицы: настоящие курсы понадобятся, когда цены начнут показывать
    /// игроку в местных деньгах. Пока важны только движения относительно старта.
    ///
    /// Двигает его сальдо: у страны с дефицитом валюта дешевеет, импортное дорожает в
    /// местных деньгах, заявка через эластичность срезается, дефицит закрывается сам.
    /// </remarks>
    public Money ExchangeRate { get; private set; } = Money.FromWhole(1);

    /// <summary>Ключевая ставка в сотых долях процента: 425 — это 4.25% годовых.</summary>
    /// <remarks>Пока только хранится. Заработает с плавающими займами, и тогда же станет
    /// рычагом игрока: поднял против инфляции — вырос свой же процентный расход.</remarks>
    public int KeyRate { get; set; }

    /// <summary>Курс не может ни исчезнуть, ни улететь: коридор тот же, что у цен.</summary>
    public void MoveRate(Money to)
    {
        var start = Money.FromWhole(1);
        ExchangeRate = new Money(Math.Clamp(to.Raw, start.Raw / Prices.MaxSwingTimes, start.Raw * Prices.MaxSwingTimes));
    }

    public Country(byte id, string name, string iso, Producer state, Priorities priorities)
    {
        Id = id;
        Name = name;
        Iso = iso;
        State = state;
        Priorities = priorities;
    }
}
