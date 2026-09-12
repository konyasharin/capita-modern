using CapitaModern.Core.Buildings;
using CapitaModern.Core.Economy;

namespace CapitaModern.Core.World;

/// <summary>Частная компания: чем владеет, что умеет и сколько у неё денег.</summary>
/// <remarks>
/// Здания остаются на счету области — там их видит карта и оттуда идёт выпуск. Компания
/// держит свою долю того же счёта: сумма по компаниям равна счёту области. Полное
/// разделение складов и цен по компаниям придёт позже; сейчас важно другое — кто получает
/// прибыль и кто решает, что строить.
///
/// Ниша — это не украшение, а главное ограничение. Лесопромышленная компания не станет
/// строить ракетный завод, даже если он прибыльнее: у неё нет ни людей, ни связей, ни
/// понимания дела. Оттого страна и не перестраивается в одну самую выгодную отрасль за
/// пару лет, как это выходило, когда решение принимало государство целиком.
/// </remarks>
public sealed class Company
{
    public int Id { get; }
    public byte Country { get; }
    public string Name { get; }

    /// <summary>В каких отраслях работает. Одна у большинства, несколько у крупных.</summary>
    public IReadOnlyList<Sector> Focus { get; }

    /// <summary>Свои деньги. Из них строит и с них платит налоги.</summary>
    public Money Cash { get; private set; }

    /// <summary>Сколько чего у неё есть, по областям.</summary>
    private readonly Dictionary<(int Region, BuildingType Type), int> _buildings = [];

    public Company(int id, byte country, string name, IReadOnlyList<Sector> focus, Money cash = default)
    {
        Id = id;
        Country = country;
        Name = name;
        Focus = focus;
        Cash = cash;
    }

    public IReadOnlyDictionary<(int Region, BuildingType Type), int> Buildings => _buildings;

    /// <summary>Берётся ли компания за эту отрасль.</summary>
    public bool Works(Sector sector) => Focus.Contains(sector);

    public void Add(int region, BuildingType type, int count)
    {
        if (count <= 0) return;

        _buildings[(region, type)] = _buildings.GetValueOrDefault((region, type)) + count;
        Size += count;
    }

    /// <summary>Убирает сколько получится и говорит, сколько убрало.</summary>
    public int Remove(int region, BuildingType type, int count)
    {
        var have = _buildings.GetValueOrDefault((region, type));
        var gone = Math.Min(have, count);
        if (gone <= 0) return 0;

        if (have == gone) _buildings.Remove((region, type));
        else _buildings[(region, type)] = have - gone;

        Size -= gone;

        return gone;
    }

    public int CountOf(BuildingType type)
    {
        var total = 0;
        foreach (var ((_, kind), count) in _buildings)
        {
            if (kind == type) total += count;
        }

        return total;
    }

    /// <summary>Всего зданий у компании. По нему делится прибыль страны.</summary>
    /// <remarks>Считается на лету, а не перебором: прибыль делится каждый тик, и перебор
    /// всех зданий всех компаний стоил втрое дороже самого тика.</remarks>
    public int Size { get; private set; }

    public void Earn(Money amount)
    {
        if (amount.Raw <= 0) return;

        Cash += amount;
    }

    /// <summary>Тратит, если хватает. Не хватило — не тратит вовсе.</summary>
    public bool TrySpend(Money amount)
    {
        if (amount.Raw <= 0 || Cash - amount < default(Money)) return false;

        Cash -= amount;

        return true;
    }
}
