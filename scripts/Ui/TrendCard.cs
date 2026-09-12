using Godot;

/// <summary>Подсказка-график: заголовок и один график по истории. Там, где важно не
/// сегодняшнее число, а куда оно идёт, словами не расскажешь.</summary>
public static class TrendCard
{
    /// <param name="traces">Ряды считаются при каждой сборке: подсказка висит, а игра
    /// идёт, и график должен идти вместе с ней.</param>
    public static (string Key, Func<Control> Body) Of(
        string key,
        string title,
        Func<double, string> show,
        Func<Trace[]> traces) =>
        (key, () =>
        {
            var card = new VBoxContainer { CustomMinimumSize = new Vector2(340, 0) };
            card.AddThemeConstantOverride("separation", 6);
            card.AddChild(Ui.Text(title, 17, 700, Skin.Bright));

            var chart = Chart.Create(string.Empty, show);
            chart.Show(traces());
            card.AddChild(chart);

            return card;
        });
}
