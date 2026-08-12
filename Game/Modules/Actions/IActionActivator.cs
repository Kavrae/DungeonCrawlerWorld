using Engine.Math;

namespace Game.Modules.Actions;

/// <summary>
/// Pure trigger mechanism for an ActivatableDefinition (an ActionDefinition or an
/// Game.Modules.Inventory.ItemDefinition) -- when/how it activates, never what it does (see
/// ActivatableDefinition.Effects for that). SpellActivator, DirectAction, and
/// Game.Modules.Actions.Activators.PotionActivator are today's three concrete kinds; a future
/// ScrollActivator/WandActivator slots in the same way, with zero ActivatableDefinition changes.
/// </summary>
public interface IActionActivator
{
    TargetingSpec Targeting { get; }

    ActionTiming Timing { get; }
}
