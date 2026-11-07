using Engine.Content;
using Engine.ECS.Components;
using Engine.Tags;
using Engine.Tiles;
using Game.Effects;
using Game.Modules.Actions;
using Game.Modules.Actions.Activators;
using Game.Modules.Actions.Effects;
using Game.Tags;
using Microsoft.Xna.Framework;

namespace Game.Modules.Inventory;

/// <summary>Makes tomes: one-use items that teach their reader one spell for good.</summary>
/// <remarks>
/// A tome is made when one is wanted, from any spell -- one the content holds or one made at runtime from existing
/// effects -- and never registered in the ItemCatalog: its definition is the Override of the stack holding it, so it
/// travels with the stack through every trade, loot and sale and is gone once the last unit is read. Its item Guid is
/// derived from the spell's, so two tomes of one spell are one item and share a stack. Its text names the spell and
/// carries the spell's description, and nothing else of it.
/// </remarks>
public static class Tomes
{
    public const int GoldValue = 250;

    public const int MaximumShopStock = 5;

    public const string SpriteName = "Tome";

    public const string Glyph = "B";

    public static readonly Color GlyphColor = new(170, 120, 255);

    private static readonly Guid TomeItemIdNamespace = new("a38a4647-1578-4ef4-ba46-c0beab3ca11e");

    private static readonly TargetingSpec ReaderTargeting = new(Shape: TargetShape.Self, MaximumRange: 0, AreaSize: 0, Anchor: TargetAnchor.Caster);

    private static readonly ConsumableActivator TomeActivator = new(ReaderTargeting, new ActionTiming(ActionTimingCategory.Immediate));

    private static readonly GameplayTagSet TomeTags = [GameTags.ItemConsumableTome, GameTags.Magic];

    /// <summary>The item Guid of the tome holding the spell spellId.</summary>
    public static Guid ItemIdFor(Guid spellId) => ContentGuid.Derive(TomeItemIdNamespace, spellId.ToByteArray());

    /// <summary>The tome that teaches spell.</summary>
    public static ItemDefinition Create(ActionDefinition spell) => new(
        ItemIdFor(spell.Id),
        $"Tome of {spell.Name}",
        SpriteName,
        Glyph,
        GlyphColor,
        TomeTags,
        [new Effect([new SpellGrant(spell)])],
        Description: $"A heavy tome that teaches its reader {spell.Name}, then crumbles to dust. {spell.Name}: {spell.Description}",
        Summary: $"Teaches {spell.Name}.",
        Activator: TomeActivator,
        GoldValue: GoldValue,
        MaximumShopStock: MaximumShopStock);

    /// <summary>Adds quantity tomes of spell to entityId's inventory.</summary>
    /// <returns>The StackInstanceId of the stack the last tome landed in.</returns>
    public static uint Grant(ComponentManager componentManager, int entityId, ActionDefinition spell, ushort quantity = 1) =>
        InventoryActions.AddItemWithOverride(componentManager, entityId, Create(spell), quantity);
}
