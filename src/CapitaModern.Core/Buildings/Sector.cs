namespace CapitaModern.Core.Buildings;

/// <summary>Отрасль. По ней государство расставляет приоритеты снабжения, поэтому
/// делений ровно столько, сколько игрок готов различать.</summary>
public enum Sector
{
    Mining,
    Power,
    Heavy,
    Civil,
    Military
}
