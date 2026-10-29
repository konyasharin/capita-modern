using CapitaModern.Core.Economy;
using CapitaModern.Core.World;

namespace CapitaModern.Core.Sim;

/// <summary>Недельный шаг экономики: производство, потребление, деньги, цены.</summary>
/// <remarks>Ни одна величина не считается от рынка за сутки: цены идут от дней запаса и
/// двигаются не больше чем на несколько процентов в неделю, поэтому числа не скачут.</remarks>
public static class EconomyStep
{
    public const int WeekDays = 7;

    /// <summary>Нормальный запас в сутках расхода.</summary>
    public const double NormDays = 45;

    /// <summary>Запас, выше которого заводы почти встают: класть больше некуда.</summary>
    public const double FullDays = 120;

    /// <summary>Доля мощности, на которой завод держится, даже когда его товар никому не нужен.</summary>
    public const double MinLoad = 0.01;

    /// <summary>Технический прогресс в год: тот же завод тем же штатом перерабатывает больше.</summary>
    public const double ProductivityGrowth = 0.015;

    /// <summary>Прирост населения в год.</summary>
    public const double PopulationGrowth = 0.009;

    /// <summary>Сколько недель трат казна держит, прежде чем отдавать излишек людям.</summary>
    public const int TreasuryWeeks = 52;

    public const double WorkingShare = 0.44;

    /// <summary>Доля добавленной стоимости, уходящая на зарплаты.</summary>
    public const double LabourShare = 0.50;

    /// <summary>Гражданские расходы государства к ВВП: соцвыплаты, управление.</summary>
    public const double CivilSpending = 0.15;

    /// <summary>Насколько за неделю может сдвинуться множитель цены.</summary>
    public const double PriceStep = 0.03;

    public const double PriceFloor = 0.6;
    public const double PriceCeiling = 1.6;

    private const double TaxStep = 0.002;
    private const double MinTax = 0.10;
    private const double MaxTax = 0.45;

    /// <summary>Выше этого годовая инфляция не идёт даже при печати.</summary>
    private const double MaxInflation = 0.30;

    /// <param name="grow">Растут ли население и прогресс; в разминке перед партией — нет.</param>
    public static void Run(GameWorld world, bool grow = true)
    {
        if (grow)
        {
            world.Productivity *= Math.Pow(1 + ProductivityGrowth, WeekDays / 365.0);
            foreach (var region in world.Regions) region.Population = (long)(region.Population * Math.Pow(1 + PopulationGrowth, WeekDays / 365.0));
        }

        foreach (var country in world.Countries) Supply(world, country);
        foreach (var country in world.Countries) Produce(world, country);
    }

    /// <summary>Регион снабжается, если от него до столицы держателя можно дойти по его же регионам.</summary>
    public static void Supply(GameWorld world, Country country)
    {
        var capital = world.RegionById[country.Capital];
        var seen = new HashSet<int>();

        if (capital is not null && capital.Controller == country.Id)
        {
            var queue = new Queue<int>([capital.Id]);
            seen.Add(capital.Id);

            while (queue.Count > 0)
            {
                foreach (var next in world.Region(queue.Dequeue()).Neighbours)
                {
                    var region = world.RegionById[next];
                    if (region is null || region.Controller != country.Id || !seen.Add(next)) continue;

                    queue.Enqueue(next);
                }
            }
        }

        // Острова и анклавы своей страны снабжаются морем: отрезает только чужой контроль.
        foreach (var region in world.ControlledBy(country.Id))
        {
            region.Supplied = seen.Contains(region.Id) || region.Owner == country.Id;
        }
    }

    private static void Produce(GameWorld world, Country country)
    {
        var catalog = world.Catalog;
        var goods = catalog.Goods.Count;

        Array.Clear(country.ProducedWeek);
        Array.Clear(country.ConsumedWeek);

        var regions = world.ControlledBy(country.Id).ToList();
        if (regions.Count == 0)
        {
            country.GdpReal = 0;
            country.Potential = 0;
            return;
        }

        var population = regions.Sum(r => (double)r.Population);
        var stacks = regions.Where(r => r.Supplied).SelectMany(r => r.Plants).Where(p => p.Count > 0).ToList();

        country.Potential = regions.SelectMany(r => r.Plants).Sum(p => p.Count * Added(catalog, p.Building)) * 365 * world.Productivity;
        var labour = Labour(world, country, population, stacks);
        Grow(world, country, population, labour);
        Capacity(world, country, population, stacks, labour);
        var shelf = Shelf(world, country, population, stacks);

        // Сколько каждая стопка хотела бы сделать и сколько ей на это нужно сырья.
        var wanted = new double[stacks.Count];
        var request = new double[goods];
        for (var i = 0; i < stacks.Count; i++)
        {
            var building = catalog.Buildings[stacks[i].Building];
            var room = building.Outputs.Length == 0 ? 1.0 : building.Outputs.Min(o => shelf[o.Good]);
            wanted[i] = stacks[i].Count * labour * room * world.Productivity;

            foreach (var (good, amount) in building.Inputs) request[good] += wanted[i] * amount * WeekDays;
            foreach (var (good, amount) in building.Upkeep) request[good] += wanted[i] * amount * WeekDays;
        }

        var fill = country.Fill;
        for (var g = 0; g < goods; g++)
        {
            country.DemandWeek[g] += request[g];
            fill[g] = request[g] <= 0 ? 1 : Math.Min(1, country.Stock[g] / request[g]);
        }

        var addedReal = 0.0;
        for (var i = 0; i < stacks.Count; i++)
        {
            var plant = stacks[i];
            var building = catalog.Buildings[plant.Building];
            var share = building.Inputs.Length == 0 ? 1.0 : building.Inputs.Min(input => fill[input.Good]);
            var runs = wanted[i] * share;
            plant.Load = plant.Count > 0 ? runs / (plant.Count * world.Productivity) : 0;

            var revenue = 0.0;
            var cost = 0.0;
            foreach (var (good, amount) in building.Inputs)
            {
                var used = runs * amount * WeekDays;
                country.Stock[good] = Math.Max(0, country.Stock[good] - used);
                cost += used * country.Price(good, catalog.Goods[good].BasePrice);
                addedReal -= used * catalog.Goods[good].BasePrice;
            }

            // Ремонт не останавливает завод: чего нет на складе, того и не потратят.
            foreach (var (good, amount) in building.Upkeep)
            {
                var used = Math.Min(country.Stock[good], runs * amount * WeekDays);
                country.Stock[good] -= used;
                cost += used * country.Price(good, catalog.Goods[good].BasePrice);
            }

            foreach (var (good, amount) in building.Outputs)
            {
                var made = runs * amount * WeekDays;
                country.Stock[good] += made;
                country.ProducedWeek[good] += made;
                revenue += made * country.Price(good, catalog.Goods[good].BasePrice);
                addedReal += made * catalog.Goods[good].BasePrice;
            }

            Pay(world, country, plant, revenue - cost);
        }

        Consume(world, country, population);

        country.GdpReal = addedReal * 365 / WeekDays;
    }

    /// <summary>Расход при полной загрузке. Полка мерится им, а не нынешним расходом: тот падал вслед
    /// за срезанным выпуском, «запас в днях» рос, и выпуск резался снова — до нуля.</summary>
    private static void Capacity(GameWorld world, Country country, double population, List<PlantStack> stacks, double labour)
    {
        Array.Clear(country.CapacityUse);
        Array.Clear(country.CapacityOut);
        foreach (var plant in stacks)
        {
            var building = world.Catalog.Buildings[plant.Building];
            foreach (var (good, amount) in building.Inputs) country.CapacityUse[good] += plant.Count * labour * amount * world.Productivity;
            foreach (var (good, amount) in building.Upkeep) country.CapacityUse[good] += plant.Count * labour * amount * world.Productivity;
            foreach (var (good, amount) in building.Outputs) country.CapacityOut[good] += plant.Count * labour * amount * world.Productivity;
        }

        foreach (var (good, rate) in country.Needs) country.CapacityUse[good] += population / 1e6 * rate;
    }

    /// <summary>Рук хватает на столько же заводов на душу, сколько на старте.</summary>
    private static double Labour(GameWorld world, Country country, double population, List<PlantStack> stacks)
    {
        var need = Jobs(world.Catalog, stacks);
        if (need <= 0 || country.WorkNeed0 <= 0 || country.Workforce0 <= 0) return 1;

        var workforce = population * WorkingShare;
        return Math.Clamp(workforce / country.Workforce0 / (need / country.WorkNeed0), 0, 1);
    }

    /// <summary>Рабочие места на полном ходу. По загрузке считать нельзя: руки то освобождались, то разбирались,
    /// и ВВП малых стран качался каждую неделю.</summary>
    public static double Jobs(Catalog catalog, IEnumerable<PlantStack> stacks) =>
        stacks.Sum(p => p.Count * catalog.Buildings[p.Building].Workers);

    /// <summary>Добавленная стоимость одного здания за сутки на полном ходу, в базовых ценах.</summary>
    public static double Added(Catalog catalog, int building)
    {
        var info = catalog.Buildings[building];
        return info.Outputs.Sum(o => o.Amount * catalog.Goods[o.Good].BasePrice)
            - info.Inputs.Sum(i => i.Amount * catalog.Goods[i.Good].BasePrice);
    }

    /// <summary>Потребление на душу идёт за достатком. Мерка достатка — мощность заводов на душу,
    /// а не сегодняшний выпуск: от выпуска выходила петля, и спад нужд сам себя разгонял.</summary>
    private static void Grow(GameWorld world, Country country, double population, double labour)
    {
        if (country.IncomeStart <= 0 || population <= 0 || country.Needs0.Length != country.Needs.Length) return;

        var income = Math.Clamp(country.Potential * labour / population / country.IncomeStart, 0.2, 5);
        var spentBefore = 0.0;
        var spentNow = 0.0;
        for (var i = 0; i < country.Needs.Length; i++)
        {
            var price = world.Catalog.Goods[country.Needs[i].Good].BasePrice;
            country.Needs[i].Rate = country.Needs0[i].Rate * Math.Pow(income, world.NeedElasticity[i]);
            spentBefore += country.Needs0[i].Rate * price;
            spentNow += country.Needs[i].Rate * price;
        }

        // Эластичность меняет доли трат, но не их сумму: тратят ровно столько, насколько выросли доходы.
        // Без этого услуги с эластичностью 1.3 обгоняли доход, и нужды малых стран сползали.
        var scale = spentNow > 0 ? spentBefore * income / spentNow : 1;
        for (var i = 0; i < country.Needs.Length; i++) country.Needs[i].Rate *= scale;
    }

    /// <summary>Сколько места на полке под каждый товар: полная загрузка, пока запас не выше нормы.</summary>
    private static double[] Shelf(GameWorld world, Country country, double population, List<PlantStack> stacks)
    {
        var catalog = world.Catalog;
        var shelf = new double[catalog.Goods.Count];

        for (var g = 0; g < shelf.Length; g++)
        {
            var good = catalog.Goods[g];
            if (good.Perishable)
            {
                // Услуги не лежат на складе: делают ровно столько, сколько просят люди.
                var need = country.Needs.Where(n => n.Good == g).Sum(n => population / 1e6 * n.Rate);
                var could = stacks.Sum(p => p.Count * catalog.Buildings[p.Building].Outputs
                    .Where(o => o.Good == g).Sum(o => o.Amount)) * world.Productivity;
                shelf[g] = could <= 0 ? 1 : Math.Clamp(need / could, MinLoad, 1);
                continue;
            }

            var use = Math.Max(country.CapacityUse[g], country.DailyUse[g]) + country.ExportUse[g];
            var days = use <= 0 ? FullDays : country.Stock[g] / use;
            shelf[g] = Math.Clamp((FullDays - days) / (FullDays - NormDays), MinLoad, 1);
        }

        return shelf;
    }

    /// <summary>Добавленная стоимость делится: зарплаты, налог в казну, остаток хозяину.</summary>
    private static void Pay(GameWorld world, Country country, PlantStack plant, double added)
    {
        var tax = Math.Max(0, added) * country.TaxRate;
        var owner = added - added * LabourShare - tax;

        country.Treasury += tax;
        country.TaxesWeek += tax;

        // Завод в занятом регионе платит не своей компании, а казне захватчика.
        if (world.CompanyOf(plant.Company) is { Alive: true } company && company.Country == country.Id)
        {
            company.Cash += owner;
            company.ProfitWeek += owner;
        }
        else
        {
            country.Treasury += owner;
            country.TaxesWeek += owner;
        }
    }

    private static void Consume(GameWorld world, Country country, double population)
    {
        var catalog = world.Catalog;
        var wanted = 0.0;
        var got = 0.0;

        foreach (var (good, rate) in country.Needs)
        {
            var need = population / 1e6 * rate * WeekDays;
            var taken = Math.Min(need, country.Stock[good]);

            country.Stock[good] -= taken;
            country.DemandWeek[good] += need;
            country.ConsumedWeek[good] += taken;

            var price = catalog.Goods[good].BasePrice;
            wanted += need * price;
            got += taken * price;
        }

        // Неделя без услуги не копится: стрижку на завтра не отложишь.
        foreach (var good in catalog.Goods.Where(g => g.Perishable)) country.Stock[good.Index] = 0;

        country.Satisfaction = wanted <= 0 ? 1 : got / wanted;
    }

    /// <summary>Конец недели: казна, инфляция, цены, расход, компании.</summary>
    public static void Close(GameWorld world)
    {
        var people = world.Regions.GroupBy(r => r.Owner).ToDictionary(g => g.Key, g => g.Sum(r => (double)r.Population));
        foreach (var country in world.Countries)
        {
            Finance(country, people.GetValueOrDefault(country.Id));
            Prices(world, country);

            for (var g = 0; g < country.DailyUse.Length; g++)
            {
                var today = country.DemandWeek[g] / WeekDays;
                country.DailyUse[g] = country.DailyUse[g] <= 0 ? today : country.DailyUse[g] + (today - country.DailyUse[g]) / 4;
                country.ExportUse[g] += (country.ExportedWeek[g] / WeekDays - country.ExportUse[g]) / 4;
            }

            // Спрос копится всю неделю, с боями и стройкой, и обнуляется только здесь.
            Array.Clear(country.DemandWeek);
        }

        Companies(world);
        Close(world.Catalog, world.Regions);
    }

    /// <summary>Ниже этой загрузки мирный завод понемногу закрывают.</summary>
    private const double CloseBelow = 0.6;

    /// <summary>Какую долю зданий стопки закрывают за неделю при полном простое.</summary>
    private const double CloseRate = 0.002;

    /// <summary>Из малой стопки не закрывают: у микрогосударства одно здание — заметная доля всей экономики.</summary>
    private const int CloseMinCount = 10;

    /// <summary>Долго недогруженные заводы закрываются, и их люди уходят туда, где товара не хватает.</summary>
    /// <remarks>Без этого фермы с ростом дохода стояли на треть, а руки на услуги не освобождались.
    /// Военные заводы не трогают: в мирное время им и положено простаивать.</remarks>
    private static void Close(Catalog catalog, IEnumerable<Region> regions)
    {
        foreach (var region in regions)
        {
            foreach (var plant in region.Plants)
            {
                var building = catalog.Buildings[plant.Building];
                if (plant.Count < CloseMinCount || building.Sector == "Military" || plant.Load >= CloseBelow) continue;

                plant.Closing += plant.Count * CloseRate * (CloseBelow - plant.Load) / CloseBelow;
                while (plant.Closing >= 1 && plant.Count >= CloseMinCount)
                {
                    plant.Count--;
                    plant.Closing -= 1;
                }
            }

            region.Plants.RemoveAll(p => p.Count <= 0);
        }
    }

    /// <summary>Расходы государства; не хватило — печатают, и это поднимает инфляцию.</summary>
    private static void Finance(Country country, double population)
    {
        var gdpWeek = country.GdpReal * country.PriceLevel * WeekDays / 365;

        // Печать меряют от ВВП не ниже полутысячи долларов на душу: у карликов без заводов
        // любой убыток госпредприятия иначе давал гиперинфляцию.
        var baseWeek = Math.Max(gdpWeek, population * 0.5 * country.PriceLevel * WeekDays / 365);
        var spending = gdpWeek * (CivilSpending + country.DefenceShare);

        // Сверх года трат казна не копит: излишек уходит людям пособиями и вложениями.
        var excess = country.Treasury - spending - spending * TreasuryWeeks;
        if (excess > 0) spending += excess * 0.05;

        country.Treasury -= spending;
        country.SpentWeek += spending;
        country.PrintedWeek = 0;

        // Советник двигает налог по чуть-чуть: казна меньше двух месяцев трат — выше, больше полугода — ниже.
        if (country.AutoTax && spending > 0)
        {
            var weeks = country.Treasury / spending;
            if (weeks < 8) country.TaxRate = Math.Min(MaxTax, country.TaxRate + TaxStep);
            else if (weeks > 26) country.TaxRate = Math.Max(MinTax, country.TaxRate - TaxStep);
        }

        if (country.Treasury < 0)
        {
            country.PrintedWeek = -country.Treasury;
            country.Treasury = 0;
        }

        // Инфляция идёт к цели за квартал: два процента фона, печать и общий дефицит товаров.
        var printed = baseWeek > 0 ? Math.Min(1, country.PrintedWeek / baseWeek) : 0;
        var shortage = 1 - country.Satisfaction;
        var target = Math.Min(MaxInflation, 0.02 + 1.5 * printed + 0.3 * Math.Max(0, shortage - country.ShortageUsual));
        country.ShortageUsual += (shortage - country.ShortageUsual) / 26;
        country.Inflation += (target - country.Inflation) * WeekDays / 90.0;
        country.PriceLevel *= Math.Exp(country.Inflation * WeekDays / 365);

        country.TaxesLast = country.TaxesWeek;
        country.SpentLast = country.SpentWeek;
        country.TaxesWeek = 0;
        country.SpentWeek = 0;
    }

    /// <summary>Цена идёт за днями запаса: мало товара — дороже, много — дешевле, в коридоре и медленно.</summary>
    private static void Prices(GameWorld world, Country country)
    {
        var catalog = world.Catalog;
        for (var g = 0; g < country.PriceMult.Length; g++)
        {
            double target;
            if (catalog.Goods[g].Perishable)
            {
                var asked = country.DemandWeek[g];
                target = asked <= 0 ? 1 : 1 + 0.5 * (1 - country.ConsumedWeek[g] / asked);
            }
            else
            {
                // Вывоз тоже расход: иначе у экспортёра цена его товара всегда лежала на полу.
                var use = country.DailyUse[g] + country.ExportUse[g];
                var days = use <= 0 ? NormDays : country.Stock[g] / use;
                // Ниже нормы цена растёт круто, выше — полого: запас между нормой и полкой — обычное дело, а не затоваривание.
                target = days < NormDays
                    ? 1 + 0.5 * (NormDays - days) / NormDays
                    : 1 - 0.4 * (days - NormDays) / (FullDays - NormDays);
            }

            target = Math.Clamp(target, PriceFloor, PriceCeiling);
            var now = country.PriceMult[g];
            country.PriceMult[g] = now + Math.Clamp(target - now, -PriceStep * now, PriceStep * now);
        }
    }

    /// <summary>Полгода в минусе — компания разоряется, её заводы забирает государство.</summary>
    private static void Companies(GameWorld world)
    {
        foreach (var company in world.Companies.Where(c => c.Alive))
        {
            company.WeeksInRed = company.Cash < 0 ? company.WeeksInRed + 1 : 0;

            // Сверх годовой прибыли касса не копится: излишек уходит владельцам.
            var cap = Math.Max(0, company.ProfitWeek) * 52;
            if (company.ProfitWeek > 0 && company.Cash > cap) company.Cash = cap;
            company.ProfitLast = company.ProfitWeek;
            company.ProfitWeek = 0;

            if (company.WeeksInRed < 26) continue;

            company.Alive = false;
            foreach (var plant in world.Regions.SelectMany(r => r.Plants).Where(p => p.Company == company.Id))
            {
                plant.Company = Company.State;
            }
        }
    }
}
