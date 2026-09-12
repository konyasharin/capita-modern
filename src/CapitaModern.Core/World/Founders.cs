using CapitaModern.Core.Buildings;
using CapitaModern.Core.Economy;

namespace CapitaModern.Core.World;

/// <summary>Раздаёт стартовые здания компаниям.</summary>
/// <remarks>
/// Данных о том, кому что принадлежит, у нас нет и не будет: списка всех заводов мира с
/// владельцами не существует. Поэтому компании не берутся из справочника, а нарезаются из
/// того, что в стране есть.
///
/// Нарезка не равномерная. В любой отрасли несколько крупных игроков и длинный хвост
/// мелких: первая компания получает примерно половину отрасли, вторая половину остатка и
/// так далее. Так и устроена настоящая концентрация — доля первой пятёрки в добыче или
/// металлургии обычно больше половины.
/// </remarks>
public static class Founders
{
    /// <summary>Сколько компаний на отрасль в стране. Больше плодить незачем: хвост из
    /// сотни фирм с долей в тысячную ничего не решает, а тик считает их все.</summary>
    public const int PerSector = 4;

    /// <summary>Какую долю оставшегося берёт очередная компания, в сотых.</summary>
    public const int BiggestShare = 50;

    /// <summary>Каждая столькая компания берётся за вторую отрасль. Многопрофильных
    /// меньшинство, но они есть: чеболи, конгломераты, госкорпорации.</summary>
    public const int WideEvery = 7;

    /// <summary>Сколько денег у компании на старте, в днях её же выпуска.</summary>
    public const int CashDays = 30;

    public static List<Company> Found(GameWorld world)
    {
        var companies = new List<Company>();
        var next = 1;

        foreach (var country in world.Countries)
        {
            // Здания страны по отраслям: что есть, то и делим.
            var byRegion = new Dictionary<Sector, List<(int Region, BuildingType Type, int Count)>>();

            foreach (var region in world.RegionsOf(country.Id))
            {
                foreach (var (type, count) in region.BuildingsCount)
                {
                    var owned = region.BuildingsOf(type, country.Id);
                    if (owned <= 0) continue;

                    var sector = world.Buildings[type].Sector;
                    if (sector == Sector.People) continue;

                    if (!byRegion.TryGetValue(sector, out var list)) byRegion[sector] = list = [];

                    list.Add((region.Id, type, owned));
                }
            }

            foreach (var (sector, holdings) in byRegion)
            {
                var made = Share(world, country, sector, holdings, ref next);
                companies.AddRange(made);
            }
        }

        Widen(companies);

        return companies;
    }

    /// <summary>Режет отрасль между несколькими компаниями: первой больше всех.</summary>
    private static List<Company> Share(
        GameWorld world,
        Country country,
        Sector sector,
        List<(int Region, BuildingType Type, int Count)> holdings,
        ref int next)
    {
        var made = new List<Company>(PerSector);
        for (var i = 0; i < PerSector; i++)
        {
            made.Add(new Company(next++, country.Id, $"{country.Iso}-{Names.Of(sector)}-{i + 1}", [sector]));
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

    /// <summary>Каждая седьмая берётся за соседнюю отрасль.</summary>
    private static void Widen(List<Company> companies)
    {
        for (var i = WideEvery - 1; i < companies.Count; i += WideEvery)
        {
            var company = companies[i];
            var second = Next(company.Focus[0]);

            companies[i] = new Company(
                company.Id, company.Country, company.Name, [company.Focus[0], second], company.Cash);

            foreach (var ((region, type), count) in company.Buildings)
            {
                companies[i].Add(region, type, count);
            }
        }
    }

    /// <summary>Соседняя отрасль по переделу: добыча тянется в тяжёлую, тяжёлая в
    /// гражданскую. Так и растут конгломераты — вверх по цепочке, а не куда попало.</summary>
    private static Sector Next(Sector sector) => sector switch
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

    /// <summary>Короткое имя отрасли для названия компании.</summary>
    private static class Names
    {
        public static string Of(Sector sector) => sector switch
        {
            Sector.Mining => "Добыча",
            Sector.Power => "Энергия",
            Sector.Heavy => "Тяжпром",
            Sector.Civil => "Гражданпром",
            Sector.Military => "Оборонпром",
            Sector.Services => "Услуги",
            _ => "Прочее",
        };
    }
}
