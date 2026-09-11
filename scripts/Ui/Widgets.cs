using Godot;

/// <summary>Мелкие кирпичи интерфейса. Панели собираются из них, а не из голых узлов
/// Godot: иначе оформление расползается по десяти файлам.</summary>
public static class Ui
{
    public static Label Text(string text, int size = 14, int weight = 400, Color? colour = null)
    {
        var label = new Label { Text = text, MouseFilter = Control.MouseFilterEnum.Ignore };

        label.AddThemeFontOverride("font", Skin.Weight(weight));
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", colour ?? Skin.Text);

        return label;
    }

    /// <summary>Число: моноширинными цифрами и с заданным местом, чтобы соседи не ездили.</summary>
    public static Label Number(string text, int size = 14, Color? colour = null, int width = 0)
    {
        var label = Text(text, size, 600, colour ?? Skin.Bright);

        label.AddThemeFontOverride("font", Skin.Digits());
        label.HorizontalAlignment = HorizontalAlignment.Right;
        if (width > 0) label.CustomMinimumSize = new Vector2(width, 0);

        return label;
    }

    public static TextureRect Icon(string path, int size, Color tint) => new()
    {
        Texture = GD.Load<Texture2D>(path),
        // Значки лежат квадратами по 512: без IgnoreSize они растягивают строку собой.
        ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
        StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
        CustomMinimumSize = new Vector2(size, size),
        SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        Modulate = tint,
        MouseFilter = Control.MouseFilterEnum.Ignore,
    };

    /// <summary>Распорка: всё, что после неё, уезжает к правому краю.</summary>
    public static Control Spring() => new()
    {
        SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        MouseFilter = Control.MouseFilterEnum.Ignore,
    };

    public static Control Gap(int height) => new()
    {
        CustomMinimumSize = new Vector2(0, height),
        MouseFilter = Control.MouseFilterEnum.Ignore,
    };

    /// <summary>Заголовок раздела внутри панели. Со значком: по нему раздел находят
    /// прокруткой, не вчитываясь в подписи.</summary>
    public static Control Section(string title, string? icon = null)
    {
        var rows = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        rows.AddThemeConstantOverride("separation", 3);
        rows.AddChild(Gap(6));

        var line = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        line.AddThemeConstantOverride("separation", 6);
        if (icon is not null) line.AddChild(Icon(Names.Ui(icon), 13, Skin.Link));

        line.AddChild(Text(title.ToUpperInvariant(), 12, 700, Skin.Dim));
        rows.AddChild(line);

        var rule = new HSeparator();
        rule.AddThemeStyleboxOverride("separator", Skin.BarBox(Skin.Soft));
        rows.AddChild(rule);

        return rows;
    }

    /// <summary>Кнопка действия. Цвет говорит о последствиях: связь — обычное,
    /// красное — необратимое.</summary>
    /// <param name="width">Ширина кнопки. Обязательна там, где подпись меняется: иначе
    /// кнопка растёт вместе с текстом и весь ряд рядом с ней прыгает.</param>
    public static Button Act(string text, Color colour, Action? pressed, int width = 0)
    {
        var button = new Button
        {
            Text = text,
            FocusMode = Control.FocusModeEnum.None,
            MouseFilter = Control.MouseFilterEnum.Stop,
            ClipText = width > 0,
            CustomMinimumSize = new Vector2(width, 0),
        };

        button.AddThemeFontOverride("font", Skin.Weight(600));
        button.AddThemeFontSizeOverride("font_size", 14);

        // Заливка живёт отдельным прямоугольником за кнопкой: у StyleBoxFlat нет
        // градиента, а ровный цвет выглядит куском пластика.
        var fill = new ColorRect
        {
            Name = "Fill",
            ShowBehindParent = true,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Material = Skin.Fill(colour),
        };

        fill.AnchorRight = 1;
        fill.AnchorBottom = 1;
        button.AddChild(fill);

        Paint(button, colour);

        button.MouseEntered += () => Glow(button, true);
        button.MouseExited += () => Glow(button, false);

        if (pressed is not null) button.Pressed += pressed;

        return button;
    }

    /// <summary>Под курсором заливка светлеет. Рамкой это уже показано, но одной рамки
    /// на широкой кнопке мало.</summary>
    private static void Glow(Button button, bool hover)
    {
        if (button.GetNodeOrNull<ColorRect>("Fill") is not { Material: ShaderMaterial paint }) return;

        var colour = (Color)button.GetMeta("tint");
        paint.SetShaderParameter("tint", new Color(colour, hover ? 0.58f : 0.34f));
    }

    public static void Paint(Button button, Color colour)
    {
        button.SetMeta("tint", colour);
        Glow(button, false);

        button.AddThemeStyleboxOverride("normal", Skin.ButtonBox(colour, false));
        button.AddThemeStyleboxOverride("hover", Skin.ButtonBox(colour, true));
        button.AddThemeStyleboxOverride("pressed", Skin.ButtonBox(colour, true));
        button.AddThemeStyleboxOverride("disabled", Skin.ButtonBox(Skin.Dim, false));
        button.AddThemeColorOverride("font_color", colour);
        button.AddThemeColorOverride("font_hover_color", Skin.Bright);
        button.AddThemeColorOverride("font_pressed_color", Skin.Bright);
        button.AddThemeColorOverride("font_disabled_color", Skin.Dim);
    }

    /// <summary>Ползунок доли от нуля до единицы. Ползунок, а не ступеньки: сумму
    /// вложения задают на глаз, точное число тут никому не нужно.</summary>
    public static HSlider Rail(Action<double> share)
    {
        var slider = new HSlider
        {
            MinValue = 0,
            MaxValue = 1,
            Step = 0.01,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            CustomMinimumSize = new Vector2(160, 20),
            FocusMode = Control.FocusModeEnum.None,
        };

        slider.AddThemeStyleboxOverride("slider", Skin.BarBox(new Color(Skin.Soft, 0.7f)));
        slider.AddThemeStyleboxOverride("grabber_area", Skin.BarBox(Skin.Link));
        slider.AddThemeStyleboxOverride("grabber_area_highlight", Skin.BarBox(Skin.Bright));
        slider.AddThemeIconOverride("grabber", Grabber());
        slider.AddThemeIconOverride("grabber_highlight", Grabber());
        slider.ValueChanged += value => share(value);

        return slider;
    }

    private static ImageTexture? _grabber;

    /// <summary>Кругляш ползунка. Рисуется в памяти: заводить ради него файл не стоит.</summary>
    private static ImageTexture Grabber()
    {
        if (_grabber is not null) return _grabber;

        const int size = 14;
        var image = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        var middle = (size - 1) / 2f;

        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var away = new Vector2(x - middle, y - middle).Length();
                var inside = Mathf.Clamp(middle - away, 0f, 1f);
                var colour = away > middle - 2 ? Skin.Bright : Skin.Link;

                image.SetPixel(x, y, new Color(colour, inside));
            }
        }

        _grabber = ImageTexture.CreateFromImage(image);

        return _grabber;
    }

    /// <summary>Оставить у узла не больше заданного числа детей.</summary>
    /// <remarks>Лишние сначала вынимаются из дерева и только потом освобождаются:
    /// QueueFree удаляет в конце кадра, и цикл «пока детей больше нужного» на нём
    /// крутился бы вечно — кадр не кончается, и видеодрайвер считает это отказом.</remarks>
    public static void Trim(Node parent, int keep)
    {
        for (var index = parent.GetChildCount() - 1; index >= keep; index--)
        {
            var child = parent.GetChild(index);

            parent.RemoveChild(child);
            child.QueueFree();
        }
    }

    /// <summary>Во сколько раз усилить действие. Ctrl — вдесятеро, Shift — всотеро.</summary>
    /// <remarks>Ставку от нуля до тридцати процентов шагом в четверть пункта иначе крутить
    /// сто двадцать раз.</remarks>
    public static int Louder()
    {
        if (Input.IsKeyPressed(Key.Shift)) return 100;

        return Input.IsKeyPressed(Key.Ctrl) ? 10 : 1;
    }

    /// <summary>Плашка-метка: блок страны, вид ставки, состояние товара.</summary>
    public static PanelContainer Chip(string text, Color colour)
    {
        var chip = new PanelContainer { SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        chip.AddThemeStyleboxOverride("panel", Skin.ChipBox(colour));
        chip.AddChild(Text(text, 12, 600, colour));

        return chip;
    }

    /// <summary>Подсказка по наведению. Проверяется прямоугольником каждый кадр: сигналы
    /// входа-выхода врут на щелях между соседними элементами.</summary>
    public static T Hover<T>(
        this T node,
        PopoverStack stack,
        string key,
        Func<Article?>? about = null,
        Func<(string Key, Control Body)?>? card = null)
        where T : Control
    {
        node.MouseFilter = Control.MouseFilterEnum.Stop;
        node.AddChild(new HoverProbe { Stack = stack, Key = key, About = about, Card = card });

        return node;
    }
}

/// <summary>Следит за курсором над своим родителем и открывает подсказку.</summary>
public partial class HoverProbe : Node
{
    public PopoverStack Stack = null!;
    public string Key = string.Empty;

    /// <summary>Что дописать к статье. Считается на месте: подробность зависит от того,
    /// что в игре происходит прямо сейчас.</summary>
    public Func<string?>? Extra;

    /// <summary>Готовая статья вместо словарной. Так подсказывают о товарах: статью на
    /// каждый из тридцати двух в словаре не напишешь, она зависит от хода партии.</summary>
    public Func<Article?>? About;

    /// <summary>Собранный виджет вместо текста. Ключ нужен, чтобы подсказка заметила, что
    /// мышь переехала на другой товар, и пересобралась.</summary>
    public Func<(string Key, Control Body)?>? Card;

    public override void _Process(double delta)
    {
        var owner = GetParent<Control>();
        if (!owner.IsVisibleInTree()) return;
        if (!owner.GetGlobalRect().HasPoint(owner.GetGlobalMousePosition())) return;

        if (Card?.Invoke() is { } card)
        {
            Stack.Open(owner, card.Key, 0, card: () => card.Body);
            return;
        }

        var ready = About?.Invoke();

        Stack.Open(owner, ready is null ? Key : $"{Key}:{ready.Title}", 0, Extra?.Invoke(), ready);
    }
}

/// <summary>Строка «название — число». Основная единица всех панелей.</summary>
public partial class StatRow : HBoxContainer
{
    private Label _value = null!;

    public static StatRow Create(
        string label,
        PopoverStack? stack = null,
        string? key = null,
        Func<(string Key, Control Body)?>? card = null)
    {
        var row = new StatRow { MouseFilter = MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", 6);
        row.AddChild(Ui.Text(label, 14, 400, Skin.Text));
        row.AddChild(Ui.Spring());

        row._value = Ui.Number("—", 14);
        row.AddChild(row._value);

        if (stack is not null && key is not null) row.Hover(stack, key, card: card);

        return row;
    }

    public void Set(string text, Color? colour = null)
    {
        _value.Text = text;
        _value.AddThemeColorOverride("font_color", colour ?? Skin.Bright);
    }
}

/// <summary>Полоска заполнения: занятость, загрузка, доля. Число рядом, потому что по
/// одной полоске точное значение не прочитать.</summary>
public partial class Bar : VBoxContainer
{
    private Label _value = null!;
    private ColorRect _fill = null!;
    private Color _colour;

    public static Bar Create(
        string label,
        Color colour,
        PopoverStack? stack = null,
        string? key = null,
        Func<(string Key, Control Body)?>? card = null)
    {
        var bar = new Bar { MouseFilter = MouseFilterEnum.Ignore, _colour = colour };
        bar.AddThemeConstantOverride("separation", 3);

        var top = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        top.AddChild(Ui.Text(label, 14, 400, Skin.Text));
        top.AddChild(Ui.Spring());

        bar._value = Ui.Number("—", 14, colour);
        top.AddChild(bar._value);
        bar.AddChild(top);

        // Жёлоб — обычный Control, а не контейнер: только так заполнение можно тянуть
        // якорем, контейнер растянул бы его на всю ширину.
        var groove = new Control
        {
            CustomMinimumSize = new Vector2(0, 5),
            MouseFilter = MouseFilterEnum.Ignore,
        };

        var back = new ColorRect { Color = new Color(colour, 0.15f), MouseFilter = MouseFilterEnum.Ignore };
        back.SetAnchorsPreset(LayoutPreset.FullRect);
        groove.AddChild(back);

        bar._fill = new ColorRect { Color = colour, MouseFilter = MouseFilterEnum.Ignore };
        bar._fill.Material = Skin.Fill(colour);
        bar._fill.SetAnchorsPreset(LayoutPreset.FullRect);
        bar._fill.AnchorRight = 0f;
        groove.AddChild(bar._fill);
        bar.AddChild(groove);

        if (stack is not null && key is not null) bar.Hover(stack, key, card: card);

        return bar;
    }

    /// <param name="share">Доля от нуля до единицы. Больше единицы прижимается к краю:
    /// полоска показывает заполнение, а перебор виден по числу.</param>
    public void Set(double share, string text, Color? colour = null)
    {
        _value.Text = text;
        _value.AddThemeColorOverride("font_color", colour ?? _colour);

        _fill.Color = colour ?? _colour;
        ((ShaderMaterial)_fill.Material).SetShaderParameter("tint", _fill.Color);
        _fill.AnchorRight = (float)Mathf.Clamp(share, 0.0, 1.0);
    }
}

/// <summary>Рычаг со ступенькой: «−  название  значение  +». Ступеньками, а не ползунком —
/// ставку задают точным числом, а не примерным местом на жёлобе.</summary>
public partial class Stepper : HBoxContainer
{
    private Label _value = null!;
    private Func<int> _read = null!;
    private Action<int> _write = null!;
    private Func<int, string> _show = null!;
    private int _step;
    private int _min;
    private int _max;

    public static Stepper Create(
        string label,
        int min,
        int max,
        int step,
        Func<int> read,
        Action<int> write,
        Func<int, string> show,
        PopoverStack? stack = null,
        string? key = null)
    {
        var knob = new Stepper
        {
            _read = read,
            _write = write,
            _show = show,
            _step = step,
            _min = min,
            _max = max,
        };

        knob.AddThemeConstantOverride("separation", 6);
        knob.AddChild(knob.Arrow("−", -1));
        knob.AddChild(Ui.Text(label, 14, 400, Skin.Text));
        knob.AddChild(Ui.Spring());

        knob._value = Ui.Number(show(read()), 14, Skin.Bright, 56);
        knob.AddChild(knob._value);
        knob.AddChild(knob.Arrow("+", 1));

        if (stack is not null && key is not null) knob.Hover(stack, key);

        return knob;
    }

    public void Refresh() => _value.Text = _show(_read());

    private Button Arrow(string text, int direction)
    {
        var button = Ui.Act(text, Skin.Link, () =>
        {
            _write(Mathf.Clamp(_read() + direction * _step * Ui.Louder(), _min, _max));
            Refresh();
        });

        button.CustomMinimumSize = new Vector2(24, 0);

        return button;
    }
}

/// <summary>Переключатель «включено — выключено» строкой.</summary>
public partial class Switch : HBoxContainer
{
    public static Switch Create(string label, Func<bool> read, Action<bool> write, Color colour)
    {
        var row = new Switch();
        row.AddThemeConstantOverride("separation", 6);
        row.AddChild(Ui.Text(label, 14, 400, Skin.Text));
        row.AddChild(Ui.Spring());

        var button = Ui.Act(read() ? "да" : "нет", colour, null);
        button.CustomMinimumSize = new Vector2(46, 0);
        button.Pressed += () =>
        {
            write(!read());
            Paint(button, read(), colour);
        };

        Paint(button, read(), colour);
        row.AddChild(button);

        return row;
    }

    private static void Paint(Button button, bool on, Color colour)
    {
        button.Text = on ? "да" : "нет";
        Ui.Paint(button, on ? colour : Skin.Dim);
    }
}
