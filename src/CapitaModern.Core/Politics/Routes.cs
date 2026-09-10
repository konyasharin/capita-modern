using CapitaModern.Core.Economy;

namespace CapitaModern.Core.Politics;

/// <summary>Как страна добирается до мирового рынка.</summary>
/// <remarks>
/// Приморская страна грузит в своём порту. Страна без моря везёт через соседей, платит им
/// за проход и упирается в их согласие: недружественный сосед пропустит дороже, враждебный
/// не пропустит вовсе. Так у Афганистана и Средней Азии выход к рынку зависит не от их
/// экономики, а от того, с кем они дружат.
///
/// Пути считаются не до каждой страны, а до рынка: торговля у нас общая, отправитель и
/// получатель в ней не встречаются. Этого хватает, чтобы перекрытие транзита работало.
/// </remarks>
public sealed class Routes
{
    /// <summary>Дальше этого пути не ищем: за пять чужих границ уже никто не возит.</summary>
    public const int Unreachable = 6;

    private readonly bool[] _coastal;
    private readonly List<byte>[] _neighbours;
    private readonly int[] _hops;
    private readonly byte[] _through;

    /// <param name="map">Кто приморский и с кем граничит. Пусто — считаем всех
    /// приморскими: мир без географии не должен оказаться отрезанным от рынка.</param>
    public Routes(IReadOnlyDictionary<byte, (bool Coastal, byte[] Neighbours)>? map = null, int countries = 256)
    {
        map ??= new Dictionary<byte, (bool, byte[])>();
        _coastal = new bool[countries];
        _neighbours = new List<byte>[countries];
        _hops = new int[countries];
        _through = new byte[countries];

        for (var i = 0; i < countries; i++)
        {
            _neighbours[i] = [];
            _coastal[i] = map.Count == 0;
        }

        foreach (var (country, info) in map)
        {
            _coastal[country] = info.Coastal;
            _neighbours[country].AddRange(info.Neighbours);
        }

        Recompute(null, null);
    }

    public bool Coastal(byte country) => _coastal[country];

    public IReadOnlyList<byte> NeighboursOf(byte country) => _neighbours[country];

    /// <summary>Сколько чужих границ до моря. Ноль у приморских.</summary>
    public int HopsTo(byte country) => _hops[country];

    /// <summary>Через кого идёт первый шаг. Ему и платят за проход.</summary>
    public byte Through(byte country) => _through[country];

    public bool CanReachMarket(byte country) => _hops[country] < Unreachable;

    /// <summary>Пересчитывает пути. Звать при смене отношений, запретов и границ.</summary>
    /// <param name="attitude">Как первый пропускает второго; ниже враждебности не пустит.</param>
    /// <param name="blocked">Кто вообще никого через себя не пускает.</param>
    public void Recompute(Func<byte, byte, int>? attitude, Func<byte, bool>? blocked)
    {
        Array.Fill(_hops, Unreachable);
        Array.Clear(_through);

        var wave = new Queue<byte>();
        for (var country = 0; country < _coastal.Length; country++)
        {
            if (!_coastal[country]) continue;

            _hops[country] = 0;
            wave.Enqueue((byte)country);
        }

        // Волной от берега вглубь: у всех переходов одна цена, поэтому обход в ширину и
        // даёт кратчайший путь.
        while (wave.Count > 0)
        {
            var near = wave.Dequeue();
            if (blocked?.Invoke(near) == true) continue;

            foreach (var far in _neighbours[near])
            {
                if (_hops[far] <= _hops[near] + 1) continue;
                if (attitude?.Invoke(near, far) <= CreditMarket.Hostile) continue;

                _hops[far] = _hops[near] + 1;
                _through[far] = near;
                wave.Enqueue(far);
            }
        }
    }
}
