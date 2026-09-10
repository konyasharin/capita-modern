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
    Components,
    Electronics,
    Microelectronics,

    // Конечное потребление
    Food,
    ConsumerGoods,
    Medicine,

    /// <summary>Две трети мирового ВВП. Не хранятся и не возятся: стрижку впрок не
    /// сделаешь и через границу не отправишь.</summary>
    Services,

    // Военная техника
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
