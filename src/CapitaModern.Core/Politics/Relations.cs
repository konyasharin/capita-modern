using CapitaModern.Core.Economy;

namespace CapitaModern.Core.Politics;

/// <summary>Как страны друг к другу относятся: от −100 до 100.</summary>
/// <remarks>
/// Пар у двух сотен стран сорок тысяч, руками их не задать. Поэтому отношение выводится
/// из блоков, а поверх лежат отдельные пары — для случаев, которые из блока не следуют.
/// </remarks>
public sealed class Relations
{
    public const int Friendly = 60;
    public const int Neutral = 0;

    /// <summary>Своим — своё.</summary>
    private const int SameBloc = 60;

    private readonly Bloc[] _bloc;
    private readonly Dictionary<(byte, byte), int> _pairs = new();

    public Relations(IReadOnlyDictionary<byte, Bloc>? blocs = null, int countries = 256)
    {
        _bloc = new Bloc[countries];
        foreach (var (country, bloc) in blocs ?? new Dictionary<byte, Bloc>()) _bloc[country] = bloc;
    }

    public Bloc BlocOf(byte country) => _bloc[country];

    public int Between(byte a, byte b)
    {
        if (a == b) return 100;
        if (_pairs.TryGetValue(Key(a, b), out var set)) return set;

        return BetweenBlocs(_bloc[a], _bloc[b]);
    }

    /// <summary>Отдельная пара поверх блоков: союз, война, старая обида.</summary>
    public void Set(byte a, byte b, int attitude)
    {
        _pairs[Key(a, b)] = Math.Clamp(attitude, -100, 100);
    }

    /// <summary>Внутри блока дружат, Запад с Россией враждует сильнее всего, а
    /// неприсоединившиеся ровны со всеми — на них и идёт борьба за влияние.</summary>
    public static int BetweenBlocs(Bloc a, Bloc b)
    {
        if (a == b) return a == Bloc.NonAligned ? 10 : SameBloc;
        if (a == Bloc.NonAligned || b == Bloc.NonAligned) return Neutral;

        var pair = a < b ? (a, b) : (b, a);

        return pair switch
        {
            (Bloc.West, Bloc.China) => -30,
            (Bloc.West, Bloc.Russia) => -70,
            (Bloc.China, Bloc.Russia) => 40,
            _ => Neutral,
        };
    }

    private static (byte, byte) Key(byte a, byte b) => a < b ? (a, b) : (b, a);
}
