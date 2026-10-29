using CapitaModern.Core.Sim;
using CapitaModern.Core.World;

namespace CapitaModern.Core.Tests;

public class WorldTests
{
    [Fact]
    public void Regions_Belong_To_Countries_And_Have_Neighbours()
    {
        var world = Fixture.NewGame().World;

        Assert.NotEmpty(world.Regions);
        Assert.All(world.Regions, r => Assert.NotNull(world.CountryById[r.Owner]));
        Assert.True(world.Regions.Count(r => r.Neighbours.Length > 0) > world.Regions.Count() * 0.9);
    }

    [Fact]
    public void Biggest_Economies_Start_On_Top()
    {
        var world = Fixture.NewGame().World;
        var top = world.Countries.OrderByDescending(c => c.GdpReal).Take(3).Select(c => c.Iso).ToList();

        Assert.Contains("USA", top);
        Assert.Contains("CHN", top);
    }

    [Fact]
    public void Planned_Countries_Have_No_Companies()
    {
        var world = Fixture.NewGame().World;
        var prk = world.Countries.First(c => c.Iso == "PRK");

        Assert.Equal(EconomicSystem.Planned, prk.System);
        Assert.DoesNotContain(world.Regions.Where(r => r.Owner == prk.Id).SelectMany(r => r.Plants), p => p.Company != Company.State);
    }

    [Fact]
    public void A_Year_Runs_Without_Jumps()
    {
        var game = Fixture.NewGame();
        var world = game.World;

        // Первые полгода мир садится на свои запасы; дальше недельный ВВП мира не должен прыгать.
        for (var day = 0; day < 182; day++) game.Tick();

        var last = world.Countries.Sum(c => c.GdpReal);
        for (var day = 0; day < 182; day++)
        {
            game.Tick();
            if (world.Day % EconomyStep.WeekDays != 0) continue;

            var now = world.Countries.Sum(c => c.GdpReal);
            Assert.InRange(now / last, 0.98, 1.02);
            last = now;
        }

        Assert.All(world.Countries, c => Assert.InRange(c.PriceLevel, 0.9, 1.5));
    }
}
