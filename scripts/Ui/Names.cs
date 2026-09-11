using System.Text;
using Godot;
using CapitaModern.Core.Buildings;
using CapitaModern.Core.Economy;
using CapitaModern.Core.Politics;

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
            _ => $"{sign}{size:0}",
        };
    }

    /// <summary>Во сколько долларов обходится одна денежная единица модели.</summary>
    /// <remarks>Внутри деньги считаются тысячами долларов: иначе мировой ВВП не влезает
    /// в Money с его сотыми долями. Наружу показываем доллары.</remarks>
    public const double Dollar = 1000;

    /// <summary>Деньги модели в подпись. На вход идут внутренние единицы, не доллары.</summary>
    public static string Cash(double units) => $"{Count(units * Dollar)}$";

    public static string Amount(GoodAmount amount) => Count(amount.Exact);

    /// <summary>Цена: у дешёвого сырья значащие цифры за запятой, у дорогого — нет.</summary>
    public static string Price(Money price)
    {
        var dollars = price.Exact * Dollar;

        return dollars switch
        {
            >= 100_000 => Cash(price.Exact),
            >= 100 => $"{dollars:0}$",
            >= 1 => $"{dollars:0.0}$",
            _ => $"{dollars:0.000}$",
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
