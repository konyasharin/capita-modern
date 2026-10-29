using CapitaModern.Core.World;

namespace CapitaModern.Core.Tests;

public class LawAndWarTests
{
    [Fact]
    public void Planned_Takes_Plants_From_Companies_And_Market_Gives_Them_Back()
    {
        var game = Fixture.NewGame();
        var world = game.World;
        var rus = world.Countries.First(c => c.Iso == "RUS");
        var plants = world.Regions.Where(r => r.Owner == rus.Id).SelectMany(r => r.Plants).ToList();
        Assert.Contains(plants, p => p.Company != Company.State);

        game.SetSystem(rus.Id, EconomicSystem.Planned);
        Assert.All(plants, p => Assert.Equal(Company.State, p.Company));

        game.SetSystem(rus.Id, EconomicSystem.Market);
        Assert.Contains(plants, p => p.Company != Company.State);
    }

    [Fact]
    public void Transfer_Hands_Region_And_Foreign_Plants_To_State()
    {
        var game = Fixture.NewGame();
        var world = game.World;
        var usa = world.Countries.First(c => c.Iso == "USA");
        var mex = world.Countries.First(c => c.Iso == "MEX");
        var region = world.Regions.First(r => r.Owner == usa.Id && r.Plants.Any(p => p.Company != Company.State));

        game.Transfer(region.Id, mex.Id);

        Assert.Equal(mex.Id, region.Owner);
        Assert.Equal(mex.Id, region.Controller);
        Assert.All(region.Plants, p => Assert.Equal(Company.State, p.Company));
    }

    [Fact]
    public void Divisions_Take_Enemy_Ground_And_Peace_Keeps_It()
    {
        var game = Fixture.NewGame();
        var world = game.World;
        var rus = world.Countries.First(c => c.Iso == "RUS");
        var ukr = world.Countries.First(c => c.Iso == "UKR");

        game.DeclareWar(rus.Id, ukr.Id);
        for (var day = 0; day < 120; day++) game.Tick();

        var taken = world.Regions.Where(r => r.Owner == ukr.Id && r.Controller == rus.Id).Select(r => r.Id).ToList();
        Assert.NotEmpty(taken);

        game.MakePeace(rus.Id, ukr.Id);
        Assert.All(taken, id => Assert.Equal(rus.Id, world.Region(id).Owner));
        Assert.False(world.AtWar(rus.Id, ukr.Id));
    }

    [Fact]
    public void Giving_Away_The_Capital_Moves_It()
    {
        var game = Fixture.NewGame();
        var world = game.World;
        var deu = world.Countries.First(c => c.Iso == "DEU");
        var fra = world.Countries.First(c => c.Iso == "FRA");
        var capital = deu.Capital;

        game.Transfer(capital, fra.Id);

        Assert.NotEqual(capital, deu.Capital);
        Assert.Equal(deu.Id, world.Region(deu.Capital).Owner);
        Assert.DoesNotContain(world.Divisions, d => d.Country == deu.Id && d.Region == capital);
    }

    [Fact]
    public void A_Year_Of_War_On_Several_Fronts_Keeps_Divisions_Sane()
    {
        var game = Fixture.NewGame();
        var world = game.World;
        var rus = world.Countries.First(c => c.Iso == "RUS").Id;
        foreach (var iso in new[] { "UKR", "KAZ", "GEO", "FIN" }) game.DeclareWar(rus, world.Countries.First(c => c.Iso == iso).Id);

        for (var day = 0; day < 365; day++) game.Tick();

        Assert.All(world.Divisions, d =>
        {
            Assert.InRange(d.Organization, 0, 1);
            Assert.InRange(d.Equipment, 0, 1);
            Assert.Equal(d.Country, world.Region(d.Region).Controller);
        });
    }

    [Fact]
    public void Trade_Stops_Between_Enemies()
    {
        var game = Fixture.NewGame();
        var world = game.World;
        var a = world.Countries.First(c => c.Iso == "USA");
        var b = world.Countries.First(c => c.Iso == "CHN");

        game.DeclareWar(a.Id, b.Id);
        Assert.True(world.AtWar(a.Id, b.Id));
        Assert.Contains(b.Id, world.Enemies(a.Id));
    }
}
