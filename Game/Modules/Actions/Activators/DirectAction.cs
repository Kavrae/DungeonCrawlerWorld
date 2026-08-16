using Engine.Math;

namespace Game.Modules.Actions.Activators;

/// <summary>Represents a direct action that applies its effects immediately with no modification.</summary>
/// <param name="Targeting">The targeting specification for the action.</param>
/// <param name="Timing">The timing specification for the action.</param>
/// <cleanupVersion>1</cleanupVersion>
public sealed record DirectAction(TargetingSpec Targeting, ActionTiming Timing) : IActionActivator;
