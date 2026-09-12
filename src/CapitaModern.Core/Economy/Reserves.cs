namespace CapitaModern.Core.Economy;

/// <summary>Чем страна платит за импорт. Не одно число, а набор вложений.</summary>
/// <remarks>
/// Своя валюта и чужая — разные вещи: первую государство печатает, вторую только
/// зарабатывает или занимает. Поэтому резервы лежат отдельно от <see cref="Treasury"/>.
///
/// Заморозка идёт по месту хранения, а не по эмитенту. Отнять могут только то, что лежит
/// у них: половину российских резервов в 2022 отняли в Европе и США, а юани в Китае и
/// золото в своих хранилищах остались. Поэтому в резервах и хранится
/// <see cref="Reserve.Custodian"/>, и поэтому же есть смысл разносить их по разным местам.
/// </remarks>
public sealed class Reserves
{
    private readonly List<Reserve> _held = [];
    private readonly HashSet<byte> _frozenBy = [];

    public Reserves(IEnumerable<Reserve>? start = null)
    {
        foreach (var reserve in start ?? []) Add(reserve.Kind, reserve.Issuer, reserve.Custodian, reserve.Amount);
    }

    public IReadOnlyList<Reserve> Held => _held;

    /// <summary>Кто заморозил. Пустое — все резервы доступны.</summary>
    public IReadOnlyCollection<byte> FrozenBy => _frozenBy;

    /// <summary>Чем можно заплатить прямо сейчас.</summary>
    /// <remarks>Считается по ходу, а не перебором вложений: спрашивают его на каждой
    /// сделке, а сделок за тик двадцать шесть тысяч.</remarks>
    public Money Liquid { get; private set; }

    /// <summary>Сколько отнято. Показывать игроку это надо отдельно: деньги как бы есть,
    /// а тратить нельзя.</summary>
    public Money Frozen { get; private set; }

    /// <summary>Сколько всего, вместе с отнятым.</summary>
    public Money Value => Liquid + Frozen;

    /// <summary>Во что превращается приходящая выручка. У себя её держат в золоте —
    /// своей валютой резервы не бывают; у чужого хранителя это его валюта.</summary>
    public static Reserve Incoming(byte owner, byte custodian, Money amount) =>
        custodian == owner
            ? new Reserve(ReserveKind.Metal, owner, owner, amount)
            : new Reserve(ReserveKind.ForeignCurrency, custodian, custodian, amount);

    public void Add(Reserve reserve) => Add(reserve.Kind, reserve.Issuer, reserve.Custodian, reserve.Amount);

    public void Add(ReserveKind kind, byte issuer, byte custodian, Money amount)
    {
        if (amount < default(Money)) throw new ArgumentOutOfRangeException(nameof(amount));
        if (amount.Raw == 0) return;

        // Обычным перебором, а не FindIndex: тот создаёт замыкание на каждый вызов, а
        // вызовов за тик по одному на сделку — двадцать шесть тысяч.
        for (var i = 0; i < _held.Count; i++)
        {
            var held = _held[i];
            if (held.Kind != kind || held.Issuer != issuer || held.Custodian != custodian) continue;

            _held[i] = held with { Amount = held.Amount + amount };
            Note(custodian, amount);

            return;
        }

        _held.Add(new Reserve(kind, issuer, custodian, amount));
        Note(custodian, amount);
    }

    /// <summary>Тратит с любого доступного места. Не хватило — не тронуто ничего.</summary>
    public bool TrySpend(Money amount)
    {
        if (amount < default(Money)) throw new ArgumentOutOfRangeException(nameof(amount));
        if (amount.Raw == 0) return true;
        if (Liquid < amount) return false;

        var left = amount;
        for (var i = 0; i < _held.Count && left.Raw > 0; i++)
        {
            if (IsFrozen(_held[i])) continue;

            var taken = _held[i].Amount < left ? _held[i].Amount : left;
            _held[i] = _held[i] with { Amount = _held[i].Amount - taken };
            left -= taken;
        }

        Liquid -= amount;

        return true;
    }

    /// <summary>Хранитель перестал пускать к тому, что у него лежит.</summary>
    /// <remarks>Морозит именно хранитель: отнять можно только своими руками. Присоединиться
    /// к чужим санкциям — это самому позвать <see cref="Freeze"/> у себя.</remarks>
    public void Freeze(byte custodian)
    {
        if (_frozenBy.Add(custodian)) Recount();
    }

    public void Unfreeze(byte custodian)
    {
        if (_frozenBy.Remove(custodian)) Recount();
    }

    /// <summary>Относит пришедшее к доступному или к отнятому.</summary>
    private void Note(byte custodian, Money amount)
    {
        if (_frozenBy.Contains(custodian)) Frozen += amount;
        else Liquid += amount;
    }

    /// <summary>Пересчитывает оба итога заново. Нужно только при заморозке: она редкая.</summary>
    private void Recount()
    {
        Liquid = Total(frozen: false);
        Frozen = Total(frozen: true);
    }

    /// <summary>Своё хранилище: у себя не отнимут.</summary>
    public static byte HomeVault(byte owner) => owner;

    private bool IsFrozen(Reserve reserve) => _frozenBy.Contains(reserve.Custodian);

    private Money Total(bool frozen)
    {
        var total = default(Money);
        foreach (var reserve in _held)
        {
            if (IsFrozen(reserve) == frozen) total += reserve.Amount;
        }

        return total;
    }
}
