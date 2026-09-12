using CapitaModern.Core.Economy;

namespace CapitaModern.Core.World;

/// <summary>Что в стране построено и стоит: жилой фонд у людей и дороги у государства.</summary>
/// <remarks>
/// Устроено как арсенал и нужно для того же — дать товару покупателя. Стройматериалов мир
/// выпускает всемеро больше, чем у него заказывают, леса вдвое, и цена их упиралась в пол
/// коридора у сорока пяти стран. Покупатель у них был один — стройка заводов, а в жизни
/// главные их потребители жильё и дороги.
///
/// Жильё покупают люди из своих денег, дороги — государство из бюджета. И то и другое
/// изнашивается: дом служит полвека, дорога три десятка лет. Отсюда и ровный спрос, а
/// заодно и показатель, по которому видно, живёт страна лучше или хуже.
/// </remarks>
public sealed class Estate
{
    /// <summary>Сколько лет стоит дом. В статистике жилой фонд списывается примерно за
    /// полвека.</summary>
    public const int HouseYears = 50;

    /// <summary>Сколько лет служит дорога до капитального ремонта.</summary>
    public const int RoadYears = 30;

    /// <summary>Жилой фонд в материалах, из которых он сложен.</summary>
    public GoodAmount Housing { get; private set; }

    /// <summary>Дороги и всё казённое строительство, в тех же материалах.</summary>
    public GoodAmount Roads { get; private set; }

    public void Settle(GoodAmount added) => Housing += added;

    public void Pave(GoodAmount added) => Roads += added;

    /// <summary>Изнашивает построенное за сутки.</summary>
    public void Wear(int daysInYear)
    {
        Housing -= new GoodAmount(Housing.Raw / (HouseYears * daysInYear));
        Roads -= new GoodAmount(Roads.Raw / (RoadYears * daysInYear));
    }
}
