namespace CapitaModern.Core.Buildings;

public enum BuildingType
{
    // Добыча — требует месторождения в регионе, см. data/economy/buildings.json
    CoalMine,
    OilRig,
    GasField,
    IronMine,
    CopperMine,
    BauxiteMine,
    UraniumMine,
    RareEarthMine,
    LoggingCamp,
    Farm,

    // Энергетика
    CoalPlant,
    GasPlant,
    NuclearPlant,
    HydroPlant,

    // Переделы
    Refinery,
    SteelMill,
    Smelter,
    ChemicalPlant,
    MaterialsPlant,
    ElectronicsPlant,

    // Конечная продукция
    FoodPlant,
    ConsumerGoodsPlant,
    PharmaPlant,
    ArmsFactory,
}
