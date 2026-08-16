using Engine.Math;

namespace Game.Modules.Actions.Activators;

/// <summary>Item-triggered activator for a one-time-use scroll.</summary>
/// <param name="Targeting">The targeting specification for the scroll.</param>
/// <param name="Timing">The timing specification for the scroll.</param>
/// <param name="SpellId">The ID of the spell this scroll represents.</param>
/// <cleanupVersion>1</cleanupVersion>
public sealed record ScrollActivator(TargetingSpec Targeting, ActionTiming Timing, Guid SpellId) : IActionActivator;
