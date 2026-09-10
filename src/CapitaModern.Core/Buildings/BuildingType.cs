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
    ComponentsPlant,
    ElectronicsPlant,
    MicroelectronicsPlant,

    // Конечная продукция
    FoodPlant,
    ConsumerGoodsPlant,
    PharmaPlant,
    ArmourPlant,
    ArtilleryPlant,
    SmallArmsPlant,
    AmmunitionPlant,
    TacticalDronePlant,
    StrikeDronePlant,
    MissilePlant,
    AircraftPlant,
    AirDefencePlant,
    ElectronicWarfarePlant,

    /// <summary>Сфера услуг: сырья не ест, только людей. Две трети мирового ВВП.</summary>
    ServiceFirm,
}
