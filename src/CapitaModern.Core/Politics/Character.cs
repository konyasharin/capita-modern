namespace CapitaModern.Core.Politics;

/// <summary>К чему страна склонна. Сотня — как все, двести — вдвое охотнее.</summary>
/// <remarks>
/// Из склонностей и складывается курс развития. Стране не нужно помнить, что она растёт по
/// шесть процентов в год: она растёт, потому что каждый тик решает одинаково — строит, а не
/// раздаёт, копит резервы, а не проедает их. Смена правительства меняет черты, и курс
/// поворачивает сам, без таблицы с заданным ростом.
///
/// Складывать сдвиги, а не заменять: у страны черт несколько, и «печатает деньги» вместе с
/// «строит армию» дают не одно и не другое, а обе склонности сразу.
/// </remarks>
public sealed class Character
{
    /// <summary>Обычная склонность, ×1.</summary>
    public const int Usual = 100;

    /// <summary>Печатать, а не занимать, когда в казне дыра.</summary>
    public int Prints { get; private set; } = Usual;

    /// <summary>Занимать за границей охотнее прочих.</summary>
    public int Borrows { get; private set; } = Usual;

    /// <summary>Тратить на армию сверх обычного.</summary>
    public int Arms { get; private set; } = Usual;

    /// <summary>Вкладывать в стройку, а не раздавать владельцам.</summary>
    public int Invests { get; private set; } = Usual;

    /// <summary>Копить резервы, а не проедать их.</summary>
    public int Hoards { get; private set; } = Usual;

    /// <summary>Кормить своих: людям достаётся дефицит прежде заводов.</summary>
    public int Feeds { get; private set; } = Usual;

    /// <summary>Держать рынок: вывозить даже в убыток, лишь бы не уступить.</summary>
    public int Holds { get; private set; } = Usual;

    /// <summary>Ставить на промышленность, а не на добычу и услуги.</summary>
    public int Builds { get; private set; } = Usual;

    /// <summary>Черты, из которых этот характер сложен. Для окна страны и для событий.</summary>
    public IReadOnlyList<string> Traits => _traits;

    private readonly List<string> _traits = [];

    /// <summary>Добавляет черту. Сдвиги складываются: −50 к обычной сотне даёт полтинник.</summary>
    public void Take(Trait trait)
    {
        if (_traits.Contains(trait.Name)) return;

        _traits.Add(trait.Name);

        Prints = Shift(Prints, trait.Prints);
        Borrows = Shift(Borrows, trait.Borrows);
        Arms = Shift(Arms, trait.Arms);
        Invests = Shift(Invests, trait.Invests);
        Hoards = Shift(Hoards, trait.Hoards);
        Feeds = Shift(Feeds, trait.Feeds);
        Holds = Shift(Holds, trait.Holds);
        Builds = Shift(Builds, trait.Builds);
    }

    /// <summary>Снимает черту — так курс и поворачивает при смене правительства.</summary>
    public void Drop(Trait trait)
    {
        if (!_traits.Remove(trait.Name)) return;

        Prints = Shift(Prints, -trait.Prints);
        Borrows = Shift(Borrows, -trait.Borrows);
        Arms = Shift(Arms, -trait.Arms);
        Invests = Shift(Invests, -trait.Invests);
        Hoards = Shift(Hoards, -trait.Hoards);
        Feeds = Shift(Feeds, -trait.Feeds);
        Holds = Shift(Holds, -trait.Holds);
        Builds = Shift(Builds, -trait.Builds);
    }

    /// <summary>Ниже нуля склонность не опускается: «вовсе никогда» — это ноль.</summary>
    private static int Shift(int was, int by) => Math.Max(0, was + by);
}
