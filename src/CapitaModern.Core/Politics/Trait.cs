namespace CapitaModern.Core.Politics;

/// <summary>Черта страны: чем её решения отличаются от обычных.</summary>
/// <param name="Name">Как черта зовётся. По имени её и снимают при смене курса.</param>
/// <remarks>
/// Сдвиги, а не готовые числа: черта говорит «на полсотни охотнее печатает», а не «печатает
/// сто пятьдесят». Оттого их и можно складывать, и тысяча непохожих стран выходит из
/// десятка черт.
///
/// Отрицательный сдвиг так же полезен, как и положительный: «кормит своих» заодно означает
/// «меньше вкладывает в стройку», и это не побочный вред, а суть выбора.
/// </remarks>
public readonly record struct Trait(
    string Name,
    string Tells,
    int Prints = 0,
    int Borrows = 0,
    int Arms = 0,
    int Invests = 0,
    int Hoards = 0,
    int Feeds = 0,
    int Holds = 0,
    int Builds = 0);
