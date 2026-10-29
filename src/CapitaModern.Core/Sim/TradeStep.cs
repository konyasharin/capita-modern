using CapitaModern.Core.World;

namespace CapitaModern.Core.Sim;

/// <summary>Мировой обмен: кто делает больше, чем тратит, отдаёт тем, кто делает меньше.</summary>
/// <remarks>
/// Считаются потоки, а не только запасы: страна хочет свой недельный расход сверх своего выпуска
/// и добор до нормы запаса. Иначе экспортёр не мог вывезти добытое, склад рос, и добыча вставала.
/// Без курсов и расчётов: товар просто переходит, страны в войне друг с другом не торгуют.
/// </remarks>
public static class TradeStep
{
    /// <summary>Какую долю разрыва с нормой запаса страна закрывает за неделю.</summary>
    private const double Refill = 0.25;

    public static void Run(GameWorld world)
    {
        var countries = world.Countries.Where(c => c.DailyUse.Any(u => u > 0)).ToList();
        foreach (var country in countries)
        {
            Array.Clear(country.ImportedWeek);
            Array.Clear(country.ExportedWeek);
        }

        foreach (var good in world.Catalog.Goods.Where(g => !g.Perishable))
        {
            var g = good.Index;
            var sellers = new List<(Country Country, double Amount)>();
            var buyers = new List<(Country Country, double Amount)>();

            foreach (var country in countries)
            {
                var gap = country.DailyUse[g] * EconomyStep.WeekDays - country.ProducedWeek[g]
                    + (country.DailyUse[g] * EconomyStep.NormDays - country.Stock[g]) * Refill;

                if (gap > 0) buyers.Add((country, gap));

                // Заказ делится между продавцами по их мощности сверх своего расхода, а не по тому,
                // что сейчас лежит на складе: иначе при избытке в мире весь вывоз доставался
                // случайным странам, а у России, способной кормить нефтью полмира, вставала добыча.
                var spare = (country.CapacityOut[g] - country.DailyUse[g]) * EconomyStep.WeekDays;
                if (spare > 0 && country.Stock[g] > 0) sellers.Add((country, spare));
            }

            var offered = sellers.Sum(s => s.Amount);
            var wanted = buyers.Sum(b => b.Amount);
            if (offered <= 0 || wanted <= 0) continue;

            var buyShare = Math.Min(1, offered / wanted);

            foreach (var (buyer, want) in buyers)
            {
                var friendly = sellers.Where(s => !world.AtWar(s.Country.Id, buyer.Id)).ToList();
                var pool = friendly.Sum(s => s.Amount);
                if (pool <= 0) continue;

                var amount = want * buyShare;
                foreach (var (seller, offer) in friendly)
                {
                    var part = Math.Min(amount * offer / pool, seller.Stock[g]);
                    if (part <= 0) continue;

                    seller.Stock[g] -= part;
                    buyer.Stock[g] += part;
                    seller.ExportedWeek[g] += part;
                    buyer.ImportedWeek[g] += part;
                }
            }
        }
    }
}
