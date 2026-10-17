namespace Game.Modules.Shops;

/// <summary>Published whenever a player successfully gives Gold to a shop (see CurrencyRowContent's own Give path, the only currency direction a shop accepts -- a player can never Take from one) -- the "Angel Investor" achievement's own trigger.</summary>
public readonly record struct GoldGivenToShopEvent(int PlayerEntityId, int ShopEntityId, int Amount);
