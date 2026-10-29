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

    /// <summary>Рост производительности за год у лидеров при обычном налоге и рынке.</summary>
    private const double BaseGrowth = 0.015;

    /// <summary>Сколько прибавляет отставание от лидеров: за каждое «в e раз беднее».</summary>
    private const double CatchUp = 0.007;

    /// <summary>Без усилий заводы стареют, и производительность сползает.</summary>
    private const double Decay = -0.005;

    private const double MaxGrowth = 0.06;

    /// <summary>Налог, при котором частные вложения обычные; каждый пункт сверху режет их на два процента.</summary>
    private const double NeutralTax = 0.20;

    /// <summary>План строит не там, где выгодно: отдача вложений вдвое ниже.</summary>
    private const double PlannedReturn = 0.5;

    /// <summary>Лидеры по выпуску на душу берутся только среди стран крупнее этого: не карлики и не эмираты.</summary>
    private const double FrontierPopulation = 20e6;

    /// <summary>Склад начинают беречь, когда его меньше чем на столько недель: нехватка нарастает плавно, а не обрушивается в день,
    /// когда склад опустел.</summary>
    private const double RationWeeks = 2;

    /// <summary>Какую долю добытого мир за то же время находит заново.</summary>
    private const double Discovery = 0.9;

    /// <summary>Рудник работает в полную силу, пока остаток выше этой доли стартового запаса; дальше — по остатку.</summary>
    private const double DepletionTail = 0.25;

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

    /// <summary>Сколько сверх расходов советник оставляет казне на госстройку.</summary>
    private const double TaxSurplus = 1.15;
    private const double MinTax = 0.10;
    private const double MaxTax = 0.45;

    /// <summary>Выше этого годовая инфляция не идёт даже при печати.</summary>
    private const double MaxInflation = 0.30;

    /// <param name="grow">Растут ли население и прогресс; в разминке перед партией — нет.</param>
    public static void Run(GameWorld world, bool grow = true)
    {
        if (grow)
        {
            Advance(world);
            foreach (var region in world.Regions) region.Population = (long)(region.Population * Math.Pow(1 + PopulationGrowth, WeekDays / 365.0));
        }

        foreach (var country in world.Countries) Supply(world, country);
        foreach (var country in world.Countries) Produce(world, country);
        Discover(world);
    }

    /// <summary>Разведка: мир находит чуть меньше, чем добыл, и по богатству недр, а не там, где копают.</summary>
    /// <remarks>Без неё запасы у всех кончались разом лет через тридцать, и мир вставал.</remarks>
    private static void Discover(GameWorld world)
    {
        var deposits = world.Regions.Where(r => r.Reserve0.Count > 0).ToList();
        foreach (var good in deposits.SelectMany(r => r.Reserve0.Keys).Distinct())
        {
            var mined = world.Countries.Sum(c => c.ProducedWeek[good]);
            var richness = deposits.Sum(r => r.Deposits.GetValueOrDefault(good));
            if (mined <= 0 || richness <= 0) continue;

            foreach (var region in deposits.Where(r => r.Deposits.ContainsKey(good)))
            {
                region.Reserve[good] = region.Reserve.GetValueOrDefault(good) + mined * Discovery * region.Deposits[good] / richness;
            }
        }
    }

    /// <summary>Производительность каждой страны: бедная догоняет лидеров, но только пока вкладывается.</summary>
    private static void Advance(GameWorld world)
    {
        // ВВП считается по тому, что страна держит, — и население так же: иначе захватчик выглядел богаче.
        var people = world.Regions.GroupBy(r => r.Controller).ToDictionary(g => g.Key, g => g.Sum(r => (double)r.Population));
        var leaders = world.Countries
            .Where(c => c.GdpReal > 0 && people.GetValueOrDefault(c.Id) > FrontierPopulation)
            .Select(c => c.GdpReal / people[c.Id]).OrderByDescending(x => x).Take(5).ToList();
        var frontier = leaders.Count > 0 ? leaders.Average() : 0;

        foreach (var country in world.Countries)
        {
            var population = people.GetValueOrDefault(country.Id);
            if (country.GdpReal <= 0 || population <= 0) continue;

            var gap = frontier > 0 ? Math.Log(Math.Max(1, frontier / (country.GdpReal / population))) : 0;
            // Стройка в модели — плохая мерка вложений: крупным странам её режут руки, и они «не вкладывались».
            // Усилие задают налоговое бремя и строй.
            var effort = Math.Clamp(1 - 2 * (country.TaxRate - NeutralTax), 0.4, 1.2);
            if (country.System == EconomicSystem.Planned) effort *= PlannedReturn;

            var growth = Math.Min(MaxGrowth, Decay + effort * (BaseGrowth + CatchUp * gap));
            country.ProductivityGrowth = growth;
            country.Productivity *= Math.Pow(1 + growth, WeekDays / 365.0);
        }
    }

    /// <summary>Какую долю рудник ещё выдаёт, по остатку месторождения.</summary>
    public static double Richness(Region region, Building building)
    {
        if (building.Deposit < 0 || !region.Reserve0.TryGetValue(building.Deposit, out var start) || start <= 0) return 1;
        return Math.Clamp(region.Reserve.GetValueOrDefault(building.Deposit) / (start * DepletionTail), 0, 1);
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
        var units = regions.Where(r => r.Supplied)
            .SelectMany(r => r.Plants.Where(p => p.Count > 0).Select(p => (Region: r, Plant: p)))
            .ToList();
        var stacks = units.Select(u => u.Plant).ToList();
        var rates = units.Select(u => country.Productivity * Richness(u.Region, catalog.Buildings[u.Plant.Building])).ToArray();

        country.Potential = (regions.SelectMany(r => r.Plants).Sum(p => p.Count * Added(catalog, p.Building)) + country.Retired)
            * 365 * country.Productivity;
        var labour = Labour(world, country, population, stacks);
        Grow(world, country, population, labour);
        Capacity(world, country, population, stacks, rates, labour);
        var shelf = Shelf(world, country, population, stacks, rates);

        // Сколько каждая стопка хотела бы сделать и сколько ей на это нужно сырья.
        var wanted = new double[stacks.Count];
        var request = new double[goods];
        for (var i = 0; i < stacks.Count; i++)
        {
            var building = catalog.Buildings[stacks[i].Building];
            var room = building.Outputs.Length == 0 ? 1.0 : building.Outputs.Min(o => shelf[o.Good]);
            wanted[i] = stacks[i].Count * labour * room * rates[i];

            foreach (var (good, amount) in building.Inputs) request[good] += wanted[i] * amount * WeekDays;
            foreach (var (good, amount) in building.Upkeep) request[good] += wanted[i] * amount * WeekDays;
        }

        var fill = country.Fill;
        for (var g = 0; g < goods; g++)
        {
            country.DemandWeek[g] += request[g];
            var ration = catalog.Goods[g].Perishable ? 1 : RationWeeks;
            fill[g] = request[g] <= 0 ? 1 : Math.Min(1, country.Stock[g] / (request[g] * ration));
        }

        var addedReal = 0.0;
        for (var i = 0; i < stacks.Count; i++)
        {
            var plant = stacks[i];
            var building = catalog.Buildings[plant.Building];
            var share = building.Inputs.Length == 0 ? 1.0 : building.Inputs.Min(input => fill[input.Good]);
            var runs = wanted[i] * share;
            // Загрузка меряется от полной отдачи: выработанный рудник выглядит простаивающим и со временем закрывается.
            plant.Load = plant.Count > 0 ? runs / (plant.Count * country.Productivity) : 0;

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
                addedReal -= used * catalog.Goods[good].BasePrice;
            }

            foreach (var (good, amount) in building.Outputs)
            {
                var made = runs * amount * WeekDays;
                if (good == building.Deposit) units[i].Region.Reserve[good] = Math.Max(0, units[i].Region.Reserve.GetValueOrDefault(good) - made);
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
    private static void Capacity(GameWorld world, Country country, double population, List<PlantStack> stacks, double[] rates, double labour)
    {
        Array.Clear(country.CapacityUse);
        Array.Clear(country.CapacityOut);
        for (var i = 0; i < stacks.Count; i++)
        {
            var plant = stacks[i];
            var full = plant.Count * labour * rates[i];
            var building = world.Catalog.Buildings[plant.Building];
            foreach (var (good, amount) in building.Inputs) country.CapacityUse[good] += full * amount;
            foreach (var (good, amount) in building.Upkeep) country.CapacityUse[good] += full * amount;
            foreach (var (good, amount) in building.Outputs) country.CapacityOut[good] += full * amount;
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

    /// <summary>Добавленная стоимость одного здания за сутки на полном ходу, в базовых ценах, за вычетом ремонта.</summary>
    public static double Added(Catalog catalog, int building)
    {
        var info = catalog.Buildings[building];
        double Value((int Good, double Amount)[] items) => items.Sum(i => i.Amount * catalog.Goods[i.Good].BasePrice);
        return Value(info.Outputs) - Value(info.Inputs) - Value(info.Upkeep);
    }

    /// <summary>Потребление на душу идёт за достатком. Мерка достатка — мощность заводов на душу,
    /// а не сегодняшний выпуск: от выпуска выходила петля, и спад нужд сам себя разгонял.</summary>
    private static void Grow(GameWorld world, Country country, double population, double labour)
    {
        if (country.IncomeStart <= 0 || population <= 0 || country.Needs0.Length != country.Needs.Length) return;

        var income = Math.Clamp(country.Potential * labour / population / country.IncomeStart, 0.2, 5);
        country.Income = income;
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
    private static double[] Shelf(GameWorld world, Country country, double population, List<PlantStack> stacks, double[] rates)
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
                var could = stacks.Select((p, i) => p.Count * rates[i] * catalog.Buildings[p.Building].Outputs
                    .Where(o => o.Good == g).Sum(o => o.Amount)).Sum();
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
            var ration = catalog.Goods[good].Perishable ? 1 : RationWeeks;
            var taken = need <= 0 ? 0 : need * Math.Min(1, country.Stock[good] / (need * ration));

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

            // Вниз зарплата идёт втрое медленнее: после удара страна долго остаётся дорогой.
            country.Wage += (country.Income - country.Wage) / (country.Income > country.Wage ? 156 : 468);

            // Спрос копится всю неделю, с боями и стройкой, и обнуляется только здесь.
            Array.Clear(country.DemandWeek);
        }

        Companies(world);
        ClosePlants(world);
    }

    /// <summary>Ниже этой загрузки мирный завод понемногу закрывают.</summary>
    private const double CloseBelow = 0.6;

    /// <summary>Какую долю зданий стопки закрывают за неделю при полном простое.</summary>
    private const double CloseRate = 0.002;

    /// <summary>Из малой стопки не закрывают: у микрогосударства одно здание — заметная доля всей экономики.</summary>
    private const int CloseMinCount = 10;

    /// <summary>Долго недогруженные мирные заводы закрываются, и их люди уходят туда, где товара не хватает.</summary>
    /// <remarks>Без этого фермы с ростом дохода стояли на треть, а руки на услуги не освобождались.</remarks>
    private static void ClosePlants(GameWorld world)
    {
        foreach (var region in world.Regions)
        {
            foreach (var plant in region.Plants)
            {
                var building = world.Catalog.Buildings[plant.Building];
                if (plant.Count < CloseMinCount || building.Sector == "Military" || plant.Load >= CloseBelow)
                {
                    plant.Closing = 0;
                    continue;
                }

                plant.Closing += plant.Count * CloseRate * (CloseBelow - plant.Load) / CloseBelow;
                while (plant.Closing >= 1 && plant.Count > CloseMinCount)
                {
                    plant.Count--;
                    plant.Closing -= 1;
                    world.Country(region.Owner).Retired += Added(world.Catalog, plant.Building);
                }
            }
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

        // Советник смотрит на поток, а не на остаток: казну тратит и госстройка, и это не дефицит.
        if (country.AutoTax && spending > 0)
        {
            if (country.TaxesWeek < spending) country.TaxRate = Math.Min(MaxTax, country.TaxRate + TaxStep);
            else if (country.TaxesWeek > spending * TaxSurplus) country.TaxRate = Math.Max(MinTax, country.TaxRate - TaxStep);
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
