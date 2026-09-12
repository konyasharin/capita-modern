using CapitaModern.Core.Buildings;
using CapitaModern.Core.Economy;
using CapitaModern.Core.Loading;

namespace CapitaModern.Core.World;

/// <summary>Раздаёт стартовые здания компаниям.</summary>
/// <remarks>
/// Кто именно владеет каждым заводом мира, не знает никто: такого списка не существует.
/// Поэтому компании не берутся из справочника целиком, а нарезаются из того, что в стране
/// есть, — но имена у главных настоящие. Газпром, Toyota, Apple: страну узнают по ним, и
/// новость «Норникель закрыл рудник» читается иначе, чем «RUS-Добыча-3».
///
/// Нарезка неравномерная, и это не украшение. В любой отрасли несколько крупных игроков и
/// длинный хвост мелких: доля первой пятёрки в добыче или металлургии обычно больше
/// половины. Мелкие фирмы не бутафория — они держат хвост выпуска, разоряются первыми и
/// первыми же лезут в новые дела.
/// </remarks>
public static class Founders
{
    /// <summary>Сколько безымянных фирм на отрасль сверх названных по имени.</summary>
    public const int SmallPerSector = 12;

    /// <summary>Какую долю оставшегося берёт очередная компания, в сотых.</summary>
    public const int BiggestShare = 45;

    /// <summary>Каждая столькая компания берётся за вторую отрасль. Многопрофильных
    /// меньшинство, но они есть: чеболи, конгломераты, госкорпорации.</summary>
    public const int WideEvery = 9;

    /// <summary>Сколько денег у компании на старте, в днях её же выпуска.</summary>
    public const int CashDays = 30;

    public static List<Company> Found(GameWorld world, CompaniesFile? known = null)
    {
        var companies = new List<Company>();
        var next = 1;

        foreach (var country in world.Countries)
        {
            var named = known?.Companies.GetValueOrDefault(country.Iso) ?? [];
            var holdings = Holdings(world, country);

            foreach (var (sector, inSector) in holdings)
            {
                companies.AddRange(Share(world, country, sector, inSector, named, ref next));
            }
        }

        Widen(companies);

        return companies;
    }

    /// <summary>Что у страны есть, разложенное по отраслям.</summary>
    private static Dictionary<Sector, List<(int Region, BuildingType Type, int Count)>> Holdings(
        GameWorld world, Country country)
    {
        var byRegion = new Dictionary<Sector, List<(int Region, BuildingType Type, int Count)>>();

        foreach (var region in world.RegionsOf(country.Id))
        {
            foreach (var (type, _) in region.BuildingsCount)
            {
                var owned = region.BuildingsOf(type, country.Id);
                if (owned <= 0) continue;

                var sector = world.Buildings[type].Sector;
                if (sector == Sector.People) continue;

                if (!byRegion.TryGetValue(sector, out var list)) byRegion[sector] = list = [];

                list.Add((region.Id, type, owned));
            }
        }

        return byRegion;
    }

    /// <summary>Режет отрасль: сперва названные по имени, за ними безымянный хвост.</summary>
    private static List<Company> Share(
        GameWorld world,
        Country country,
        Sector sector,
        List<(int Region, BuildingType Type, int Count)> holdings,
        NamedCompanyDto[] named,
        ref int next)
    {
        var made = new List<Company>();

        foreach (var dto in named)
        {
            if (!Enum.TryParse<Sector>(dto.Sector, out var theirs) || theirs != sector) continue;

            made.Add(new Company(next++, country.Id, dto.Name, [sector], known: true));
        }

        for (var i = 0; i < SmallPerSector; i++)
        {
            made.Add(new Company(next++, country.Id, $"{country.Iso} {Names.Of(sector)} {i + 1}", [sector]));
        }

        foreach (var (region, type, count) in holdings)
        {
            var left = count;
            for (var i = 0; i < made.Count && left > 0; i++)
            {
                // Последней достаётся остаток целиком: иначе на дроблении теряются здания.
                var take = i == made.Count - 1 ? left : Math.Max(1, left * BiggestShare / 100);

                made[i].Add(region, type, take);
                left -= take;
            }
        }

        // Пустые не нужны: в маленькой стране на отрасль приходится одно здание.
        made.RemoveAll(company => company.Size == 0);

        foreach (var company in made) company.Earn(StartCash(world, country, company));

        return made;
    }

    /// <summary>Каждая девятая берётся за соседнюю отрасль.</summary>
    private static void Widen(List<Company> companies)
    {
        for (var i = WideEvery - 1; i < companies.Count; i += WideEvery)
        {
            var company = companies[i];
            var second = Next(company.Focus[0]);

            var wider = new Company(
                company.Id, company.Country, company.Name, [company.Focus[0], second],
                company.Cash, company.Known);

            foreach (var ((region, type), count) in company.Buildings) wider.Add(region, type, count);

            companies[i] = wider;
        }
    }

    /// <summary>Соседняя отрасль по переделу: добыча тянется в тяжёлую, тяжёлая в
    /// гражданскую. Так и растут конгломераты — вверх по цепочке, а не куда попало.</summary>
    public static Sector Next(Sector sector) => sector switch
    {
        Sector.Mining => Sector.Heavy,
        Sector.Power => Sector.Mining,
        Sector.Heavy => Sector.Civil,
        Sector.Civil => Sector.Services,
        Sector.Military => Sector.Heavy,
        _ => Sector.Civil,
    };

    /// <summary>Месячная выручка компании в стартовых ценах — на первые стройки.</summary>
    private static Money StartCash(GameWorld world, Country country, Company company)
    {
        var daily = default(Money);

        foreach (var ((_, type), count) in company.Buildings)
        {
            foreach (var (good, amount) in world.Buildings[type].Outputs)
            {
                daily += new Money(
                    (long)((Int128)country.State.Prices.StartOf(good).Raw * amount.Raw * count / GoodAmount.Scale));
            }
        }

        return new Money(daily.Raw * CashDays);
    }

    /// <summary>Короткое имя отрасли для безымянных фирм.</summary>
    private static class Names
    {
        public static string Of(Sector sector) => sector switch
        {
            Sector.Mining => "добыча",
            Sector.Power => "энергетика",
            Sector.Heavy => "тяжпром",
            Sector.Civil => "гражданпром",
            Sector.Military => "оборонпром",
            Sector.Services => "услуги",
            _ => "прочее",
        };
    }
}
