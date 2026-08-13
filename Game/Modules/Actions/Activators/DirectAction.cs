using Engine.Math;

namespace Game.Modules.Actions.Activators;

/// <summary>
/// The simplest activator: no mana, no special mechanic -- applies its ActionDefinition's Effects
/// exactly as defined, with no modifications. Punch is a DirectAction using a DirectDamage.
/// </summary>
public sealed record DirectAction(TargetingSpec Targeting, ActionTiming Timing) : IActionActivator;
