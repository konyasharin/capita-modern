using CapitaModern.Core.World;

namespace CapitaModern.Core.Sim;

/// <summary>Мировой обмен: кто делает больше, чем тратит, отдаёт тем, кто делает меньше.</summary>
/// <remarks>Считаются потоки, а не запасы: иначе экспортёр не мог вывезти добытое, и добыча вставала.</remarks>
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

                // Заказ делят по мощности сверх своего расхода, а не по складу: иначе вывоз доставался случайным странам.
                var spare = (country.CapacityOut[g] - country.DailyUse[g]) * EconomyStep.WeekDays;
                if (spare > 0 && country.Stock[g] > 0) sellers.Add((country, spare));
            }

            var offered = sellers.Sum(s => s.Amount);
            var wanted = buyers.Sum(b => b.Amount);
            if (offered <= 0 || wanted <= 0) continue;

            var buyShare = Math.Min(1, offered / wanted);

            foreach (var (buyer, want) in buyers)
            {
                var friendly = sellers.Where(s => s.Country != buyer && !world.AtWar(s.Country.Id, buyer.Id)).ToList();
                var pool = friendly.Sum(s => s.Amount);
                if (pool <= 0) continue;

                var amount = want * buyShare;
                foreach (var (seller, offer) in friendly)
                {
                    // Продавец не отдаёт запас, нужный ему самому: Андорра вывезла всю электронику, и встали её услуги.
                    var reserve = seller.DailyUse[g] * EconomyStep.NormDays / 2;
                    var part = Math.Min(amount * offer / pool, Math.Max(0, seller.Stock[g] - reserve));
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
