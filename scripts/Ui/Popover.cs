using Godot;

/// <summary>Всплывающее пояснение. Стоит на месте и не гаснет, пока на нём мышь.</summary>
/// <remarks>
/// Живёт не сам по себе, а в <see cref="PopoverStack"/>: подсказка может открыть
/// подсказку, и закрывать их надо с хвоста.
/// </remarks>
public partial class Popover : PanelContainer
{
    private const int Width = 360;
    private const int Pad = 14;

    /// <summary>Отступ от источника и от краёв экрана.</summary>
    private const int Gap = 8;

    /// <summary>От чего открыт. Пока мышь над ним, подсказка не гаснет.</summary>
    public Control Source { get; private set; } = null!;

    public string Key { get; private set; } = string.Empty;

    /// <summary>Жива ли ещё причина, по которой подсказку открыли.</summary>
    /// <remarks>Для показателя это «курсор над ним», а для термина внутри другой
    /// подсказки — «курсор над самим термином». Одним прямоугольником источника тут не
    /// обойтись: у термина источник — вся строка текста, и вторая подсказка не гасла,
    /// пока мышь оставалась в первой.</remarks>
    public Func<bool> Alive { get; set; } = () => false;

    /// <summary>Термин, над которым сейчас курсор. Пустая строка — ни над каким.</summary>
    public string HotTerm { get; set; } = string.Empty;

    private RichTextLabel _body = null!;

    public static Popover Create(Control source, string key, Article article)
    {
        var popover = new Popover
        {
            Source = source,
            Key = key,
            CustomMinimumSize = new Vector2(Width, 0),
            MouseFilter = MouseFilterEnum.Stop,
        };

        popover.AddThemeStyleboxOverride("panel", Skin.PopoverBox());

        var rows = new VBoxContainer();
        rows.AddThemeConstantOverride("separation", 6);
        popover.AddChild(rows);

        var title = new Label { Text = article.Title, MouseFilter = MouseFilterEnum.Ignore };
        title.AddThemeFontOverride("font", Skin.Weight(600));
        title.AddThemeColorOverride("font_color", Skin.Bright);
        title.AddThemeFontSizeOverride("font_size", 17);
        rows.AddChild(title);

        popover._body = new RichTextLabel
        {
            BbcodeEnabled = true,
            FitContent = true,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            ScrollActive = false,
            CustomMinimumSize = new Vector2(Width - Pad * 2, 0),
            MouseFilter = MouseFilterEnum.Stop,
        };

        popover._body.AddThemeFontOverride("normal_font", Skin.Weight(400));
        popover._body.AddThemeFontOverride("bold_font", Skin.Weight(600));
        popover._body.AddThemeColorOverride("default_color", Skin.Text);
        popover._body.AddThemeFontSizeOverride("normal_font_size", 15);
        popover._body.Text = Markup(article.Text);
        rows.AddChild(popover._body);

        return popover;
    }

    /// <summary>Кого сейчас показывает подсказка внутри текста.</summary>
    public RichTextLabel Body => _body;

    /// <summary>Ставится один раз и больше не двигается: подсказка, которая ездит за
    /// курсором, не даёт себя прочитать.</summary>
    /// <remarks>Сбоку, если источник прижат к левому краю, и снизу во всех остальных
    /// случаях. Вкладки и строки панели стоят слева, и подсказка под ними закрывала бы
    /// то, ради чего её открыли.</remarks>
    public void PlaceNear(Control source, Rect2 screen)
    {
        var anchor = source.GetGlobalRect();
        var size = GetCombinedMinimumSize();

        var beside = anchor.Position.X < screen.Size.X / 3
            && anchor.Position.X + anchor.Size.X + size.X + Gap <= screen.Size.X;

        var x = beside
            ? anchor.Position.X + anchor.Size.X + Gap
            : anchor.Position.X + anchor.Size.X / 2 - size.X / 2;

        var y = beside
            ? anchor.Position.Y
            : anchor.Position.Y + anchor.Size.Y;

        // Снизу не помещается — вешаем выше; сбоку просто подтягиваем вверх.
        if (y + size.Y > screen.Size.Y - Gap)
        {
            y = beside ? screen.Size.Y - size.Y - Gap : anchor.Position.Y - size.Y;
        }

        Position = new Vector2(
            Mathf.Clamp(x, Gap, Mathf.Max(Gap, screen.Size.X - size.X - Gap)),
            Mathf.Max(y, Gap));
    }

    /// <summary>Наши ссылки вида [[ключ|слово]] превращаются в подчёркнутый термин.</summary>
    private static string Markup(string text)
    {
        var result = new System.Text.StringBuilder();
        var rest = text.AsSpan();

        while (true)
        {
            var open = rest.IndexOf("[[");
            if (open < 0) break;

            var close = rest[open..].IndexOf("]]");
            if (close < 0) break;

            var inside = rest.Slice(open + 2, close - 2);
            var bar = inside.IndexOf('|');
            var key = bar < 0 ? inside : inside[..bar];
            var word = bar < 0 ? inside : inside[(bar + 1)..];

            result.Append(rest[..open]);
            result.Append($"[url={key}][color=#{Skin.Link.ToHtml(false)}][u]{word}[/u][/color][/url]");
            rest = rest[(open + close + 2)..];
        }

        result.Append(rest);

        return result.ToString();
    }
}
