namespace CapitaModern.Core.Economy;

public enum GoodType
{
    // Ресурсы — добываются из земли
    Coal,
    Oil,
    Gas,
    IronOre,
    CopperOre,
    Bauxite,
    Uranium,
    RareEarth,
    Timber,
    Agriculture,

    // Переделы
    Electricity,
    Fuel,
    Metals,
    Chemicals,
    Materials,
    Electronics,

    // Конечное потребление
    Food,
    ConsumerGoods,
    Medicine,

    // Военная техника. Расходуемое тратится каждые сутки боя, долговременное теряется
    // только с потерями — от этого зависит, что важнее стране: склад или выпуск.
    Armour,
    Artillery,
    SmallArms,
    Ammunition,
    TacticalDrones,
    StrikeDrones,
    Missiles,
    Aircraft,
    AirDefence,
    ElectronicWarfare,
}
