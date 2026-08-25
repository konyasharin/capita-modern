using Godot;

/// <summary>
/// Временная демонстрация движения фронта: включается ключом --war.
/// Настоящей военной механики в игре пока нет, это заглушка, чтобы посмотреть,
/// как поле владения выглядит в движении. См. docs/02-war.md.
/// </summary>
public partial class FrontDemo : Node
{
    /// <summary>Шаг симуляции фиксирован, а не берётся из delta: тогда кадр N — это
    /// всегда одно и то же состояние, и серия снимков воспроизводима.</summary>
    private const float Step = 1f / 60f;

    /// <summary>Сколько прочности снимает наступление за секунду.</summary>
    private const float Push = 0.9f;

    /// <summary>Во сколько раз сильнее давление на выбранном направлении.</summary>
    private const float FocusGain = 2.5f;

    /// <summary>Радиус направления главного удара, в ячейках.</summary>
    private const float FocusRadius = 55f;

    /// <summary>Период качания фронта туда-обратно, в секундах.</summary>
    private const float Swing = 18f;

    /// <summary>Насколько сильно поле подтягивается к среднему по соседям за тик.</summary>
    private const float Blur = 0.25f;

    private WorldMapView _map = null!;
    private int[] _cells = [];
    private byte _attacker;
    private byte _defender;
    private float _time;
    private int _focusX;
    private int _focusY;

    /// <summary>Знаковое поле: плюс — территория наступающего, минус — обороняющегося,
    /// модуль — прочность владения. Одно число вместо пары «владелец плюс прочность»
    /// позволяет размывать фронт обычным усреднением.</summary>
    private float[] _field = [];
    private float[] _next = [];
    private bool[] _zone = [];

    public override void _Ready()
    {
        if (!OS.GetCmdlineUserArgs().Contains("--war"))
        {
            QueueFree();
            return;
        }

        _map = GetParent().GetNode<WorldMapView>("WorldMapView");

        _attacker = (byte)(_map.Countries.ByIso("RUS")?.Id ?? 0);
        _defender = (byte)(_map.Countries.ByIso("UKR")?.Id ?? 0);

        if (_attacker == 0 || _defender == 0)
        {
            GD.PushError("не нашёл страны для демонстрации");
            QueueFree();
            return;
        }

        // Держим только ячейки двух воюющих стран: перебирать всю карту каждый тик незачем.
        var list = new List<int>();

        for (var i = 0; i < _map.Map.Owner.Length; i++)
        {
            if (_map.Map.Owner[i] == _attacker || _map.Map.Owner[i] == _defender) list.Add(i);
        }

        _cells = [.. list];

        _field = new float[_map.Map.Owner.Length];
        _next = new float[_map.Map.Owner.Length];
        _zone = new bool[_map.Map.Owner.Length];

        foreach (var at in _cells)
        {
            _zone[at] = true;
            _field[at] = _map.Map.Owner[at] == _attacker ? 1f : -1f;
        }

        // Направление главного удара — восток Украины.
        var (x, y) = ToCell(38.0, 48.3);
        _focusX = x;
        _focusY = y;

        GD.Print($"война: ячеек в зоне {_cells.Length}, удар на {_focusX},{_focusY}");
    }

    public override void _Process(double delta)
    {
        _time += Step;

        // Качание: полпериода наступает одна сторона, полпериода отыгрывает другая.
        var swing = Mathf.Sin(Mathf.Tau * _time / Swing);

        Press(swing * Push * Step);
        Diffuse();
        Apply();

        _map.RefreshField();

        if (Mathf.RoundToInt(_time / Step) % 60 == 0)
        {
            var held = 0;
            foreach (var at in _cells) if (_map.Map.Owner[at] == _defender) held++;
            GD.Print($"t={_time:0.0} качание={swing:0.00} у обороны {held} ячеек");
        }
    }

    /// <summary>
    /// Давление приходится на полосу соприкосновения и тем сильнее, чем ближе к
    /// направлению главного удара.
    /// </summary>
    private void Press(float force)
    {
        var width = _map.Map.Width;

        foreach (var at in _cells)
        {
            var touching = false;
            foreach (var n in Around(at, width))
            {
                if (_zone[n] && _field[n] * _field[at] < 0f) { touching = true; break; }
            }

            if (!touching) continue;

            var dx = (at % width - _focusX) / FocusRadius;
            var dy = (at / width - _focusY) / FocusRadius;

            _field[at] += force * (1f + FocusGain * Mathf.Exp(-(dx * dx + dy * dy)));
        }
    }

    /// <summary>
    /// Размытие поля — не косметика, а механика: оно срезает иглы, не даёт прорыву
    /// выродиться в нить шириной в ячейку и удерживает фронт гладким сам по себе.
    /// </summary>
    private void Diffuse()
    {
        var width = _map.Map.Width;

        foreach (var at in _cells)
        {
            var sum = 0f;
            var count = 0;

            foreach (var n in Around(at, width))
            {
                if (!_zone[n]) continue;
                sum += _field[n];
                count++;
            }

            _next[at] = count == 0
                ? _field[at]
                : Mathf.Clamp(Mathf.Lerp(_field[at], sum / count, Blur), -1f, 1f);
        }

        foreach (var at in _cells) _field[at] = _next[at];
    }

    /// <summary>Знак поля решает, чья ячейка, модуль — насколько прочно она держится.</summary>
    private void Apply()
    {
        var owner = _map.Map.Owner;
        var control = _map.Map.Control;

        foreach (var at in _cells)
        {
            owner[at] = _field[at] >= 0f ? _attacker : _defender;
            control[at] = Mathf.Max(0.02f, Mathf.Abs(_field[at]));
        }
    }

    private static int[] Around(int at, int width) => [at - 1, at + 1, at - width, at + width];

    private (int X, int Y) ToCell(double lon, double lat)
    {
        var deg = 360.0 / _map.Map.Width;
        return (
            (int)((lon + 180.0) / 360.0 * _map.Map.Width),
            (int)((WorldMap.LatTop - lat) / (_map.Map.Height * deg) * _map.Map.Height)
        );
    }
}
