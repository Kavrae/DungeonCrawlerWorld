using Engine.Math;
using Game.Modules.Actions;
using Game.Modules.Actions.Activators;
using Game.Modules.Actions.Effects;
using Game.Modules.StatusEffects;
using Microsoft.Xna.Framework;

namespace Game.Modules.Inventory.Definitions;

/// <summary>
/// First real WandActivator item, and the concrete proof case for per-slot item divergence (see
/// the Inventory system TODO entry) -- each wand's remaining Charges is per-physical-instance
/// state, tracked via InventoryActions.PeelOneIntoDivergentStack rather than a shared stack
/// Quantity. This catalog entry's own Charges/MaxCharges: 0 is a placeholder, always overwritten
/// at grant time -- never granted directly via plain InventoryActions.AddItem; see
/// Game.Modules.Inventory.WandGrantEffects.
/// </summary>
public static class WandOfFireball
{
    public static readonly Guid Id = new("7c3e9a1d-4b6f-4e2a-8d1c-000000000031");

    private const int Range = 10;
    private const int AreaSize = 3;
    private const short MinDamage = 25;
    private const short MaxDamage = 35;
    private const int BurningStacks = 5;

    public static ItemDefinition Build() => new(
        Id, "Wand of Fireball", "Wand", "w", Color.OrangeRed,
        Tags: [Tag.Ranged, Tag.Wand, Tag.Consumable, Tag.Fire],
        Effects: [new ActionEffect([new DirectDamage(MinDamage, MaxDamage), new StatusEffectGrant(StatusEffectType.Burning, StackCount: BurningStacks)])],
        Description: "A wand that hurls a bursting ball of fire, scorching everything caught in its blast and leaving them burning.",
        Summary: "Deals fire damage in a burst and inflicts Burning.",
        GoldValue: 20,
        Activator: new WandActivator(
            new TargetingSpec(Shape: TargetShape.Burst, Range: Range, AreaSize: AreaSize),
            new ActionTiming(ActionTimingCategory.Immediate, CooldownFrames: null),
            Charges: 0,
            MaxCharges: 0));
}
