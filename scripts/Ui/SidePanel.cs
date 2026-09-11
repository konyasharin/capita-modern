using CapitaModern.Core.World;
using Godot;

/// <summary>Общая рама боковой панели: шапка с названием, крестик и прокручиваемое тело.
/// Наследники наполняют тело и обновляют его.</summary>
public abstract partial class SidePanel : PanelContainer
{
    protected GameLoop Loop = null!;
    protected PopoverStack Stack = null!;

    /// <summary>Сюда наследник складывает строки, полоски и таблицы.</summary>
    protected VBoxContainer Rows = null!;

    /// <summary>Что с чем было по дням. Из неё панели рисуют графики.</summary>
    protected History Past = null!;

    protected Country Me => Loop.PlayerCountry;
    protected byte Id => Loop.Player;

    public abstract string Title { get; }

    /// <summary>Имя значка вкладки в assets/icons/ui.</summary>
    public abstract string Icon { get; }

    public Action? Closed;

    /// <summary>Ставится до добавления в дерево: наполнение зависит от мира.</summary>
    public void Setup(GameLoop loop, PopoverStack stack)
    {
        Loop = loop;
        Stack = stack;
    }

    public override void _Ready()
    {
        Past = GetNode<History>("/root/Game/History");

        CustomMinimumSize = new Vector2(Skin.PanelWidth, 0);
        AddThemeStyleboxOverride("panel", Skin.PanelBox());

        var frame = new VBoxContainer();
        frame.AddThemeConstantOverride("separation", 0);
        AddChild(frame);

        frame.AddChild(Header());

        var margin = new MarginContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        margin.AddThemeConstantOverride("margin_left", 12);
        margin.AddThemeConstantOverride("margin_right", 8);
        margin.AddThemeConstantOverride("margin_top", 8);
        margin.AddThemeConstantOverride("margin_bottom", 10);
        frame.AddChild(margin);

        var scroll = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };

        margin.AddChild(scroll);

        // Полоса прокрутки рисуется поверх содержимого, а не рядом с ним, поэтому место
        // ей отводится отступом изнутри — иначе она режет правую колонку чисел.
        var inner = new MarginContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        inner.AddThemeConstantOverride("margin_right", 12);
        scroll.AddChild(inner);

        Rows = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        Rows.AddThemeConstantOverride("separation", 4);
        inner.AddChild(Rows);

        Skin.Scrollbar(scroll.GetVScrollBar());

        Build();
        Refresh();
    }

    protected abstract void Build();

    public abstract void Refresh();

    /// <summary>Строка «название — число» сразу в теле панели.</summary>
    protected StatRow Stat(string label, string? key = null)
    {
        var row = StatRow.Create(label, Stack, key);
        Rows.AddChild(row);

        return row;
    }

    protected Bar Gauge(string label, Color colour, string? key = null)
    {
        var bar = Bar.Create(label, colour, Stack, key);
        Rows.AddChild(bar);

        return bar;
    }

    protected void Section(string title, string? icon = null) => Rows.AddChild(Ui.Section(title, icon));

    protected Chart Graph(string title, Func<double, string>? show = null)
    {
        var chart = Chart.Create(title, show);
        Rows.AddChild(chart);

        return chart;
    }

    protected Bars Columns()
    {
        var bars = Bars.Create();
        Rows.AddChild(bars);

        return bars;
    }

    /// <summary>Пояснение мелким шрифтом под разделом: где число врёт и почему.</summary>
    protected void Note(string text)
    {
        var note = Ui.Text(text, 12, 400, Skin.Dim);
        note.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        note.CustomMinimumSize = new Vector2(Skin.PanelWidth - 46, 0);

        Rows.AddChild(note);
    }

    private Control Header()
    {
        var head = new PanelContainer();
        head.AddThemeStyleboxOverride("panel", Skin.HeaderBox());

        var line = new HBoxContainer();
        line.AddThemeConstantOverride("separation", 8);
        head.AddChild(line);

        line.AddChild(Ui.Icon(Names.Ui($"tab-{Icon}"), 17, Skin.Link));
        line.AddChild(Ui.Text(Title, 17, 700, Skin.Bright));
        line.AddChild(Ui.Spring());

        var close = Ui.Act("✕", Skin.Dim, () => Closed?.Invoke());
        close.CustomMinimumSize = new Vector2(24, 0);
        line.AddChild(close);

        return head;
    }
}
