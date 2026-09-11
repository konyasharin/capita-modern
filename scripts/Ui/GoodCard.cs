using System.Text;
using CapitaModern.Core.Buildings;
using CapitaModern.Core.Economy;

/// <summary>Карточка товара для подсказки: цена, что с ним у нас сейчас и где он нужен.
/// Собирается на месте — в словаре такого не напишешь, всё зависит от состояния игры.</summary>
public static class GoodCard
{
    /// <summary>Сколько заводов перечислять поимённо, прежде чем писать «и ещё N».</summary>
    private const int Listed = 5;

    public static Article Of(GameLoop loop, GoodType good)
    {
        var sim = loop.Simulation;
        var id = loop.Player;
        var state = loop.PlayerCountry.State;
        var start = state.Prices.StartOf(good);
        var times = start.Raw > 0 ? state.Prices.Of(good).Exact / start.Exact : 1;

        var text = new StringBuilder();

        text.Append($"Цена [b]{Fmt.Price(state.Prices.Of(good))}[/b], к началу партии ×{times:0.00}.\n\n");
        text.Append($"У нас за день: выпуск [b]{Fmt.Amount(sim.OutputOf(id, good))}[/b], ");
        text.Append($"заказ [b]{Fmt.Amount(sim.InputOf(id, good))}[/b], ");
        text.Append($"на складе [b]{Fmt.Amount(state.Stock.Of(good))}[/b].");

        var lack = sim.ShortOf(id, good);
        if (lack.Raw > 0) text.Append($" Не хватило [b]{Fmt.Amount(lack)}[/b].");

        Line(loop, text, "Делают", info => info.Outputs.ContainsKey(good));
        Line(loop, text, "Идёт в", info => info.Inputs.ContainsKey(good));
        Line(loop, text, "Идёт в стройку", info => info.BuildCost.ContainsKey(good));

        return new Article(Names.Of(good), text.ToString());
    }

    private static void Line(GameLoop loop, StringBuilder text, string title, Func<BuildingInfo, bool> fits)
    {
        var found = Enum.GetValues<BuildingType>()
            .Where(type => fits(loop.World.Buildings[type]))
            .ToList();

        if (found.Count == 0) return;

        var names = found.Take(Listed).Select(Names.Of);
        var tail = found.Count > Listed ? $" и ещё {found.Count - Listed}" : string.Empty;

        text.Append($"\n\n[b]{title}:[/b] {string.Join(", ", names)}{tail}.");
    }
}
