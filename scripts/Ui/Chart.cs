using Godot;

/// <summary>Один ряд на графике.</summary>
public readonly record struct Trace(string Name, Color Colour, IReadOnlyList<float> Points);

/// <summary>График по дням: заголовок, поле и шкала слева. Без чисел на шкале линия
/// показывает форму, но не величину.</summary>
public partial class Chart : VBoxContainer
{
    private const int PlotHeight = 84;

    /// <summary>Место под подпись ряда. Задаётся, иначе при смене числа соседняя подпись
    /// уезжает вбок вместе с ним.</summary>
    private const int LegendWidth = 132;

    /// <summary>Больше стольких делений на шкале не рисуем. Точного числа нет: шаг
    /// берётся круглый, а делений выходит сколько выйдет — три или четыре. Поле высотой
    /// в восемьдесят точек, и на шести подписи уже слипаются.</summary>
    private const int MostDivisions = 4;

    private Label _title = null!;
    private HBoxContainer _legend = null!;
    private Plot _plot = null!;

    private Func<double, string> _show = value => $"{value:0.##}";

    private static readonly (string Label, int Days)[] Windows =
    [
        ("нед", 7),
        ("мес", 30),
        ("год", 365),
        ("всё", 0),
    ];

    private readonly List<Button> _windows = [];
    private HBoxContainer _picker = null!;
    private Trace[] _shown = [];
    private int _window = 365;

    public static Chart Create(string title, Func<double, string>? show = null)
    {
        var chart = new Chart { MouseFilter = MouseFilterEnum.Ignore };
        chart.AddThemeConstantOverride("separation", 2);

        if (show is not null) chart._show = show;

        var head = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        head.AddThemeConstantOverride("separation", 8);

        chart._title = Ui.Text(title, 13, 600, Skin.Text);
        head.AddChild(chart._title);
        head.AddChild(Ui.Spring());

        chart._legend = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        chart._legend.AddThemeConstantOverride("separation", 10);
        head.AddChild(chart._legend);
        chart.AddChild(head);

        chart._picker = new HBoxContainer();
        chart._picker.AddThemeConstantOverride("separation", 4);
        chart.AddChild(chart._picker);

        chart._plot = new Plot { CustomMinimumSize = new Vector2(0, PlotHeight), Show = chart._show };
        chart.AddChild(chart._plot);

        return chart;
    }

    /// <summary>Добавляет переключатель отрезка: неделя, месяц, год, всё время.</summary>
    /// <remarks>Отдельная кнопка «день» не нужна: точка на ряду и есть день, из одной
    /// точки графика не выйдет.</remarks>
    public Chart Ranged()
    {
        foreach (var (label, days) in Windows)
        {
            var which = days;
            var button = Ui.Act(label, Skin.Link, () => Cut(which), 44);

            button.AddThemeFontSizeOverride("font_size", 12);
            _windows.Add(button);
            _picker.AddChild(button);
        }

        Cut(_window);

        return this;
    }

    private void Cut(int days)
    {
        _window = days;

        for (var index = 0; index < _windows.Count; index++)
        {
            Ui.Paint(_windows[index], Windows[index].Days == days ? Skin.Bright : Skin.Link);
        }

        if (_shown.Length > 0) Show(_shown);
    }

    /// <summary>Хвост ряда длиной в окно. Ноль — весь ряд.</summary>
    private IReadOnlyList<float> Tail(IReadOnlyList<float> points)
    {
        if (_window <= 0 || points.Count <= _window) return points;

        var tail = new float[_window];
        for (var at = 0; at < _window; at++) tail[at] = points[points.Count - _window + at];

        return tail;
    }

    /// <summary>Обновляет данные. Ряды делят одну шкалу, поэтому в один график кладут
    /// только сравнимое: вывоз с ввозом, но не ВВП с инфляцией.</summary>
    public void Show(params Trace[] traces)
    {
        _shown = traces;
        traces = traces.Select(trace => trace with { Points = Tail(trace.Points) }).ToArray();

        _plot.Traces = traces;

        var low = float.MaxValue;
        var high = float.MinValue;

        foreach (var trace in traces)
        {
            foreach (var point in trace.Points)
            {
                low = Mathf.Min(low, point);
                high = Mathf.Max(high, point);
            }
        }

        if (low > high) (low, high) = (0f, 1f);

        // Ноль подтягиваем в шкалу, только если ряд его пересекает: иначе ВВП в паре
        // триллионов превращается в прямую у верхнего края.
        if (low > 0 && high / Mathf.Max(low, 0.0001f) > 6) low = 0;
        if (high < 0) high = 0;

        // Ровная линия: раздвигаем шкалу вокруг неё, иначе делить будет нечего.
        if (high - low < Mathf.Abs(high) * 0.001f) high = low + Mathf.Max(Mathf.Abs(low) * 0.1f, 1f);

        var step = Step((high - low) / MostDivisions);

        _plot.Step = step;
        _plot.Low = Mathf.Floor(low / step) * step;
        _plot.High = Mathf.Max(Mathf.Ceil(high / step) * step, _plot.Low + step);
        _plot.QueueRedraw();

        Legend(traces);
    }

    /// <summary>Круглый шаг шкалы не меньше заданного: 1, 2, 2.5 или 5 на своём порядке.
    /// Иначе подписи выходят вроде «947.98B$» — число верное, а прочесть нельзя.</summary>
    private static float Step(float raw)
    {
        if (raw <= 0) return 1;

        var order = Mathf.Pow(10, Mathf.Floor(Mathf.Log(raw) / Mathf.Log(10)));
        var times = raw / order;

        return (float)(order * (times <= 1 ? 1 : times <= 2 ? 2 : times <= 2.5 ? 2.5 : times <= 5 ? 5 : 10));
    }

    private void Legend(Trace[] traces)
    {
        Ui.Trim(_legend, traces.Length);

        for (var index = 0; index < traces.Length; index++)
        {
            if (index >= _legend.GetChildCount())
            {
                var fresh = Ui.Text(string.Empty, 12, 600);

                fresh.CustomMinimumSize = new Vector2(LegendWidth, 0);
                fresh.HorizontalAlignment = HorizontalAlignment.Right;
                fresh.ClipText = true;
                _legend.AddChild(fresh);
            }

            var label = _legend.GetChild<Label>(index);
            var points = traces[index].Points;
            var last = points.Count > 0 ? points[^1] : 0;

            label.Text = $"{traces[index].Name} {_show(last)}";
            label.AddThemeColorOverride("font_color", traces[index].Colour);
        }
    }

    /// <summary>Само поле. Рисуется вручную: узлов на тысячу точек не напасёшься.</summary>
    private partial class Plot : Control
    {
        /// <summary>Полоса слева под подписи шкалы. Линия по ней не идёт: цифра поверх
        /// графика читается плохо, а без цифр не видно величины.</summary>
        private const int AxisWidth = 52;

        public Trace[] Traces = [];
        public float Low;
        public float High = 1;
        public float Step = 1;
        public Func<double, string> Show = value => $"{value:0.##}";

        /// <summary>Шрифт подписей один на все графики: создавать его в каждой
        /// перерисовке — это новый объект шестьдесят раз в секунду.</summary>
        private static readonly FontVariation Face = Skin.Weight(400);

        private float Field => Mathf.Max(Size.X - AxisWidth, 2);

        public override void _Draw()
        {
            var box = new Rect2(AxisWidth, 0, Field, Size.Y);

            DrawRect(box, new Color(Skin.Ink, 0.5f));
            DrawRect(box, new Color(Skin.Soft, 0.9f), filled: false, width: 1);

            for (var value = Low; value <= High + Step / 2; value += Step)
            {
                var y = Y(value);

                if (y > 1 && y < Size.Y - 1)
                {
                    DrawLine(new Vector2(AxisWidth, y), new Vector2(Size.X, y), new Color(Skin.Soft, 0.55f));
                }

                // Крайние подписи вжимаются внутрь: у самой границы буквы обрезались бы.
                var baseline = Mathf.Clamp(y + 3.5f, 9f, Size.Y - 1f);

                DrawString(Face, new Vector2(0, baseline), Show(value), HorizontalAlignment.Right,
                    AxisWidth - 6, 10, new Color(Skin.Dim, 0.95f));
            }

            foreach (var trace in Traces) Draw(trace);
        }

        private void Draw(Trace trace)
        {
            if (trace.Points.Count < 2 || Field < 2) return;

            var width = (int)Field;
            var points = new Vector2[width];

            // По точке на пиксель: за пять лет их под две тысячи, а полоса шириной
            // четыреста — рисовать каждую незачем.
            for (var x = 0; x < width; x++)
            {
                var at = trace.Points.Count == 1
                    ? 0
                    : x * (trace.Points.Count - 1) / (width - 1);

                points[x] = new Vector2(AxisWidth + x, Y(trace.Points[at]));
            }

            var under = new Vector2[width + 2];
            points.CopyTo(under, 0);
            under[width] = new Vector2(AxisWidth + width - 1, Size.Y);
            under[width + 1] = new Vector2(AxisWidth, Size.Y);

            // Заливка под линией гаснет книзу: ровная плашка цвета спорит с самой линией.
            var shades = new Color[width + 2];
            for (var at = 0; at < width; at++) shades[at] = new Color(trace.Colour, 0.26f);

            shades[width] = new Color(trace.Colour, 0.02f);
            shades[width + 1] = new Color(trace.Colour, 0.02f);

            DrawPolygon(under, shades);
            DrawPolyline(points, trace.Colour, 1.6f, antialiased: true);
        }

        private float Y(float value) =>
            Size.Y - (value - Low) / Mathf.Max(High - Low, 0.0001f) * Size.Y;
    }
}
