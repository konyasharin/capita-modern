namespace CapitaModern.Core.Economy;

/// <summary>Одно вложение в резервах.</summary>
/// <param name="Issuer">Чья это валюта или бумага. От него зависит цена.</param>
/// <param name="Custodian">Где лежит. От него зависит, отнимут или нет.</param>
/// <remarks>
/// Место и эмитент — разные вещи, и путать их нельзя. Замораживает не тот, кто напечатал,
/// а тот, у кого лежит: золото Венесуэлы отняли в Банке Англии, хотя золото ничьё, а юани
/// в китайских банках не отнял никто. Своё хранилище — это <see cref="Custodian"/>, равный
/// самому владельцу, и тогда не отнять вовсе.
/// </remarks>
public readonly record struct Reserve(ReserveKind Kind, byte Issuer, byte Custodian, Money Amount);
