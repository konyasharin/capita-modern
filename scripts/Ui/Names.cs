using System.Text;
using Godot;
using CapitaModern.Core.Buildings;
using CapitaModern.Core.Economy;
using CapitaModern.Core.Politics;
using Country = CapitaModern.Core.World.Country;

/// <summary>Русские названия для того, что в коде названо по-английски, и пути к значкам.
/// Держатся в C#, а не в данных: компилятор так проверяет, что ни один товар не забыт.</summary>
public static class Names
{
    public static string Of(GoodType good) => good switch
    {
        GoodType.Coal => "Уголь",
        GoodType.Oil => "Нефть",
        GoodType.Gas => "Газ",
        GoodType.IronOre => "Железная руда",
        GoodType.CopperOre => "Медная руда",
        GoodType.Bauxite => "Бокситы",
        GoodType.Uranium => "Уран",
        GoodType.RareEarth => "Редкозёмы",
        GoodType.Timber => "Лес",
        GoodType.Agriculture => "Сельхозсырьё",
        GoodType.Electricity => "Электричество",
        GoodType.Fuel => "Топливо",
        GoodType.Metals => "Металлы",
        GoodType.Chemicals => "Химия",
        GoodType.Materials => "Стройматериалы",
        GoodType.Components => "Комплектующие",
        GoodType.Electronics => "Электроника",
        GoodType.Microelectronics => "Микроэлектроника",
        GoodType.Food => "Еда",
        GoodType.ConsumerGoods => "Товары",
        GoodType.Medicine => "Лекарства",
        GoodType.Services => "Услуги",
        GoodType.Armour => "Бронетехника",
        GoodType.Artillery => "Артиллерия",
        GoodType.SmallArms => "Стрелковое",
        GoodType.Ammunition => "Боеприпасы",
        GoodType.TacticalDrones => "Тактические дроны",
        GoodType.StrikeDrones => "Ударные дроны",
        GoodType.Missiles => "Ракеты",
        GoodType.Aircraft => "Авиация",
        GoodType.AirDefence => "ПВО",
        GoodType.ElectronicWarfare => "РЭБ",
        _ => good.ToString(),
    };

    /// <summary>Свой цвет каждому товару: в сетке из тридцати двух плашек различают не
    /// значок, а пятно. Оттенки идут по переделам — сырьё землистое, переделы стальные,
    /// потребление тёплое, военное красное.</summary>
    public static Color ColourOf(GoodType good) => good switch
    {
        GoodType.Coal => new Color("6b6b78"),
        GoodType.Oil => new Color("5b4a6e"),
        GoodType.Gas => new Color("4f7fa8"),
        GoodType.IronOre => new Color("8a6a55"),
        GoodType.CopperOre => new Color("b4703c"),
        GoodType.Bauxite => new Color("a8887a"),
        GoodType.Uranium => new Color("74c46a"),
        GoodType.RareEarth => new Color("9a6fc4"),
        GoodType.Timber => new Color("6d8f4a"),
        GoodType.Agriculture => new Color("c2a63c"),

        GoodType.Electricity => new Color("e8d24a"),
        GoodType.Fuel => new Color("d1843c"),
        GoodType.Metals => new Color("9aa7b4"),
        GoodType.Chemicals => new Color("5fbfa8"),
        GoodType.Materials => new Color("b09a7e"),
        GoodType.Components => new Color("7f96c4"),
        GoodType.Electronics => new Color("4fb3d9"),
        GoodType.Microelectronics => new Color("6fd4e8"),

        GoodType.Food => new Color("d95f5f"),
        GoodType.ConsumerGoods => new Color("d97fb0"),
        GoodType.Medicine => new Color("e4e4ec"),
        GoodType.Services => new Color("8fb8e0"),

        GoodType.Armour => new Color("7a6a4a"),
        GoodType.Artillery => new Color("8f5a3c"),
        GoodType.SmallArms => new Color("a35c4a"),
        GoodType.Ammunition => new Color("c46a3c"),
        GoodType.TacticalDrones => new Color("6aa3a3"),
        GoodType.StrikeDrones => new Color("4a8f8f"),
        GoodType.Missiles => new Color("c44a4a"),
        GoodType.Aircraft => new Color("5f7fa8"),
        GoodType.AirDefence => new Color("7f6ac4"),
        GoodType.ElectronicWarfare => new Color("a35fc4"),

        _ => Skin.Dim,
    };

    public static string Of(BuildingType type) => type switch
    {
        BuildingType.CoalMine => "Угольная шахта",
        BuildingType.OilRig => "Нефтевышка",
        BuildingType.GasField => "Газовый промысел",
        BuildingType.IronMine => "Железный рудник",
        BuildingType.CopperMine => "Медный рудник",
        BuildingType.BauxiteMine => "Бокситовый карьер",
        BuildingType.UraniumMine => "Урановый рудник",
        BuildingType.RareEarthMine => "Рудник редкозёмов",
        BuildingType.LoggingCamp => "Лесозаготовка",
        BuildingType.Farm => "Хозяйство",
        BuildingType.CoalPlant => "Угольная ТЭС",
        BuildingType.GasPlant => "Газовая ТЭС",
        BuildingType.NuclearPlant => "АЭС",
        BuildingType.HydroPlant => "ГЭС",
        BuildingType.Refinery => "НПЗ",
        BuildingType.SteelMill => "Металлургический завод",
        BuildingType.Smelter => "Плавильня",
        BuildingType.ChemicalPlant => "Химзавод",
        BuildingType.MaterialsPlant => "Завод стройматериалов",
        BuildingType.ComponentsPlant => "Завод комплектующих",
        BuildingType.ElectronicsPlant => "Завод электроники",
        BuildingType.MicroelectronicsPlant => "Завод микроэлектроники",
        BuildingType.FoodPlant => "Пищевой завод",
        BuildingType.ConsumerGoodsPlant => "Завод товаров",
        BuildingType.PharmaPlant => "Фармзавод",
        BuildingType.ArmourPlant => "Танковый завод",
        BuildingType.ArtilleryPlant => "Артиллерийский завод",
        BuildingType.SmallArmsPlant => "Оружейный завод",
        BuildingType.AmmunitionPlant => "Патронный завод",
        BuildingType.TacticalDronePlant => "Завод тактических дронов",
        BuildingType.StrikeDronePlant => "Завод ударных дронов",
        BuildingType.MissilePlant => "Ракетный завод",
        BuildingType.AircraftPlant => "Авиазавод",
        BuildingType.AirDefencePlant => "Завод ПВО",
        BuildingType.ElectronicWarfarePlant => "Завод РЭБ",
        BuildingType.RetailFirm => "Торговля",
        BuildingType.TransportFirm => "Перевозки",
        BuildingType.PublicService => "Госуслуги",
        BuildingType.BusinessFirm => "Деловые услуги",
        _ => type.ToString(),
    };

    /// <summary>Русское название страны. В countries.json имена английские: они пришли
    /// из Natural Earth и служат ключом сверки с данными, поэтому перевод лежит рядом.</summary>
    public static string Of(Country country) => Of(country.Iso, country.Name);

    public static string Of(string iso, string fallback) => Countries.GetValueOrDefault(iso, fallback);

    /// <summary>Русское название области. Английские имена в regions.json — ключ сверки
    /// с нарезкой Natural Earth, поэтому перевод лежит рядом.</summary>
    public static string Region(string name) => Regions.GetValueOrDefault(name, name);

    private static Dictionary<string, string>? _countries;
    private static Dictionary<string, string>? _regions;

    private static Dictionary<string, string> Regions =>
        _regions ??= Read("res://data/ui/regions-ru.json");

    private static Dictionary<string, string> Countries =>
        _countries ??= Read("res://data/ui/countries-ru.json");

    private static Dictionary<string, string> Read(string path)
    {
        var json = Godot.FileAccess.GetFileAsString(path);
        using var doc = System.Text.Json.JsonDocument.Parse(json);

        var names = new Dictionary<string, string>();
        foreach (var item in doc.RootElement.GetProperty("names").EnumerateObject())
        {
            names[item.Name] = item.Value.GetString() ?? item.Name;
        }

        return names;
    }

    public static string Of(Sector sector) => sector switch
    {
        Sector.Mining => "Добыча",
        Sector.Power => "Энергетика",
        Sector.Heavy => "Тяжёлая",
        Sector.Civil => "Гражданская",
        Sector.Military => "Военная",
        Sector.Services => "Услуги",
        Sector.People => "Население",
        _ => sector.ToString(),
    };

    public static string Of(Bloc bloc) => bloc switch
    {
        Bloc.NonAligned => "Неприсоединившиеся",
        Bloc.West => "Запад",
        Bloc.China => "Китай",
        Bloc.Russia => "Россия",
        _ => bloc.ToString(),
    };

    public static Color ColourOf(Bloc bloc) => bloc switch
    {
        Bloc.West => Skin.People,
        Bloc.China => Skin.Bad,
        Bloc.Russia => Skin.Output,
        _ => Skin.Dim,
    };

    public static string Of(LoanSource source) => source switch
    {
        LoanSource.Foreign => "внешний",
        LoanSource.Domestic => "внутренний",
        LoanSource.Multilateral => "МВФ",
        _ => source.ToString(),
    };

    public static string IconOf(GoodType good) => $"res://assets/icons/goods/{Kebab(good.ToString())}.svg";

    public static string IconOf(BuildingType type) => $"res://assets/icons/buildings/{Kebab(type.ToString())}.svg";

    public static string Ui(string name) => $"res://assets/icons/ui/{name}.svg";

    /// <summary>IronOre в iron-ore: имена файлов значков делались из имён перечислений.</summary>
    private static string Kebab(string name)
    {
        var result = new StringBuilder(name.Length + 4);

        foreach (var symbol in name)
        {
            if (char.IsUpper(symbol) && result.Length > 0) result.Append('-');
            result.Append(char.ToLowerInvariant(symbol));
        }

        return result.ToString();
    }
}

/// <summary>Числа в подпись. Собрано в одном месте: одна и та же величина в панели и в
/// таблице должна выглядеть одинаково.</summary>
public static class Fmt
{
    /// <summary>Штуки и людей — тысячами и миллионами, иначе в колонку не влезет.</summary>
    public static string Count(double value)
    {
        var sign = value < 0 ? "-" : string.Empty;
        var size = System.Math.Abs(value);

        return size switch
        {
            >= 1e12 => $"{sign}{size / 1e12:0.##}T",
            >= 1e9 => $"{sign}{size / 1e9:0.##}B",
            >= 1e6 => $"{sign}{size / 1e6:0.##}M",
            >= 1e4 => $"{sign}{size / 1e3:0.#}K",
            >= 1e3 => $"{sign}{size / 1e3:0.##}K",
            >= 10 => $"{sign}{size:0}",
            // Мелкое округлять до нуля нельзя: выпуск одного завода за день — доли
            // единицы, и «0» читается как «не работает».
            > 0 => $"{sign}{size:0.###}",
            _ => "0",
        };
    }

    /// <summary>Во сколько долларов обходится одна денежная единица модели.</summary>
    /// <remarks>Внутри деньги считаются тысячами долларов: иначе мировой ВВП не влезает
    /// в Money с его сотыми долями. Наружу показываем доллары.</remarks>
    public const double Dollar = 1000;

    /// <summary>Знак валюты страны игрока. Ставится один раз при загрузке мира.</summary>
    /// <remarks>Деньги в модели у каждой страны свои, и подписывать их долларом нельзя:
    /// зарплата в России считается в рублях, а в Японии в иенах.</remarks>
    public static string Symbol { get; set; } = "$";

    /// <summary>Местные деньги в подпись. На вход идут внутренние единицы.</summary>
    public static string Cash(double units) => $"{Count(units * Dollar)}{Symbol}";

    /// <summary>Мировые деньги в подпись: резервы, внешний долг, внешняя торговля. Они
    /// считаются в долларах у всех стран, и знак у них всегда долларовый.</summary>
    public static string World(double units) => $"{Count(units * Dollar)}$";

    public static string Amount(GoodAmount amount) => Count(amount.Exact);

    /// <summary>Цена: у дешёвого сырья значащие цифры за запятой, у дорогого — нет.</summary>
    public static string Price(Money price)
    {
        var whole = price.Exact * Dollar;

        return whole switch
        {
            >= 100_000 => Cash(price.Exact),
            >= 100 => $"{whole:0}{Symbol}",
            >= 1 => $"{whole:0.0}{Symbol}",
            _ => $"{whole:0.000}{Symbol}",
        };
    }

    public static string Percent(double value, bool signed = false) =>
        signed ? $"{value:+0.0;-0.0;0.0}%" : $"{value:0.0}%";

    /// <summary>Ставка хранится в сотых долях процента.</summary>
    public static string Rate(int hundredths) => $"{hundredths / 100.0:0.00}%";

    /// <summary>Красим по знаку: рост дохода зелёный, рост цен красный. Что именно
    /// хорошо, решает вызывающий.</summary>
    public static Color Sign(double value, bool moreIsBetter = true) => value switch
    {
        > 0 => moreIsBetter ? Skin.Good : Skin.Bad,
        < 0 => moreIsBetter ? Skin.Bad : Skin.Good,
        _ => Skin.Text,
    };
}
