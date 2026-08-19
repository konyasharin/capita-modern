namespace CapitaModern.Core.Loading;

/// <summary>
/// Справочник, разложенный по целочисленному индексу. Приведение ключа к индексу
/// делает наследник: там тип ключа известен, и не нужны ни боксинг, ни рефлексия.
/// </summary>
/// <remarks>
/// Ограничение <c>class</c> обязательно: у структуры массив заполнился бы нулями,
/// а не null, и обе проверки в конструкторе молча пропустили бы ошибку.
/// </remarks>
public abstract class Catalog<TInfo> where TInfo : class
{
    // Массив, а не словарь: индексатор дёргается в тике на каждый объект,
    // обращение по индексу дешевле хеширования.
    private readonly TInfo[] _byIndex;

    protected Catalog(
        IEnumerable<TInfo> infos,
        Func<TInfo, int> infoIntoIndex,
        int count
    ) {
        _byIndex = new TInfo[count];

        // Дубль и пропуск должны падать при загрузке с внятным текстом,
        // а не всплывать NullReferenceException посреди тика.
        foreach (var info in infos)
        {
            int index = infoIntoIndex(info);

            if (_byIndex[index] is not null)
                throw new InvalidDataException($"{index} был указан дважды");

            _byIndex[index] = info;
        }

        for (int i = 0; i < count; i++)
        {
            if (_byIndex[i] is null)
                throw new InvalidDataException($"{i} не был указан");
        }
    }

    protected TInfo Get(int index) => _byIndex[index];
}
