using Godot;

/// <summary>
/// Утилита разработки: снять кадр и выйти.
///   godot --path . -- --shot=C:/tmp/map.png --shot-frame=40
/// Без аргумента --shot нода немедленно удаляется и ничего не делает.
/// </summary>
public partial class DevScreenshot : Node
{
    private string? _path;
    private int _framesLeft = 30;
    private int _total = 30;

    /// <summary>Сколько игровых суток прокручивается за один кадр.</summary>
    private const int DaysPerFrame = 20;
    private Vector2? _focus;
    private float _zoom = 1f;
    private readonly List<Vector2> _mouse = [];
    private Vector2? _click;
    private int? _tab;
    private bool _window;
    private int? _good;
    private int _days;

    public override void _Ready()
    {
        foreach (var arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--shot=", StringComparison.Ordinal))
            {
                _path = arg["--shot=".Length..];
            }
            else if (arg.StartsWith("--shot-frame=", StringComparison.Ordinal)
                && int.TryParse(arg["--shot-frame=".Length..], out var frames))
            {
                _framesLeft = frames;
                _total = frames;
            }
            else if (arg.StartsWith("--shot-mouse=", StringComparison.Ordinal))
            {
                // Курсор нужен, чтобы снять подсказку: она открывается по наведению.
                // Точек можно дать несколько через «;» — так проверяется, гаснет ли
                // вложенная подсказка, когда мышь ушла с термина.
                foreach (var step in arg["--shot-mouse=".Length..].Split(';'))
                {
                    var at = step.Split(',');
                    if (at.Length == 2 && float.TryParse(at[0], out var mx) && float.TryParse(at[1], out var my))
                    {
                        _mouse.Add(new Vector2(mx, my));
                    }
                }
            }
            else if (arg.StartsWith("--shot-days=", StringComparison.Ordinal)
                && int.TryParse(arg["--shot-days=".Length..], out var days))
            {
                _days = days;
            }
            else if (arg == "--shot-window")
            {
                _window = true;
            }
            else if (arg.StartsWith("--shot-good=", StringComparison.Ordinal)
                && int.TryParse(arg["--shot-good=".Length..], out var good))
            {
                _good = good;
            }
            else if (arg.StartsWith("--shot-tab=", StringComparison.Ordinal)
                && int.TryParse(arg["--shot-tab=".Length..], out var tab))
            {
                _tab = tab;
            }
            else if (arg.StartsWith("--shot-click=", StringComparison.Ordinal))
            {
                var at = arg["--shot-click=".Length..].Split(',');
                if (at.Length == 2 && float.TryParse(at[0], out var cx) && float.TryParse(at[1], out var cy))
                {
                    _click = new Vector2(cx, cy);
                }
            }
            else if (arg.StartsWith("--shot-focus=", StringComparison.Ordinal))
            {
                var parts = arg["--shot-focus=".Length..].Split(',');
                if (parts.Length == 3
                    && float.TryParse(parts[0], out var x)
                    && float.TryParse(parts[1], out var y)
                    && float.TryParse(parts[2], out var zoom))
                {
                    _focus = new Vector2(x, y);
                    _zoom = zoom;
                }
            }
        }

        if (_path is null)
        {
            QueueFree();
            return;
        }

        // Кадры ограничены. Без ограничения игра молотит под четыреста кадров в секунду
        // и без нужды греет видеокарту: снимку хватает и шестидесяти.
        Engine.MaxFps = 60;
    }

    public override void _Process(double delta)
    {
        // Фокус ставится здесь, а не в _Ready: камера получает границы карты
        // только после _Ready родителя, то есть уже после нашего.
        if (_focus is { } point)
        {
            GetParent().GetNode<MapCamera>("MapCamera").FocusOn(point, _zoom);
            _focus = null;
        }

        // Прокрутка идёт порциями, и счётчик кадров при этом стоит. Разом нельзя: сотня
        // тиков в одном кадре вешает его на секунды, а зависший кадр Windows считает
        // отказом видеодрайвера и сбрасывает его вместе с игрой.
        if (_days > 0)
        {
            var loop = GetParent().GetNode<GameLoop>("GameLoop");
            var step = Math.Min(_days, DaysPerFrame);

            for (var day = 0; day < step; day++) loop.Advance();
            _days -= step;

            return;
        }

        if (_window && _framesLeft == _total - 8)
        {
            var window = GetParent().GetNode<ResourceWindow>("Overlay/ResourceWindow");

            window.Toggle();
            if (_good is { } good) window.Pick((CapitaModern.Core.Economy.GoodType)good);

            _window = false;
        }

        if (_tab is { } which && _framesLeft == _total - 8)
        {
            GetParent().GetNode<SideTabs>("Ui/SideTabs").Show(which);
            _tab = null;
        }

        // Щелчок по карте: панель выбора ждёт нажатия и отпускания, а не одного события.
        if (_click is { } spot && _framesLeft == _total - 11)
        {
            Input.WarpMouse(spot);
            Input.ParseInputEvent(new InputEventMouseButton
            {
                ButtonIndex = MouseButton.Left, Pressed = true, Position = spot, GlobalPosition = spot,
            });

            Input.ParseInputEvent(new InputEventMouseButton
            {
                ButtonIndex = MouseButton.Left, Pressed = false, Position = spot, GlobalPosition = spot,
            });

            _click = null;
        }

        // Точки проходятся по одной, по восемь кадров на каждую. Начинаются они после
        // того, как открыта вкладка: до этого наводиться попросту не на что.
        var since = _total - 14 - _framesLeft;
        if (since >= 0 && since % 8 == 0 && since / 8 < _mouse.Count)
        {
            var cursor = _mouse[since / 8];

            // WarpMouse двигает курсор молча, а подсказки на терминах ждут события
            // движения — иначе наведение на них не проверить.
            Input.WarpMouse(cursor);
            Input.ParseInputEvent(new InputEventMouseMotion { Position = cursor, GlobalPosition = cursor });
        }

        if (--_framesLeft > 0)
        {
            return;
        }

        var image = GetViewport().GetTexture().GetImage();
        var error = image.SavePng(_path);

        GD.Print($"fps {Engine.GetFramesPerSecond()}");
        GD.Print(error == Error.Ok ? $"снимок: {_path}" : $"снимок не удался: {error}");
        GetTree().Quit();
    }
}
