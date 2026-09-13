using CapitaModern.Core.Politics;
using Xunit;

namespace CapitaModern.Core.Tests;

/// <summary>Черты складываются, снимаются и задают курс страны.</summary>
public class CharacterTests
{
    private static readonly Trait Printer = new("печатный станок", "закрывает дыру станком",
        Prints: 120, Borrows: -40, Hoards: -30);

    private static readonly Trait Saver = new("копит на чёрный день", "держит резервы выше нормы",
        Hoards: 120, Borrows: -50, Prints: -40);

    [Fact]
    public void CharacterStartsOrdinary()
    {
        var mind = new Character();

        Assert.Equal(Character.Usual, mind.Prints);
        Assert.Empty(mind.Traits);
    }

    [Fact]
    public void TraitsAddUp()
    {
        var mind = new Character();
        mind.Take(Printer);
        mind.Take(Saver);

        // Сотня плюс сто двадцать минус сорок: страна и печатает, и копит — обе склонности сразу.
        Assert.Equal(180, mind.Prints);
        Assert.Equal(190, mind.Hoards);
        Assert.Equal(10, mind.Borrows);
        Assert.Equal(2, mind.Traits.Count);
    }

    /// <summary>Смена правительства снимает черту, и курс поворачивает.</summary>
    [Fact]
    public void DroppingATraitUndoesItExactly()
    {
        var mind = new Character();
        mind.Take(Printer);
        mind.Take(Saver);
        mind.Drop(Printer);

        Assert.Equal(Character.Usual - 40, mind.Prints);
        Assert.Equal(Character.Usual + 120, mind.Hoards);
        Assert.Single(mind.Traits);
    }

    [Fact]
    public void SameTraitTwiceChangesNothing()
    {
        var mind = new Character();
        mind.Take(Printer);
        mind.Take(Printer);

        Assert.Equal(220, mind.Prints);
        Assert.Single(mind.Traits);
    }

    /// <summary>Ниже нуля склонность не опускается: «вовсе никогда» — это ноль.</summary>
    [Fact]
    public void LeaningNeverGoesBelowZero()
    {
        var mind = new Character();
        mind.Take(new Trait("отшельник", "не занимает вовсе", Borrows: -500));

        Assert.Equal(0, mind.Borrows);
    }
}
