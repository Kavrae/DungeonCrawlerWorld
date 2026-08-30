namespace Game.Modules.BodyPartEffects.Components;

/// <summary>Marker: every Arm/Hand body part this entity has is simultaneously disabled -- ActionActivationSystem refuses to activate a Tag.Melee action for it outright rather than just applying an extreme Tag.Melee-conditional OutgoingDamage multiplier. Granted/removed by BodyPartEffectsSystem as the underlying body parts change.</summary>
public readonly record struct MeleeDisabledComponent;
