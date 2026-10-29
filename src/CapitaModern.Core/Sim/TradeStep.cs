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

    private sealed record Seller(Country Country, double Spare, double Weight, double Cost)
    {
        /// <summary>Сколько ещё можно отдать на этой неделе, не трогая запас для себя.</summary>
        public double Left { get; set; }
    }

    public static void Run(GameWorld world)
    {
        var countries = world.Countries.Where(c => c.DailyUse.Any(u => u > 0)).ToList();
        foreach (var country in countries)
        {
            Array.Clear(country.ImportedWeek);
            Array.Clear(country.ExportedWeek);
        }

        var tariffs = new Dictionary<Country, double>();
        var subsidies = new Dictionary<(Country, int), double>();

        foreach (var good in world.Catalog.Goods.Where(g => !g.Perishable))
        {
            var g = good.Index;
            var sellers = new List<Seller>();
            foreach (var country in countries)
            {
                // Заказ делят по мощности сверх своего расхода, а не по складу: иначе вывоз доставался случайным странам.
                var spare = (country.CapacityOut[g] - country.DailyUse[g]) * EconomyStep.WeekDays;
                if (spare <= 0 || country.Stock[g] <= 0) continue;

                // Продавец не отдаёт запас, нужный ему самому: Андорра вывезла всю электронику, и встали её услуги.
                var reserve = country.DailyUse[g] * EconomyStep.NormDays / 2;
                var cost = Cost(country, g) * (1 - country.ExportSubsidy[g]);
                sellers.Add(new Seller(country, spare, spare * Math.Pow(cost, -Sensitivity), cost)
                {
                    Left = Math.Max(0, country.Stock[g] - reserve),
                });
            }

            var buyers = new List<(Country Country, double Need)>();
            foreach (var country in countries)
            {
                var gap = country.DailyUse[g] * EconomyStep.WeekDays - country.ProducedWeek[g]
                    + (country.DailyUse[g] * EconomyStep.NormDays - country.Stock[g]) * Refill;

                gap += Displace(world, country, g, sellers) * country.ProducedWeek[g];
                if (gap > 0) buyers.Add((country, gap));
            }

            var offered = sellers.Sum(s => s.Spare);
            var wanted = buyers.Sum(b => b.Need);
            if (offered <= 0 || wanted <= 0) continue;

            var buyShare = Math.Min(1, offered / wanted);
            var need = buyers.Select(b => b.Need * buyShare).ToArray();

            // Два прохода: дешёвому продавцу заказывают больше, чем у него есть, — каждому покупателю урезают
            // поровну, а недоданное во втором проходе делят между теми, у кого осталось.
            for (var pass = 0; pass < 2; pass++)
            {
                var asked = new Dictionary<Seller, double>();
                var plans = new List<(int Buyer, Seller Seller, double Amount)>();
                for (var b = 0; b < buyers.Count; b++)
                {
                    if (need[b] <= 0) continue;

                    var buyer = buyers[b].Country;
                    var friendly = sellers.Where(s => s.Left > 0 && s.Country != buyer && !world.AtWar(s.Country.Id, buyer.Id)).ToList();
                    var pool = friendly.Sum(s => s.Weight);
                    if (pool <= 0) continue;

                    foreach (var seller in friendly)
                    {
                        var amount = need[b] * seller.Weight / pool;
                        plans.Add((b, seller, amount));
                        asked[seller] = asked.GetValueOrDefault(seller) + amount;
                    }
                }

                foreach (var (b, seller, amount) in plans)
                {
                    var part = amount * Math.Min(1, seller.Left / asked[seller]);
                    if (part <= 0) continue;

                    var buyer = buyers[b].Country;
                    need[b] -= part;
                    seller.Country.Stock[g] -= part;
                    buyer.Stock[g] += part;
                    seller.Country.ExportedWeek[g] += part;
                    buyer.ImportedWeek[g] += part;

                    tariffs[buyer] = tariffs.GetValueOrDefault(buyer) + part * good.BasePrice * buyer.PriceLevel * buyer.Tariff[g];
                    subsidies[(seller.Country, g)] = subsidies.GetValueOrDefault((seller.Country, g))
                        + part * good.BasePrice * seller.Country.PriceLevel * seller.Country.ExportSubsidy[g];
                }

                foreach (var (seller, amount) in asked) seller.Left = Math.Max(0, seller.Left - Math.Min(amount, seller.Left));
            }
        }

        Settle(world, tariffs, subsidies);
    }

    /// <summary>Пошлину платят свои компании, ввозившие товар, доплату получают свои компании отрасли.</summary>
    /// <remarks>Деньги не берутся из воздуха: где компаний нет, государство платит само себе, и это не учитывается.</remarks>
    private static void Settle(GameWorld world, Dictionary<Country, double> tariffs, Dictionary<(Country, int), double> subsidies)
    {
        foreach (var (country, due) in tariffs.Where(t => t.Value > 0))
        {
            var payers = world.Companies.Where(c => c.Alive && c.Country == country.Id && c.Cash > 0).ToList();
            var cash = payers.Sum(c => c.Cash);
            var paid = Math.Min(due, cash);
            if (paid <= 0) continue;

            foreach (var company in payers) company.Cash -= paid * company.Cash / cash;
            country.Treasury += paid;
            country.TaxesWeek += paid;
        }

        foreach (var ((country, good), due) in subsidies.Where(s => s.Value > 0))
        {
            var sectors = world.Catalog.Buildings.Where(b => b.Outputs.Any(o => o.Good == good)).Select(b => b.Sector).ToHashSet();
            var receivers = world.Companies.Where(c => c.Alive && c.Country == country.Id && sectors.Contains(c.Sector)).ToList();
            if (receivers.Count == 0) continue;

            foreach (var company in receivers) company.Cash += due / receivers.Count;
            country.Treasury -= due;
            country.SpentWeek += due;
        }
    }

    /// <summary>Во что стране обходится товар против стартового: труд дорожает с доходом, дешевеет с выработкой.</summary>
    public static double Cost(Country country, int good) =>
        country.Wage / country.Productivity * (1 + (country.TaxRate - BaseTax) / 2) * country.PriceMult[good];

    /// <summary>Какую долю своего выпуска страна заменяет ввозом: чужое, с пошлиной, дешевле своего — понемногу переходят.</summary>
    private static double Displace(GameWorld world, Country country, int good, List<Seller> sellers)
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
