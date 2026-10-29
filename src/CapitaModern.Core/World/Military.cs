namespace CapitaModern.Core.World;

/// <param name="Strength">Сила полной дивизии: стоимость штатной техники, отнесённая к мотострелковой.</param>
public sealed record DivisionTemplate(
    int Index,
    string Type,
    string Name,
    bool TakesGround,
    int Manpower,
    (int Good, double Amount)[] Equipment,
    (int Good, double Amount)[] Upkeep,
    double Strength);

public sealed class Division
{
    public required int Id { get; init; }
    public required int Country { get; set; }
    public required int Template { get; init; }
    public required int Region { get; set; }

    /// <summary>Боеготовность 0..1: падает в бою и без снабжения, восстанавливается в тылу.</summary>
    public double Organization { get; set; } = 1.0;

    /// <summary>Укомплектованность техникой 0..1.</summary>
    public double Equipment { get; set; } = 1.0;

    /// <summary>Куда идёт: соседний регион, -1 — стоит.</summary>
    public int Target { get; set; } = -1;

    public double Progress { get; set; }
    public double TravelDays { get; set; }

    /// <summary>Дальняя цель приказа: дивизия идёт к ней шаг за шагом.</summary>
    public int Goal { get; set; } = -1;

    public bool InBattle { get; set; }
    public bool Alive { get; set; } = true;

    public double Strength(DivisionTemplate template) => template.Strength * Equipment * Organization;
}
