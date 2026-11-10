namespace Game.Modules.Shops;

/// <summary>Published once for every completed trade with a shop: a shop deal (ShopCommands.TryCompleteDeal) or Gold given to one outright (ShopCommands.TryGiveCurrencyToShop).</summary>
/// <param name="PlayerOfferValue">What the player's side was worth to the shop: its stacks at the shop's price plus its Gold.</param>
/// <param name="ShopOfferValue">What the shop's side was worth: its stacks at the shop's price plus its Gold.</param>
/// <param name="PlayerOfferGold">The Gold the player's side gave the shop.</param>
/// <param name="ShopOfferIsEmpty">Whether the player got nothing back: the shop's side held no stacks and no Gold.</param>
public readonly record struct ShopDealCompletedEvent(int PlayerEntityId, int ShopEntityId, int PlayerOfferValue, int ShopOfferValue, int PlayerOfferGold, bool ShopOfferIsEmpty);
