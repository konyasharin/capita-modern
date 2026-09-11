using CapitaModern.Core.Economy;
using Godot;

/// <summary>Подсказки-графики по показателям страны и по товарам: одно определение на
/// показатель.</summary>
/// <remarks>Один и тот же показатель висит и в верхней панели, и в боковой, и подпись с
/// цветом у него должны быть те же самые.</remarks>
public static class Trends
{
    public static Func<(string Key, Control Body)?> People(History past) => One(
        "population", "Население за год", Fmt.Count, "людей", Skin.People, past, History.Line.People);

    public static Func<(string Key, Control Body)?> Workers(History past) => One(
        "workers", "Рабочая сила за год", Fmt.Count, "рук", Skin.Labour, past, History.Line.Workers);

    public static Func<(string Key, Control Body)?> Employment(History past) => One(
        "employment", "Занятость за год", value => Fmt.Percent(value), "занятость", Skin.Labour,
        past, History.Line.Employment);

    public static Func<(string Key, Control Body)?> Load(History past) => One(
        "load", "Загрузка за год", value => Fmt.Percent(value), "загрузка", Skin.Plants,
        past, History.Line.Load);

    public static Func<(string Key, Control Body)?> Output(History past) => One(
        "output", "ВВП за год", Fmt.Cash, "ВВП", Skin.Output, past, History.Line.Gdp);

    public static Func<(string Key, Control Body)?> Plants(History past) => One(
        "plants", "Предприятия за год", Fmt.Count, "предприятий", Skin.Plants, past, History.Line.Plants);

    public static Func<(string Key, Control Body)?> Treasury(History past) => One(
        "treasury", "Казна за год", Fmt.Cash, "казна", Skin.Money, past, History.Line.Treasury);

    public static Func<(string Key, Control Body)?> Debt(History past) => One(
        "debt", "Внешний долг за год", Fmt.Cash, "долг", Skin.Owed, past, History.Line.Debt);

    public static Func<(string Key, Control Body)?> Supply(History past) => One(
        "supply", "Денежная масса за год", Fmt.Cash, "масса", Skin.Money, past, History.Line.Supply);

    public static Func<(string Key, Control Body)?> Rate(History past) => One(
        "rate", "Курс валюты за год", value => $"×{value:0.00}", "курс", Skin.Rate, past, History.Line.Rate);

    /// <summary>Инфляция берётся понедельная: годовой разгон размазан по всему году и на
    /// графике его не видно.</summary>
    public static Func<(string Key, Control Body)?> Inflation(History past) => () => TrendCard.Of(
        "inflation", "Инфляция по неделям", value => Fmt.Percent(value, signed: true),
        new Trace("за неделю", Skin.Prices, past.WeeklyLine()));

    /// <summary>Цена товара за год.</summary>
    public static Func<(string Key, Control Body)?> Price(History past, Func<GoodType> good) => () =>
        TrendCard.Of($"price:{good()}", $"{Names.Of(good())}: цена за год", Fmt.Cash,
            new Trace("цена", Names.ColourOf(good()).Lightened(0.2f), past.PricesOf(good(), History.Year)));

    /// <summary>Выпуск и заказ товара за год одним графиком: врозь по ним не видно, идёт
    /// ли склад в плюс или в минус.</summary>
    public static Func<(string Key, Control Body)?> Flow(History past, Func<GoodType> good) => () =>
        TrendCard.Of($"flow:{good()}", $"{Names.Of(good())}: выпуск и заказ за год", Fmt.Count,
            new Trace("выпуск", Skin.Output, past.OutputOf(good(), History.Year)),
            new Trace("заказ", Skin.Prices, past.WantsOf(good(), History.Year)));

    /// <summary>Наша цена к мировой за год.</summary>
    public static Func<(string Key, Control Body)?> ToWorld(History past, Func<GoodType> good) => () =>
        TrendCard.Of($"worldprice:{good()}", $"{Names.Of(good())}: наша цена к мировой",
            value => $"×{value:0.00}",
            new Trace("к миру", Names.ColourOf(good()).Lightened(0.2f), past.ToWorldOf(good())));

    private static Func<(string Key, Control Body)?> One(
        string key,
        string title,
        Func<double, string> show,
        string name,
        Color colour,
        History past,
        History.Line line) =>
        () => TrendCard.Of(key, title, show, new Trace(name, colour, past.Of(line)));
}
