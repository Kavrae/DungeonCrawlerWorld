using Game.Modules.Containers.Components;
using Game.Modules.Core.Components;
using Game.Modules.Inventory.Components;
using Game.Modules.ItemPiles.Components;
using Microsoft.Xna.Framework;

namespace Game.Blueprints.Objects;

/// <summary>Items and currency put down on a tile.</summary>
/// <remarks>
/// A non-blocking container with no health: it opens in the loot window as a chest does, nothing can destroy it, and it holds
/// any number of stacks. Spawned empty by ItemDrops, which fills it, and destroyed once it holds nothing (ItemPileEndings).
/// </remarks>
public static class ItemPile
{
    public static readonly Guid Id = new("d9f6a1c4-8b2e-4f3a-9c1d-000000000127");

    public const string Name = "Item Pile";

    private const string Description = "Things someone put down here.";

    public static readonly BlueprintDefinition Definition = new(Id, Name)
    {
        Build = Build,
        NonBlocking = NonBlockingKind.None,
        Appearance = new() { Name = Name, Description = Description, Glyph = "%", GlyphColor = Color.SandyBrown, SpriteName = "LootBag-Red" },
    };

    private static void Build(BlueprintContext context)
    {
        var componentManager = context.ComponentManager;
        var entityId = context.EntityId;

        componentManager.Merge(entityId, new ItemPileComponent());
        componentManager.Merge(entityId, new ContainerComponent());
        componentManager.Merge(entityId, new UnlimitedStackCountComponent());
    }
}
