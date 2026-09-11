using Godot;

/// <summary>Подсказка-график: заголовок и один график по истории. Там, где важно не
/// сегодняшнее число, а куда оно идёт, словами не расскажешь.</summary>
public static class TrendCard
{
    public static (string Key, Control Body) Of(
        string key,
        string title,
        Func<double, string> show,
        params Trace[] traces)
    {
        var card = new VBoxContainer { CustomMinimumSize = new Vector2(340, 0) };
        card.AddThemeConstantOverride("separation", 6);
        card.AddChild(Ui.Text(title, 17, 700, Skin.Bright));

        var chart = Chart.Create(string.Empty, show);
        chart.Show(traces);
        card.AddChild(chart);

        return (key, card);
    }
}
