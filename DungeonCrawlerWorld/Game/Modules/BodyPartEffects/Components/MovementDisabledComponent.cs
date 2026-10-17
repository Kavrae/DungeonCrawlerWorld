namespace Game.Modules.BodyPartEffects.Components;

/// <summary>Marker: every Leg/Foot body part this entity has is simultaneously disabled -- MovementSystem refuses to move it outright rather than just applying an extreme MovementLockFrames multiplier. Granted/removed by BodyPartEffectsSystem as the underlying body parts change.</summary>
public readonly record struct MovementDisabledComponent;
