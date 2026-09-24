namespace CapitaModern.Core.Economy;

/// <summary>Общий уровень цен страны и то, куда его тянут деньги.</summary>
/// <remarks>
/// До этого уровень цен не был привязан ни к чему. Цена каждого товара ходила от своего
/// покрытия, а сколько стоит всё вместе — не спрашивал никто, и у страны, которой чего-то
/// вечно не хватает, цены уезжали полным шагом без предела. Держала их только подпорка
/// <see cref="Prices.MaxSwingTimes"/>, в которую упёрлось больше половины цен мира.
///
/// Привязка — количественная теория в том же грубом виде, в каком она уже работает у
/// печатного станка: денег стало вдвое больше на тот же выпуск — всё вдвое дороже;
/// выпуск вырос вдвое на те же деньги — всё вдвое дешевле. Относительные цены при этом
/// не трогаются: их по-прежнему задаёт покрытие, якорь двигает только их общий уровень.
/// </remarks>
public static class PriceLevel
{
    /// <summary>Уровень цен как на старте.</summary>
    public const int Scale = 10_000;

    /// <summary>Дальше этого уровень не считается: за тысячей раз начинается не
    /// экономика, а переполнение.</summary>
    public const int Ceiling = 1000 * Scale;

    /// <summary>Во сколько раз всё подорожало против старта: выпуск в своих ценах против
    /// того же выпуска в стартовых.</summary>
    public static int Of(Money nominal, Money real) =>
        real.Raw <= 0 ? Scale : Capped((Int128)nominal.Raw * Scale / real.Raw);

    /// <summary>Куда уровень тянут деньги: сколько их стало на единицу выпуска.</summary>
    /// <param name="supplyStart">Масса на начало партии.</param>
    /// <param name="realBefore">Выпуск в стартовых ценах на первом тике.</param>
    public static int Target(Money supply, Money supplyStart, Money real, Money realBefore)
    {
        if (supplyStart.Raw <= 0 || real.Raw <= 0) return Scale;

        return Capped((Int128)supply.Raw * realBefore.Raw * Scale / ((Int128)supplyStart.Raw * real.Raw));
    }

    private static int Capped(Int128 level) => level < 1 ? 1 : level > Ceiling ? Ceiling : (int)level;
}
