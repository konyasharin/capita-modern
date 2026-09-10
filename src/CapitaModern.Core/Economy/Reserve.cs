namespace CapitaModern.Core.Economy;

/// <summary>Одно вложение в резервах: чьё оно и сколько его.</summary>
/// <remarks><see cref="Issuer"/> хранится ради заморозки: отнять можно только то, что
/// лежит у эмитента. Отсюда же и смысл держать золото с криптой — их не отнять.</remarks>
public readonly record struct Reserve(ReserveKind Kind, byte Issuer, Money Amount);
