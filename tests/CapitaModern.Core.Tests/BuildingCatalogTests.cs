using CapitaModern.Core.Buildings;
using CapitaModern.Core.Economy;
using Xunit;

namespace CapitaModern.Core.Tests;

public class BuildingCatalogTests
{
    [Fact]
    public void GivesBackWhatWasPutIn()
    {
        var info = Build.Info(BuildingType.SteelMill, outputs: new() { [GoodType.Metals] = Build.Units(4) });
        var catalog = Build.Catalog(info);

        Assert.Same(info, catalog[BuildingType.SteelMill]);
    }

    [Fact]
    public void MissingTypeIsCaughtOnLoad()
    {
        var all = Enum.GetValues<BuildingType>().Where(t => t != BuildingType.SteelMill).Select(t => Build.Info(t));

        var error = Assert.Throws<InvalidDataException>(() => _ = new BuildingCatalog(all));

        Assert.Contains(nameof(BuildingType.SteelMill), error.Message);
    }

    [Fact]
    public void DuplicateTypeIsCaughtOnLoad()
    {
        var all = Enum.GetValues<BuildingType>().Select(t => Build.Info(t)).ToList();
        all.Add(Build.Info(BuildingType.SteelMill));

        var error = Assert.Throws<InvalidDataException>(() => _ = new BuildingCatalog(all));

        Assert.Contains(nameof(BuildingType.SteelMill), error.Message);
    }

    /// <summary>Рецепты приезжают из файла единицами и должны стать сотыми.</summary>
    [Fact]
    public void RecipesComeFromJsonScaled()
    {
        var catalog = BuildingCatalog.FromJson(Json());

        Assert.Equal(Build.Units(13), catalog[BuildingType.SteelMill].Inputs[GoodType.IronOre]);
        Assert.Equal(Build.Units(4), catalog[BuildingType.SteelMill].Outputs[GoodType.Metals]);
        Assert.Equal(GoodType.Oil, catalog[BuildingType.OilRig].RequiresDeposit);
        Assert.Null(catalog[BuildingType.SteelMill].RequiresDeposit);
    }

    private static string Json()
    {
        var entries = Enum.GetValues<BuildingType>().Select(type => type switch
        {
            BuildingType.SteelMill =>
                """{"type":"SteelMill","inputs":{"IronOre":13},"outputs":{"Metals":4},"optimalWorkers":100,"requiresDeposit":null}""",
            BuildingType.OilRig =>
                """{"type":"OilRig","inputs":{},"outputs":{"Oil":30},"optimalWorkers":100,"requiresDeposit":"Oil"}""",
            _ => $$"""{"type":"{{type}}","inputs":{},"outputs":{},"optimalWorkers":1,"requiresDeposit":null}""",
        });

        return "[" + string.Join(",", entries) + "]";
    }
}
