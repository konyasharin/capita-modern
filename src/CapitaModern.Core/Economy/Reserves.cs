namespace CapitaModern.Core.Economy;

/// <summary>Чем страна платит за импорт. Не одно число, а набор вложений.</summary>
/// <remarks>
/// Своя валюта и чужая — разные вещи: первую государство печатает, вторую только
/// зарабатывает или занимает. Поэтому резервы лежат отдельно от <see cref="Treasury"/>.
/// </remarks>
public sealed class Reserves
{
    private readonly List<Reserve> _held = [];
    private readonly HashSet<byte> _frozen = [];

    public Reserves(IEnumerable<Reserve>? start = null)
    {
        foreach (var reserve in start ?? []) Add(reserve.Kind, reserve.Issuer, reserve.Amount);
    }

    public IReadOnlyList<Reserve> Held => _held;

    /// <summary>Чем можно заплатить прямо сейчас: замороженное не считается.</summary>
    public Money Liquid
    {
        get
        {
            var total = default(Money);
            foreach (var reserve in _held)
            {
                if (!Frozen(reserve)) total += reserve.Amount;
            }

            return total;
        }
    }

    /// <summary>Сколько стоит всё, включая замороженное. Для показа игроку.</summary>
    public Money Value
    {
        get
        {
            var total = default(Money);
            foreach (var reserve in _held) total += reserve.Amount;

            return total;
        }
    }

    public void Add(ReserveKind kind, byte issuer, Money amount)
    {
        if (amount < default(Money)) throw new ArgumentOutOfRangeException(nameof(amount));
        if (amount.Raw == 0) return;

        var at = _held.FindIndex(held => held.Kind == kind && held.Issuer == issuer);
        if (at < 0) _held.Add(new Reserve(kind, issuer, amount));
        else _held[at] = _held[at] with { Amount = _held[at].Amount + amount };
    }

    /// <summary>Тратит с самого ликвидного. Не хватило — не тронуто ничего.</summary>
    public bool TrySpend(Money amount)
    {
        if (amount < default(Money)) throw new ArgumentOutOfRangeException(nameof(amount));
        if (Liquid < amount) return false;

        var left = amount;
        for (var i = 0; i < _held.Count && left.Raw > 0; i++)
        {
            if (Frozen(_held[i])) continue;

            var taken = _held[i].Amount < left ? _held[i].Amount : left;
            _held[i] = _held[i] with { Amount = _held[i].Amount - taken };
            left -= taken;
        }

        return true;
    }

    /// <summary>Эмитент перестал пускать к своим деньгам. Настоящий рычаг: так в 2022
    /// заморозили половину российских резервов.</summary>
    public void Freeze(byte issuer) => _frozen.Add(issuer);

    public void Unfreeze(byte issuer) => _frozen.Remove(issuer);

    /// <summary>Металл и криптовалюта не у эмитента, их не отнять.</summary>
    private bool Frozen(Reserve reserve) =>
        reserve.Kind is not (ReserveKind.Metal or ReserveKind.Crypto) && _frozen.Contains(reserve.Issuer);
}
