using Game.World;

namespace Game.Modules.StatusEffects.Components;

/// <summary>
/// One stack unit of one effect from one source -- an entity holding N of these (in the
/// MultiComponentPool this is registered as) has N stacks. 
/// </summary>
public readonly record struct StatusEffectStack(StatusEffectType EffectType, StatusEffectSource Source);
