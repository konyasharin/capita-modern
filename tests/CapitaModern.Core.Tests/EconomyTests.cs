using CapitaModern.Core.Sim;

namespace CapitaModern.Core.Tests;

public class EconomyTests
{
    [Fact]
    public void Mine_Gives_Less_When_Reserve_Runs_Out()
    {
        var world = Fixture.NewGame().World;
        var region = world.Regions.First(r => r.Reserve0.Count > 0);
        var good = region.Reserve0.Keys.First();
        var mine = world.Catalog.Buildings.First(b => b.Deposit == good);

        Assert.Equal(1, EconomyStep.Richness(region, mine));

        region.Reserve[good] = region.Reserve0[good] * 0.1;
        Assert.InRange(EconomyStep.Richness(region, mine), 0.3, 0.5);
    }

    [Fact]
    public void Poor_Countries_Grow_Productivity_Faster_Than_Rich()
    {
        var game = Fixture.NewGame();
        var world = game.World;
        for (var day = 0; day < 70; day++) game.Tick();

        var usa = world.Countries.First(c => c.Iso == "USA");
        var nga = world.Countries.First(c => c.Iso == "NGA");
        Assert.True(nga.ProductivityGrowth > usa.ProductivityGrowth + 0.01);
    }

    [Fact]
    public void Export_Subsidy_Wins_A_Bigger_Share()
    {
        var plain = Fixture.NewGame();
        var helped = Fixture.NewGame();
        var oil = plain.World.Catalog.GoodIndex("Oil");
        helped.World.Countries.First(c => c.Iso == "RUS").ExportSubsidy[oil] = 0.3;

        for (var day = 0; day < 182; day++)
        {
            plain.Tick();
            helped.Tick();
        }

        var before = plain.World.Countries.First(c => c.Iso == "RUS").ExportedWeek[oil];
        var after = helped.World.Countries.First(c => c.Iso == "RUS").ExportedWeek[oil];
        Assert.True(after > before * 1.1, $"вывоз нефти без субсидии {before:F0}, с ней {after:F0}");
    }
}
