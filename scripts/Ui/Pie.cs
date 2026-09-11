using Godot;

/// <summary>Долька круговой диаграммы.</summary>
public readonly record struct Wedge(string Name, double Share, string Text, Color Colour);

/// <summary>Круговая диаграмма с подписями сбоку. Доли без подписей не читаются, а
/// подписи без круга не дают увидеть, кто здесь главный.</summary>
public partial class Pie : HBoxContainer
{
    private const int Across = 168;

    /// <summary>Место под подписи. Задано, иначе без владельцев круг съезжает вбок.</summary>
    private const int Legend = 250;

    private Dial _dial = null!;
    private VBoxContainer _legend = null!;

    public static Pie Create()
    {
        var pie = new Pie { MouseFilter = MouseFilterEnum.Ignore };
        pie.AddThemeConstantOverride("separation", 18);

        pie._dial = new Dial
        {
            CustomMinimumSize = new Vector2(Across, Across),
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
            MouseFilter = MouseFilterEnum.Ignore,
        };

        pie.AddChild(pie._dial);

        pie._legend = new VBoxContainer
        {
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            MouseFilter = MouseFilterEnum.Ignore,
        };

        pie._legend.AddThemeConstantOverride("separation", 2);
        pie._legend.CustomMinimumSize = new Vector2(Legend, 0);
        pie.AddChild(pie._legend);

        return pie;
    }

    public void Show(IReadOnlyList<Wedge> wedges)
    {
        _dial.Wedges = wedges;
        _dial.QueueRedraw();

        Ui.Trim(_legend, wedges.Count);

        for (var index = 0; index < wedges.Count; index++)
        {
            if (index >= _legend.GetChildCount()) _legend.AddChild(Row());

            var row = _legend.GetChild(index);
            var wedge = wedges[index];

            ((ColorRect)row.GetChild(0)).Color = wedge.Colour;
            row.GetChild<Label>(1).Text = wedge.Name;
            row.GetChild<Label>(1).AddThemeColorOverride("font_color", wedge.Colour);
            row.GetChild<Label>(3).Text = Fmt.Percent(wedge.Share * 100);
            row.GetChild<Label>(4).Text = wedge.Text;
        }
    }

    private static Control Row()
    {
        var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", 7);

        row.AddChild(new ColorRect
        {
            CustomMinimumSize = new Vector2(9, 9),
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
            MouseFilter = MouseFilterEnum.Ignore,
        });

        row.AddChild(Ui.Text(string.Empty, 14, 600));
        row.AddChild(Ui.Spring());
        row.AddChild(Ui.Number(string.Empty, 14, Skin.Bright, 52));
        row.AddChild(Ui.Number(string.Empty, 14, Skin.Dim, 76));

        return row;
    }

    /// <summary>Сам круг. Дольки рисуются веером треугольников: готового рисования
    /// сектора в Godot нет.</summary>
    private partial class Dial : Control
    {
        private const int Steps = 96;

        public IReadOnlyList<Wedge> Wedges = [];

        public override void _Draw()
        {
            var centre = Size / 2f;
            var outer = Mathf.Min(Size.X, Size.Y) / 2f - 4;
            var inner = outer * 0.54f;

            DrawCircle(centre, outer + 3, new Color(Skin.Ink, 0.8f));

            var from = -Mathf.Pi / 2;

            foreach (var wedge in Wedges)
            {
                var angle = (float)(wedge.Share * Mathf.Tau);
                if (angle <= 0.0005f) continue;

                Ring(centre, inner, outer, from, from + angle, wedge.Colour);
                from += angle;
            }

            DrawArc(centre, inner, 0, Mathf.Tau, Steps, new Color(Skin.Line, 0.9f), 1, true);
            DrawArc(centre, outer, 0, Mathf.Tau, Steps, new Color(Skin.Line, 0.9f), 1, true);
        }

        /// <summary>Кольцевой сектор четырёхугольниками. Веером треугольников из середины
        /// цвет растекался пятнами: у такого веера все треугольники делят одну вершину.</summary>
        private void Ring(Vector2 centre, float inner, float outer, float from, float to, Color colour)
        {
            var count = Mathf.Max(2, (int)(Steps * (to - from) / Mathf.Tau) + 2);
            var dark = colour.Darkened(0.40f);
            var light = colour.Lightened(0.14f);

            for (var step = 0; step < count - 1; step++)
            {
                var here = Edge(centre, Mathf.Lerp(from, to, (float)step / (count - 1)), inner, outer);
                var next = Edge(centre, Mathf.Lerp(from, to, (float)(step + 1) / (count - 1)), inner, outer);

                DrawPolygon(
                    [here.Inner, here.Outer, next.Outer, next.Inner],
                    [dark, light, light, dark]);
            }
        }

        private static (Vector2 Inner, Vector2 Outer) Edge(Vector2 centre, float angle, float inner, float outer)
        {
            var away = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));

            return (centre + away * inner, centre + away * outer);
        }
    }
}
