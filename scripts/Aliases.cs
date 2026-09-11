// Те же имена дробных величин, что и в ядре: в интерфейсе читается Money, а не
// Fixed<Cash>. Файл нужен потому, что глобальные using из ядра сюда не переезжают.

global using GoodAmount = CapitaModern.Core.Economy.Fixed<CapitaModern.Core.Economy.Goods>;
global using Population = CapitaModern.Core.Economy.Fixed<CapitaModern.Core.Economy.People>;
global using Money = CapitaModern.Core.Economy.Fixed<CapitaModern.Core.Economy.Cash>;
