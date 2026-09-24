using System.Data;
using CapitaModern.Core.Buildings;
using CapitaModern.Core.Economy;
using CapitaModern.Core.Politics;
using CapitaModern.Core.World;

namespace CapitaModern.Core.Loading;

/// <summary>Собирает мир из файлов. Принимает содержимое, а не пути: в игре файлы
/// достаёт Godot, в консоли — File.</summary>
public static class WorldDataLoader
{
    public static GameWorld LoadWorld(
        string countriesJson,
        string regionsJson,
        string buildingsJson,
        string startBuildingsJson,
        string consumptionJson,
        string pricesJson,
        string reservesJson,
        string goodsJson,
        string keyRatesJson,
        string blocsJson,
        string efficiencyJson,
        string moneySupplyJson,
        string tradeCostsJson,
        string neighboursJson,
        string basinsJson,
        string currenciesJson,
        string companiesJson,
        string defenceJson,
        string traitsJson)
    {
        var consumptionFile = LoadConsumptionFile(consumptionJson);
        var needs = new Needs(consumptionFile.UnitPerMillionPeople, consumptionFile.IncomeElasticity);
        var startPrices = LoadPricesFile(pricesJson).Prices;
        var reserves = LoadReservesFile(reservesJson);
        var keyRates = JsonReader.Read<KeyRatesFile>(keyRatesJson);
        var blocs = JsonReader.Read<BlocsFile>(blocsJson);
        var efficiencyFile = JsonReader.Read<EfficiencyFile>(efficiencyJson);
        var moneySupply = JsonReader.Read<MoneySupplyFile>(moneySupplyJson);
        var tradeCostsFile = JsonReader.Read<TradeCostsFile>(tradeCostsJson);
        var neighbours = JsonReader.Read<NeighboursFile>(neighboursJson);
        var basins = JsonReader.Read<BasinsFile>(basinsJson);
        var currencies = JsonReader.Read<CurrenciesFile>(currenciesJson).Currencies;
        var defence = JsonReader.Read<DefenceFile>(defenceJson);
        var traits = JsonReader.Read<TraitsFile>(traitsJson);
        var goodDtos = JsonReader.Read<GoodDto[]>(goodsJson);
        var elasticity = new Elasticity(
            goodDtos.ToDictionary(dto => dto.Id, dto => dto.DemandElasticity),
            goodDtos.ToDictionary(dto => dto.Id, dto => dto.SupplyElasticity),
            goodDtos.ToDictionary(dto => dto.Id, dto => dto.Substitution));
        var startBuildings = WithMargin(LoadStartBuildingsFile(startBuildingsJson).StartBuildings);
        var countriesFile = LoadCountriesFile(countriesJson);
        var regionsFile = LoadRegionsFile(regionsJson);
        if (countriesFile.Height != regionsFile.Height || countriesFile.Width != regionsFile.Width)
            throw new InvalidDataException("Не совпадают размеры карты в countries.json и regions.json");

        Region[] regions = regionsFile.Regions.Select(dto =>
            ToRegion(dto, startBuildings.GetValueOrDefault(dto.Id.ToString(), new()))
        ).ToArray();
        // Население нужно до сборки стран: тем, кого нет в reserves.json, деньги
        // считаются по нему.
        var populations = new Dictionary<byte, Population>();
        foreach (var region in regions)
        {
            populations[region.LargestOwner] =
                populations.GetValueOrDefault(region.LargestOwner) + region.Demographics.Population;
        }

        var idByIso = countriesFile.Countries.ToDictionary(dto => dto.Iso, dto => dto.Id);
        Country[] countries = countriesFile.Countries
            .Select(dto => ToCountry(dto, startPrices, reserves, idByIso, populations.GetValueOrDefault(dto.Id),
                currencies.GetValueOrDefault(dto.Iso),
                moneySupply,
                defence.Share.GetValueOrDefault(dto.Iso, defence.DefaultShare)))
            .ToArray();

        GiveTraits(countries, traits);

        // Приходящую валюту по умолчанию кладут туда же, где лежит основная часть
        // резервов. Увести её в другое место — решение игрока.
        var mainCustodian = reserves.Composition
            .Where(pair => idByIso.ContainsKey(pair.Key))
            .OrderByDescending(pair => pair.Value)
            .Select(pair => idByIso[pair.Key])
            .FirstOrDefault();

        foreach (var country in countries)
        {
            country.KeyRate = keyRates.ByIso.GetValueOrDefault(country.Iso, keyRates.DefaultRate);
            country.State.Custody = mainCustodian;
        }
        BuildingCatalog buildingCatalog = BuildingCatalog.FromJson(buildingsJson);

        var efficiency = new Efficiency(
            countries
                .Where(country => efficiencyFile.ByIso.ContainsKey(country.Iso))
                .ToDictionary(
                    country => country.Id,
                    country => efficiencyFile.ByIso[country.Iso] is var dto
                        ? (dto.Skill, dto.Tech, dto.Condition)
                        : default),
            efficiencyFile.Sensitivity,
            efficiencyFile.ShowsInOutput,
            efficiencyFile.OutputMean);

        var landlocked = tradeCostsFile.Landlocked.ToHashSet();
        var tradeCosts = new TradeCosts(
            goodDtos.ToDictionary(dto => dto.Id, dto => dto.FreightShare),
            countries.Where(country => landlocked.Contains(country.Iso)).Select(country => country.Id).ToHashSet(),
            countries.ToDictionary(
                country => country.Id,
                country => tradeCostsFile.TariffByIso.GetValueOrDefault(country.Iso, tradeCostsFile.DefaultTariff)),
            tradeCostsFile.LandlockedFactor);

        // Пролив соединяет не только два главных бассейна: рядом с ним попадаются мелкие
        // заливы. Берём все пары — путь через них всё равно найдётся кратчайший.
        var straits = new List<(int, int, byte)>();
        foreach (var strait in basins.Straits)
        {
            if (!idByIso.TryGetValue(strait.Owner, out var owner)) continue;

            for (var i = 0; i < strait.Joins.Length; i++)
            {
                for (var j = i + 1; j < strait.Joins.Length; j++)
                {
                    straits.Add((strait.Joins[i], strait.Joins[j], owner));
                }
            }
        }

        var routes = new Routes(
            countries.ToDictionary(
                country => country.Id,
                country => neighbours.ByIso.TryGetValue(country.Iso, out var dto)
                    ? dto.Neighbours.Keys.Where(idByIso.ContainsKey).Select(iso => idByIso[iso]).ToArray()
                    : []),
            countries.ToDictionary(
                country => country.Id,
                country => basins.ByIso.GetValueOrDefault(country.Iso, [])),
            straits);

        var relations = new Relations(countries.ToDictionary(
            country => country.Id,
            country => blocs.ByIso.GetValueOrDefault(country.Iso, Bloc.NonAligned)));

        var world = new GameWorld(regions, countries, buildingCatalog, needs,
            new WorldMarket(new Prices(startPrices)), elasticity, relations, efficiency, tradeCosts, routes);

        FillStores(world);
        world.Settle(Founders.Found(world, JsonReader.Read<CompaniesFile>(companiesJson)));

        return world;
    }

    /// <summary>Наполняет склады на старте нормой запаса.</summary>
    /// <remarks>
    /// Пустой склад означает нулевое покрытие, а нулевое покрытие двигает цену полным
    /// шагом вверх — сразу у всех стран и по всем товарам. Первые месяцы партии уходили
    /// на этот разгон, и половина цен успевала уехать за коридор ещё до того, как
    /// хозяйство хоть что-нибудь показало.
    ///
    /// Сорок дней — та самая норма <see cref="Prices.TargetCoverDays"/>, при которой цена
    /// стоит на месте. Это и значит «нехватки нет», а на первое января двухтысячного
    /// двадцатого в мире общей нехватки и не было: ни ковидного дефицита лекарств, ни
    /// нехватки микросхем — обе беды случились позже.
    ///
    /// Спрос берётся расчётный: сколько съедят заводы на полном ходу плюс базовая нужда
    /// населения. Настоящего спроса до первого тика взять неоткуда.
    /// </remarks>
    /// <seealso cref="StartCoverDays"/>
    /// <summary>Во сколько сотых мощность больше выпуска, из которого выведена.</summary>
    /// <remarks>
    /// Число заводов посчитано из настоящего мирового выпуска, а настоящие заводы загружены
    /// примерно на три четверти. Значит мощностей в жизни на четверть больше того, что они
    /// дают, и без этой прибавки мир сведён впритык: узкие товары — материалы, электричество,
    /// электроника — идут с запасом в один-два процента, и любая неровность рвёт цепочку.
    ///
    /// Раньше прибавить было нельзя: заводы работали на полную, и лишняя мощность стала бы
    /// лишним выпуском. Теперь `Simulation.Ordered` сбавляет загрузку по спросу, и мощность
    /// с выпуском разошлись — можно дать запас, не прибавив ни единицы товара.
    /// </remarks>
    private const int CapacityMargin = 100;

    /// <summary>Добавляет заводам запас мощности.</summary>
    private static Dictionary<string, Dictionary<BuildingType, int>> WithMargin(
        Dictionary<string, Dictionary<BuildingType, int>> start)
    {
        foreach (var byType in start.Values)
        {
            foreach (var type in byType.Keys.ToArray())
            {
                byType[type] = byType[type] * CapacityMargin / 100;
            }
        }

        return start;
    }

    /// <summary>На сколько суток расхода хватает стартового запаса.</summary>
    /// <remarks>
    /// Ровно норма <see cref="Prices.TargetCoverDays"/>. Было вдвое выше: правило полки режет
    /// загрузку по отношению нормы к складу, и с двойным запасом заводы первый месяц шли
    /// вполсилы, потом разгонялись до 93% и снова сползали. С нормой ход с первых дней ровный.
    /// </remarks>
    private const int StartCoverDays = Prices.TargetCoverDays;

    private static void FillStores(GameWorld world)
    {
        foreach (var country in world.Countries)
        {
            var daily = new Dictionary<GoodType, GoodAmount>();

            foreach (var type in Enum.GetValues<BuildingType>())
            {
                var count = world.BuildingsOf(country.Id, type);
                if (count == 0) continue;

                foreach (var (good, amount) in world.Buildings[type].Inputs)
                {
                    daily[good] = daily.GetValueOrDefault(good) + new GoodAmount(amount.Raw * count);
                }
            }

            var millions = world.PopulationOf(country.Id).Whole / 1_000_000.0;
            foreach (var (good, rate) in world.Needs.BaseRates)
            {
                daily[good] = daily.GetValueOrDefault(good) + new GoodAmount((long)(rate.Raw * millions));
            }

            foreach (var (good, amount) in daily)
            {
                // Услуги в запас не кладут: они сгорали в конце первого дня, а до того валили свою
                // цену на дно, и назавтра она прыгала вчетверо — у бедных стран вся корзина.
                if (good == GoodType.Services) continue;

                country.State.Stock.Store(good, new GoodAmount(amount.Raw * StartCoverDays));
            }
        }
    }


    private static CountriesFile LoadCountriesFile(string json) => JsonReader.Read<CountriesFile>(json);
    private static RegionsFile LoadRegionsFile(string json) => JsonReader.Read<RegionsFile>(json);
    private static StartBuildingsFile LoadStartBuildingsFile(string json) => JsonReader.Read<StartBuildingsFile>(json);
    private static ConsumptionFile LoadConsumptionFile(string json) => JsonReader.Read<ConsumptionFile>(json);
    private static PricesFile LoadPricesFile(string json) => JsonReader.Read<PricesFile>(json);
    private static ReservesFile LoadReservesFile(string json) => JsonReader.Read<ReservesFile>(json);
    private static Region ToRegion(RegionDto dto, Dictionary<BuildingType, int> buildings) => new(
        dto.Id,
        Population.FromWhole(dto.Population),
        new Dictionary<byte, int>{ [dto.Country] = dto.Cells },
        buildings,
        dto.Deposits
    );
    /// <summary>Три месяца товарного импорта. Вывести это из состояния игры нельзя:
    /// страна без денег не купит сырьё, без сырья не произведёт и никогда не выберется.</summary>
    private static Money ReservesOf(CountryDto dto, ReservesFile reserves, Population population)
    {
        if (reserves.ByIso.TryGetValue(dto.Iso, out var millions)) return Money.FromWhole(millions * 1000);

        return Money.FromWhole(reserves.DefaultPerMillionPeople * population.Whole / 1_000_000 * 1000);
    }

    /// <summary>Вся денежная масса страны: доля от её ВВП.</summary>
    /// <remarks>Без местных денег государству нечем платить зарплату, а населению не на
    /// что покупать — круг внутреннего оборота не с чего начать.</remarks>
    private static Money MoneyOf(CountryDto dto, MoneySupplyFile supply) => Money.FromWhole(
        // −99 в Natural Earth означает «данных нет», а не отрицательный ВВП.
        Math.Max(0, dto.Gdp) * 1000 *
        supply.ByIso.GetValueOrDefault(dto.Iso, supply.DefaultShareOfGdp) / 100);

    // Половина денег лежит у населения: вклады граждан — примерно столько же в денежной
    // массе, сколько всё остальное. Делится это уже в ToCountry, после пересчёта по курсу.

    /// <summary>Раскладывает сумму по валютам. Доли нормируются по своей сумме: часть
    /// мировых резервов лежит в валютах, которых у нас нет.</summary>
    /// <remarks>Остаток от деления уходит эмитенту с наибольшей долей, иначе на двухстах
    /// странах наберётся заметная недостача.</remarks>
    private static Reserve[] Split(
        CountryDto dto,
        Money total,
        ReservesFile reserves,
        IReadOnlyDictionary<string, byte> idByIso)
    {
        // Золото лежит в своих хранилищах: его и не отнять, ради того и держат.
        var gold = new Money(total.Raw * reserves.GoldShare / 100);
        total -= gold;

        var shares = reserves.Composition
            .Where(pair => idByIso.ContainsKey(pair.Key))
            .OrderByDescending(pair => pair.Value)
            .ToArray();
        var sum = shares.Sum(pair => pair.Value);
        if (sum <= 0) return [new Reserve(ReserveKind.Metal, dto.Id, dto.Id, gold + total)];

        var left = total;
        var held = new Reserve[shares.Length + 1];
        held[^1] = new Reserve(ReserveKind.Metal, dto.Id, dto.Id, gold);
        for (var i = 1; i < shares.Length; i++)
        {
            var part = new Money(total.Raw * shares[i].Value / sum);
            // Валюту держат в банках эмитента: он же её и заморозит, если дойдёт до дела.
            held[i] = new Reserve(
                ReserveKind.ForeignCurrency, idByIso[shares[i].Key], idByIso[shares[i].Key], part);
            left -= part;
        }

        held[0] = new Reserve(
            ReserveKind.ForeignCurrency, idByIso[shares[0].Key], idByIso[shares[0].Key], left);

        return held;
    }

    /// <summary>Страна со своей валютой: цены и деньги пересчитаны из долларов по курсу.</summary>
    /// <remarks>
    /// В данных всё в долларах — и цены товаров, и ВВП, из которого считается масса. У
    /// страны они должны быть в её деньгах, иначе на старте рубль равен доллару, а цена
    /// стройматериалов в России выходит в семьдесят раз ниже мировой.
    ///
    /// Резервы не трогаем: это чужая валюта, и она так и остаётся в мировой мере.
    /// </remarks>
    private static Country ToCountry(
        CountryDto dto,
        IReadOnlyDictionary<GoodType, Money> startPrices,
        ReservesFile reserves,
        IReadOnlyDictionary<string, byte> idByIso,
        Population population,
        CurrencyDto? currency,
        MoneySupplyFile moneySupply,
        int defenceShare)
    {
        var rate = Money.FromWhole(1);
        var money = Currency.Dollar;

        if (currency is { } about)
        {
            rate = new Money((long)(about.Rate * Money.Scale));
            money = new Currency(about.Code, about.Symbol, about.Name);
        }

        var local = startPrices.ToDictionary(pair => pair.Key, pair => InLocal(pair.Value, rate));
        var supply = InLocal(MoneyOf(dto, moneySupply), rate);
        var savings = new Money(supply.Raw / 2);

        return new Country(
            dto.Id,
            dto.Name,
            dto.Iso,
            // Номер продавца пока совпадает с номером страны: государство одно на страну.
            // Цены у каждого свои, поэтому копия, а не общий объект.
            new Producer(
                dto.Id,
                new Stock(new Dictionary<GoodType, GoodAmount>()),
                new Treasury(
                    Split(dto, ReservesOf(dto, reserves, population), reserves, idByIso),
                    // Местные деньги: без них государству нечем платить зарплату, а населению
                    // не на что покупать — круг внутреннего оборота не с чего начать.
                    supply - savings),
                new Prices(local)),
            new Priorities(),
            savings,
            rate
        ) { Bank = new CentralBank(supply), Currency = money, DefenceShare = defenceShare }; // склад - заглушка
    }

    /// <summary>Раздаёт странам черты по списку из файла.</summary>
    /// <remarks>Ключи с пояснениями пропускаются: в файле рядом с каждым списком записано,
    /// по какому признаку черта дана, и эти строки нужны читающему, а не коду.</remarks>
    private static void GiveTraits(Country[] countries, TraitsFile file)
    {
        var byName = file.Traits.ToDictionary(dto => dto.Name, dto => dto.ToTrait());
        var byIso = countries.ToDictionary(country => country.Iso);

        foreach (var (name, isos) in file.Who)
        {
            if (!byName.TryGetValue(name, out var trait)) continue;

            foreach (var iso in isos)
            {
                if (byIso.TryGetValue(iso, out var country)) country.Character.Take(trait);
            }
        }
    }

    /// <summary>Долларовую величину в деньги страны.</summary>
    private static Money InLocal(Money dollars, Money rate) =>
        new((long)((Int128)dollars.Raw * rate.Raw / Money.Scale));
}
