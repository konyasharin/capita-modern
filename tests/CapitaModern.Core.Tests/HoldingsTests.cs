using CapitaModern.Core.Buildings;
using CapitaModern.Core.Economy;
using CapitaModern.Core.World;
using Xunit;

namespace CapitaModern.Core.Tests;

/// <summary>Указатель владения: кто чем владеет в области.</summary>
public class HoldingsTests
{
    private static Company Firm(int id) => new(id, 1, $"проба {id}", [Sector.Mining]);

    [Fact]
    public void EmptyRegionHasNoOwners()
    {
        Assert.Empty(new Holdings().OwnersOf(7, BuildingType.CoalMine));
    }

    [Fact]
    public void BuildingRegistersTheOwner()
    {
        var ledger = new Holdings();
        var one = Firm(1);
        one.Ledger = ledger;

        one.Add(7, BuildingType.CoalMine, 3);

        Assert.Single(ledger.OwnersOf(7, BuildingType.CoalMine));
    }

    /// <summary>Хозяин остаётся в списке, пока у него хоть что-то есть.</summary>
    [Fact]
    public void OwnerLeavesOnlyWhenNothingIsLeft()
    {
        var ledger = new Holdings();
        var one = Firm(1);
        one.Ledger = ledger;

        one.Add(7, BuildingType.CoalMine, 3);
        one.Remove(7, BuildingType.CoalMine, 1);

        Assert.Single(ledger.OwnersOf(7, BuildingType.CoalMine));

        one.Remove(7, BuildingType.CoalMine, 2);

        Assert.Empty(ledger.OwnersOf(7, BuildingType.CoalMine));
    }

    [Fact]
    public void TwoOwnersInOneRegion()
    {
        var ledger = new Holdings();
        var one = Firm(1);
        var two = Firm(2);
        one.Ledger = ledger;
        two.Ledger = ledger;

        one.Add(7, BuildingType.CoalMine, 3);
        two.Add(7, BuildingType.CoalMine, 5);

        Assert.Equal(2, ledger.OwnersOf(7, BuildingType.CoalMine).Count);
        Assert.Equal(5, two.CountAt(7, BuildingType.CoalMine));
    }

    /// <summary>Без указателя компания работает как раньше: тестовые миры его не ставят.</summary>
    [Fact]
    public void WorksWithoutLedger()
    {
        var one = Firm(1);
        one.Add(7, BuildingType.CoalMine, 3);

        Assert.Equal(3, one.CountAt(7, BuildingType.CoalMine));
    }
}
