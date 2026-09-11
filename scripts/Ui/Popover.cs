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

    /// <summary>От чего открыт. Пока мышь над ним, подсказка не гаснет.</summary>
    public Control Source { get; private set; } = null!;

    public string Key { get; private set; } = string.Empty;

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
        title.AddThemeColorOverride("font_color", Skin.Bright);
        title.AddThemeFontSizeOverride("font_size", 16);
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

        popover._body.AddThemeColorOverride("default_color", Skin.Text);
        popover._body.AddThemeFontSizeOverride("normal_font_size", 14);
        popover._body.Text = Markup(article.Text);
        rows.AddChild(popover._body);

        return popover;
    }

    /// <summary>Кого сейчас показывает подсказка внутри текста.</summary>
    public RichTextLabel Body => _body;

    /// <summary>Ставится один раз и больше не двигается: подсказка, которая ездит за
    /// курсором, не даёт себя прочитать.</summary>
    public void PlaceUnder(Control source, Rect2 screen)
    {
        var anchor = source.GetGlobalRect();
        var size = GetCombinedMinimumSize();

        var x = Mathf.Clamp(anchor.Position.X + anchor.Size.X / 2 - size.X / 2, 8f, screen.Size.X - size.X - 8f);
        var y = anchor.Position.Y + anchor.Size.Y;

        // Внизу не помещается — вешаем над источником.
        if (y + size.Y > screen.Size.Y - 8f) y = anchor.Position.Y - size.Y;

        Position = new Vector2(x, y);
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
