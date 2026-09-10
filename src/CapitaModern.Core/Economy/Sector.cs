namespace CapitaModern.Core.Economy;

/// <summary>Отрасль. По ней государство расставляет приоритеты снабжения, поэтому
/// делений ровно столько, сколько игрок готов различать.</summary>
public enum Sector
{
    Mining,
    Power,
    Heavy,
    Civil,
    Military,
    Services,
    People
}
