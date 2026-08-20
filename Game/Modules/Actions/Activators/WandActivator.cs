using Engine.Math;

namespace Game.Modules.Actions.Activators;

/// <summary>
/// Item-triggered activator for a wand -- immediate, no mana cost (Potion/Scroll already never
/// cost mana; "does not use mana" falls out for free here by simply having no mana field at all),
/// and works with any TargetingSpec (nothing here is shape-specific). Unlike PotionActivator/
/// ScrollActivator, a wand isn't consumed from a shared stack of identical units on use -- each
/// physical wand carries its own remaining Charges, ticking down independently of any other stack
/// of the same item id (see the per-slot item divergence work, InventoryActions.
/// PeelOneIntoDivergentStack). MaxCharges is fixed once, at grant time, off the recipient's
/// Intelligence (see WandActivationEffects/Game.Modules.Inventory.WandGrantEffects) -- never
/// recomputed later, even if Intelligence changes afterward.
/// </summary>
/// <param name="Targeting">The targeting specification for the wand.</param>
/// <param name="Timing">The timing specification for the wand.</param>
/// <param name="Charges">Uses remaining on this specific wand.</param>
/// <param name="MaxCharges">The charge count this wand started with when granted.</param>
public sealed record WandActivator(TargetingSpec Targeting, ActionTiming Timing, ushort Charges, ushort MaxCharges) : IActionActivator;
