using CapitaModern.Core.Buildings;
using CapitaModern.Core.Economy;
using Godot;

/// <summary>Карточка товара для подсказки: кружок с цветом, четыре числа, цена за месяц
/// графиком и рецепт значками. Собирается на месте — в словаре такого не напишешь, всё
/// зависит от состояния игры.</summary>
public static class GoodCard
{
    private const int Wide = 330;

    /// <summary>Ключ и виджет. Ключ нужен подсказке, чтобы заметить смену товара.</summary>
    public static (string Key, Control Body) Of(GameLoop loop, History past, GoodType good)
    {
        var card = new VBoxContainer { CustomMinimumSize = new Vector2(Wide, 0) };
        card.AddThemeConstantOverride("separation", 7);

        card.AddChild(Head(loop, good));
        card.AddChild(Numbers(loop, good));
        card.AddChild(Price(loop, past, good));
        card.AddChild(Recipe(loop, good));

        return ($"good:{good}", card);
    }

    private static Control Head(GameLoop loop, GoodType good)
    {
        var head = new HBoxContainer();
        head.AddThemeConstantOverride("separation", 10);

        head.AddChild(Badge(good));

        var name = Ui.Text(Names.Of(good), 18, 700, Names.ColourOf(good).Lightened(0.35f));
        name.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        head.AddChild(name);
        head.AddChild(Ui.Spring());

        var world = loop.World.Market.Prices.Of(good);
        var times = world.Raw > 0 ? loop.PlayerCountry.State.Prices.Of(good).Exact / world.Exact : 0;

        head.AddChild(times > 0
            ? Ui.Chip($"×{times:0.00} к миру", Fmt.Sign(times - 1, moreIsBetter: false))
            : Ui.Chip("не возят", Skin.Dim));

        return head;
    }

    /// <summary>Тот же кружок, что и в сетке окна производства: товар узнают по нему.</summary>
    private static Control Badge(GoodType good)
    {
        const int size = 38;

        var holder = new Control
        {
            CustomMinimumSize = new Vector2(size, size),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };

        var paint = new ShaderMaterial { Shader = GD.Load<Shader>("res://scenes/ui/badge.gdshader") };
        paint.SetShaderParameter("tint", Names.ColourOf(good));

        var disc = new ColorRect
        {
            Material = paint,
            Size = new Vector2(size, size),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };

        holder.AddChild(disc);

        var icon = Ui.Icon(Names.IconOf(good), 22, Colors.White);
        icon.Material = Skin.Glyph(Names.ColourOf(good));
        icon.Position = new Vector2((size - 22) / 2f, (size - 22) / 2f);
        icon.Size = new Vector2(22, 22);
        holder.AddChild(icon);

        return holder;
    }

    private static Control Numbers(GameLoop loop, GoodType good)
    {
        var sim = loop.Simulation;
        var id = loop.Player;
        var state = loop.PlayerCountry.State;

        var grid = new GridContainer { Columns = 2 };
        grid.AddThemeConstantOverride("h_separation", 18);
        grid.AddThemeConstantOverride("v_separation", 2);

        Put(grid, "Цена", Fmt.Price(state.Prices.Of(good)), Skin.Money);
        Put(grid, "На складе", Fmt.Amount(state.Stock.Of(good)), Skin.Bright);
        Put(grid, "Выпуск за день", Fmt.Amount(sim.OutputOf(id, good)), Skin.Output);
        Put(grid, "Заказ за день", Fmt.Amount(sim.InputOf(id, good)), Skin.Text);

        var lack = sim.ShortOf(id, good);
        if (lack.Raw > 0) Put(grid, "Не хватило", Fmt.Amount(lack), Skin.Bad);

        return grid;
    }

    private static void Put(GridContainer grid, string label, string value, Color colour)
    {
        var row = new HBoxContainer { CustomMinimumSize = new Vector2((Wide - 18) / 2, 0) };
        row.AddThemeConstantOverride("separation", 6);

        row.AddChild(Ui.Text(label, 13, 400, Skin.Dim));
        row.AddChild(Ui.Spring());
        row.AddChild(Ui.Number(value, 13, colour));

        grid.AddChild(row);
    }

    /// <summary>Цена за последний месяц. По одному числу не видно, дорожает товар или нет.</summary>
    private static Control Price(GameLoop loop, History past, GoodType good)
    {
        var chart = Chart.Create("Цена за месяц", value => Fmt.Price(new Money((long)(value * 100))));

        chart.Show(new Trace("цена", Names.ColourOf(good).Lightened(0.2f), past.PricesOf(good)));

        return chart;
    }

    /// <summary>Рецепт значками: что съедает завод и что отдаёт. Берётся первый завод,
    /// который этот товар делает; если товар только едят — показывается, кто.</summary>
    private static Control Recipe(GameLoop loop, GoodType good)
    {
        var maker = Enum.GetValues<BuildingType>()
            .FirstOrDefault(type => loop.World.Buildings[type].Outputs.ContainsKey(good));

        var info = loop.World.Buildings[maker];
        var makes = info.Outputs.ContainsKey(good);

        var block = new VBoxContainer();
        block.AddThemeConstantOverride("separation", 3);

        block.AddChild(Ui.Text(
            makes ? $"Делает {Names.Of(maker)}" : "Никто не делает",
            12, 600, Skin.Dim));

        if (!makes) return block;

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 6);
        block.AddChild(row);

        foreach (var (input, _) in info.Inputs.OrderBy(pair => (int)pair.Key))
        {
            row.AddChild(Ui.Icon(Names.IconOf(input), 20, Names.ColourOf(input)));
        }

        if (info.Inputs.Count == 0) row.AddChild(Ui.Text("из земли", 13, 400, Skin.Dim));

        row.AddChild(Ui.Text("→", 15, 700, Skin.Link));
        row.AddChild(Ui.Icon(Names.IconOf(good), 20, Names.ColourOf(good)));
        row.AddChild(Ui.Spring());

        var eaters = Enum.GetValues<BuildingType>()
            .Count(type => loop.World.Buildings[type].Inputs.ContainsKey(good));

        if (eaters > 0) row.AddChild(Ui.Text($"идёт в {eaters} произв.", 12, 400, Skin.Dim));

        return block;
    }
}
