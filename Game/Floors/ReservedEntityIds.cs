namespace Game.Floors;

/// <summary>
/// Entity ids FloorBuilder mints up front (before PopulateFloor) for placeholders that outlive
/// any single trade -- reused for the life of the game, never destroyed/recreated -- rather than
/// scoped to the player's own identity (see FloorBuilder.ReservePlayerEntity, which stays a bare
/// int on World since Game-layer systems -- movement, targeting, IPlayerQuery -- read it
/// constantly). Nothing in the Game layer ever reads these two; they exist solely so Presentation
/// (MapViewState, InventoryGridContent, TradeWindowController) has a stable id to move stacks/
/// currency into and back out of for a trade in progress. See FloorBuilder.ReserveTradeOfferEntities
/// and PLAN-trade-window.md's own "Entity model" section.
/// </summary>
public sealed record ReservedEntityIds(int TradeOfferPlayerEntityId, int TradeOfferShopEntityId);
