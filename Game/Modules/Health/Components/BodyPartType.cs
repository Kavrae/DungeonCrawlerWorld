namespace Game.Modules.Health.Components;

/// <summary>Categorizes a BodyPartComponent instance.</summary>
/// <remarks>Minimal set for the Complex health path's first pass -- see the BodyPartType categorization follow-up (TODO.md) for the gameplay-effects-per-type pass this enum doesn't attempt yet.</remarks>
public enum BodyPartType : byte
{
    Head,
    Torso,
    Arm,
    Leg,
    Hand,
    Foot,
}
