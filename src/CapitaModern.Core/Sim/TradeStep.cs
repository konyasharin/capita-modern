using CapitaModern.Core.World;

namespace CapitaModern.Core.Sim;

/// <summary>Мировой обмен: кто делает больше, чем тратит, отдаёт тем, кто делает меньше.</summary>
/// <remarks>Считаются потоки, а не запасы: иначе экспортёр не мог вывезти добытое, и добыча вставала.</remarks>
public static class TradeStep
{
    /// <summary>Какую долю разрыва с нормой запаса страна закрывает за неделю.</summary>
    private const double Refill = 0.25;

    /// <summary>Насколько резко заказ уходит к дешёвому: продавец на 10% дешевле получает в полтора раза больше.</summary>
    private const double Sensitivity = 4;

    /// <summary>Налог, при котором издержки не меняются.</summary>
    private const double BaseTax = 0.20;

    /// <summary>Какую долю своего выпуска страна в пределе отдаёт чужому, если оно дешевле.</summary>
    private const double MaxShift = 0.5;

    /// <summary>Чужое дешевле на 10% — со временем ввозом заменяют пятую часть своего.</summary>
    private const double ShiftGain = 2;

    /// <summary>За сколько недель покупатели переходят на дешёвое: доли не прыгают.</summary>
    private const double ShiftWeeks = 26;

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
            var sellers = new List<(Country Country, double Spare, double Weight, double Cost)>();
            foreach (var country in countries)
            {
                // Заказ делят по мощности сверх своего расхода, а не по складу: иначе вывоз доставался случайным странам.
                var spare = (country.CapacityOut[g] - country.DailyUse[g]) * EconomyStep.WeekDays;
                if (spare <= 0 || country.Stock[g] <= 0) continue;

                var cost = Cost(country, g) * (1 - country.ExportSubsidy[g]);
                sellers.Add((country, spare, spare * Math.Pow(cost, -Sensitivity), cost));
            }

            var buyers = new List<(Country Country, double Amount)>();
            foreach (var country in countries)
            {
                var gap = country.DailyUse[g] * EconomyStep.WeekDays - country.ProducedWeek[g]
                    + (country.DailyUse[g] * EconomyStep.NormDays - country.Stock[g]) * Refill;

                gap += Displace(world, country, g, sellers) * country.ProducedWeek[g];
                if (gap > 0) buyers.Add((country, gap));
            }

            var offered = sellers.Sum(s => s.Spare);
            var wanted = buyers.Sum(b => b.Amount);
            if (offered <= 0 || wanted <= 0) continue;

            var buyShare = Math.Min(1, offered / wanted);

            foreach (var (buyer, want) in buyers)
            {
                var friendly = sellers.Where(s => s.Country != buyer && !world.AtWar(s.Country.Id, buyer.Id)).ToList();
                var pool = friendly.Sum(s => s.Weight);
                if (pool <= 0) continue;

                var amount = want * buyShare;
                foreach (var (seller, _, weight, _) in friendly)
                {
                    // Продавец не отдаёт запас, нужный ему самому: Андорра вывезла всю электронику, и встали её услуги.
                    var reserve = seller.DailyUse[g] * EconomyStep.NormDays / 2;
                    var part = Math.Min(amount * weight / pool, Math.Max(0, seller.Stock[g] - reserve));
                    if (part <= 0) continue;

                    seller.Stock[g] -= part;
                    buyer.Stock[g] += part;
                    seller.ExportedWeek[g] += part;
                    buyer.ImportedWeek[g] += part;

                    seller.Treasury -= part * good.BasePrice * seller.PriceLevel * seller.ExportSubsidy[g];
                    buyer.Treasury += part * good.BasePrice * buyer.PriceLevel * buyer.Tariff[g];
                }
            }
        }
    }

    /// <summary>Во что стране обходится товар против стартового: труд дорожает с доходом, дешевеет с выработкой.</summary>
    public static double Cost(Country country, int good) =>
        country.Wage / country.Productivity * (1 + (country.TaxRate - BaseTax) / 2) * country.PriceMult[good];

    /// <summary>Какую долю своего выпуска страна заменяет ввозом: чужое, с пошлиной, дешевле своего — понемногу переходят.</summary>
    private static double Displace(
        GameWorld world, Country country, int good, List<(Country Country, double Spare, double Weight, double Cost)> sellers)
    {
        var target = 0.0;
        if (country.ProducedWeek[good] > 0)
        {
            var friendly = sellers.Where(s => s.Country != country && !world.AtWar(s.Country.Id, country.Id)).ToList();
            var weight = friendly.Sum(s => s.Weight);
            if (weight > 0)
            {
                var foreign = friendly.Sum(s => s.Cost * s.Weight) / weight * (1 + country.Tariff[good]);
                target = Math.Clamp((Cost(country, good) / foreign - 1) * ShiftGain, 0, MaxShift);
            }
        }

        country.ImportShift[good] += (target - country.ImportShift[good]) / ShiftWeeks;
        return country.ImportShift[good];
    }
}
