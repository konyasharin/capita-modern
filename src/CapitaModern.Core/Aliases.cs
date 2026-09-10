// Имена для дробных величин: в коде читается GoodAmount, а не Fixed<Goods>.
// Типы при этом остаются разными, перепутать товары с людьми компилятор не даст.

global using GoodAmount = CapitaModern.Core.Economy.Fixed<CapitaModern.Core.Economy.Goods>;
global using Population = CapitaModern.Core.Economy.Fixed<CapitaModern.Core.Economy.People>;
global using Price = CapitaModern.Core.Economy.Fixed<CapitaModern.Core.Economy.Money>;
