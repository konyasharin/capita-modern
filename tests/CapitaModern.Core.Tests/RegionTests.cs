using CapitaModern.Core.Buildings;
using CapitaModern.Core.Economy;
using CapitaModern.Core.World;
using Xunit;

namespace CapitaModern.Core.Tests;

public class RegionTests
{
    private static Region Split(int first, int second, Dictionary<BuildingType, int>? buildings = null) =>
        new(1, 1000, new Dictionary<byte, int> { [1] = first, [2] = second },
            buildings ?? new Dictionary<BuildingType, int>(), new Dictionary<GoodType, int>());

    [Fact]
    public void RegionWithoutOwnersIsRejected()
    {
        Assert.Throws<ArgumentException>(() =>
            new Region(1, 0, new Dictionary<byte, int>(), new Dictionary<BuildingType, int>(), new Dictionary<GoodType, int>()));
    }

    [Fact]
    public void CellsCountIsSumOfShares()
    {
        Assert.Equal(30, Split(10, 20).CellsCount);
    }

    [Fact]
    public void CellsOfUnknownCountryIsZero()
    {
        Assert.Equal(0, Split(10, 20).CellsOf(77));
    }

    [Fact]
    public void ShareIsFractional()
    {
        Assert.Equal(0.25f, Split(10, 30).ShareOf(1));
    }

    [Fact]
    public void LargestOwnerTakesTheBiggerHalf()
    {
        Assert.Equal(2, Split(10, 20).LargestOwner);
    }

    /// <summary>При равенстве ответ обязан быть всегда одним и тем же.</summary>
    [Fact]
    public void LargestOwnerBreaksTiesByBiggerId()
    {
        Assert.Equal(2, Split(10, 10).LargestOwner);
    }

    [Fact]
    public void IsSplitOnlyWhenOwnersDiffer()
    {
        Assert.False(Build.Region(1, 1).IsSplit);
        Assert.True(Split(10, 20).IsSplit);
    }

    [Fact]
    public void TransferMovesCellsAndKeepsTheTotal()
    {
        var region = Split(10, 20);

        region.TransferCells(2, 1, 5);

        Assert.Equal(15, region.CellsOf(1));
        Assert.Equal(15, region.CellsOf(2));
        Assert.Equal(30, region.CellsCount);
    }

    [Fact]
    public void TransferOfEverythingDropsTheOwner()
    {
        var region = Split(10, 20);

        region.TransferCells(1, 2, 10);

        Assert.Equal(0, region.CellsOf(1));
        Assert.False(region.IsSplit);
        Assert.Equal(30, region.CellsCount);
    }

    [Fact]
    public void TransferToSelfIsRejected()
    {
        Assert.Throws<ArgumentException>(() => Split(10, 20).TransferCells(1, 1, 5));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void TransferOfNothingOrBackwardsIsRejected(int count)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Split(10, 20).TransferCells(1, 2, count));
    }

    [Fact]
    public void TransferMoreThanOwnedIsRejected()
    {
        Assert.Throws<InvalidOperationException>(() => Split(10, 20).TransferCells(1, 2, 11));
    }

    [Fact]
    public void TransferFromEmptyOwnerIsRejected()
    {
        Assert.Throws<InvalidOperationException>(() => Split(10, 20).TransferCells(77, 1, 1));
    }

    [Fact]
    public void BuildingsAddUpAndComeBack()
    {
        var region = Build.Region(1, 1);

        region.AddBuildings(BuildingType.SteelMill, 3);
        region.AddBuildings(BuildingType.SteelMill, 2);

        Assert.Equal(5, region.BuildingsOf(BuildingType.SteelMill));
        Assert.Equal(0, region.BuildingsOf(BuildingType.Refinery));
    }

    [Fact]
    public void RemovingMoreThanThereIsFails()
    {
        var region = Build.Region(1, 1, new Dictionary<BuildingType, int> { [BuildingType.SteelMill] = 3 });

        Assert.False(region.TryRemoveBuildings(BuildingType.SteelMill, 4));
        Assert.Equal(3, region.BuildingsOf(BuildingType.SteelMill));

        Assert.True(region.TryRemoveBuildings(BuildingType.SteelMill, 3));
        Assert.Equal(0, region.BuildingsOf(BuildingType.SteelMill));
    }

    /// <summary>
    /// Главное свойство дележа: сколько бы ни было владельцев и как бы ни легло округление,
    /// сумма по странам равна общему числу построек.
    /// </summary>
    [Theory]
    [InlineData(1, 1, 7)]
    [InlineData(1, 2, 7)]
    [InlineData(10, 20, 7)]
    [InlineData(1, 99, 100)]
    [InlineData(33, 67, 3)]
    [InlineData(5, 5, 1)]
    public void SplitBuildingsSumToTheTotal(int first, int second, int buildings)
    {
        var region = Split(first, second, new Dictionary<BuildingType, int> { [BuildingType.SteelMill] = buildings });

        var mine = region.BuildingsOf(BuildingType.SteelMill, 1);
        var theirs = region.BuildingsOf(BuildingType.SteelMill, 2);

        Assert.Equal(buildings, mine + theirs);
    }

    [Fact]
    public void WholeRegionKeepsAllItsBuildings()
    {
        var region = Build.Region(1, 1, new Dictionary<BuildingType, int> { [BuildingType.SteelMill] = 7 });

        Assert.Equal(7, region.BuildingsOf(BuildingType.SteelMill, 1));
    }

    [Fact]
    public void CountryWithoutCellsGetsNoBuildings()
    {
        var region = Split(10, 20, new Dictionary<BuildingType, int> { [BuildingType.SteelMill] = 7 });

        Assert.Equal(0, region.BuildingsOf(BuildingType.SteelMill, 77));
    }

    [Fact]
    public void DepositsAnswerWhatCanBeMined()
    {
        var region = Build.Region(1, 1, deposits: new Dictionary<GoodType, int> { [GoodType.Oil] = 400 });

        Assert.Equal(400, region.DepositOf(GoodType.Oil));
        Assert.True(region.HasDeposit(GoodType.Oil));
        Assert.False(region.HasDeposit(GoodType.Coal));
    }
}
