namespace CapitaModern.Core.Politics;

/// <summary>Куда страна тяготеет. Грубо, зато считается и меняется одним полем.</summary>
/// <remarks>Настоящая мера близости — совпадение голосований в ООН; блоки её огрубляют
/// до четырёх групп, чтобы не хранить сорок тысяч пар.</remarks>
public enum Bloc
{
    /// <summary>Ни с кем особо. Большая часть Африки, Латинской Америки, Южной Азии.</summary>
    NonAligned,

    West,
    China,
    Russia,
}
