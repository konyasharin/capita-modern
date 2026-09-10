namespace CapitaModern.Core.Economy;

/// <summary>Один шаг к равновесию: величина двигается на процент от перекоса.</summary>
/// <remarks>Одно правило на цены и на курс. Курс — это цена денег страны, и отдельный
/// механизм ему не нужен.</remarks>
public static class Drift
{
    /// <param name="under">Насколько не хватает: положительное двигает вверх.</param>
    /// <param name="over">На что делить перекос — сумма обеих сторон.</param>
    public static long Step(long value, long under, long over, int stepPercent)
    {
        if (over == 0) return value; // ничего не известно — двигать не от чего

        // Int128: величина на процент на перекос не влезает в long у дорогих товаров.
        // Деление обрубает дробь в обе стороны одинаково, так что само по себе не сползает.
        long delta = (long)((Int128)value * stepPercent * under / ((Int128)over * 100));

        // Дешёвое иначе застревает навсегда: два процента от копейки — ноль.
        if (delta == 0 && under != 0) delta = under > 0 ? 1 : -1;

        return value + delta;
    }
}
