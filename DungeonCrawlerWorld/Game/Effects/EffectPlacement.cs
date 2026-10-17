namespace Game.Effects;

/// <summary>Where an effect entry lands when an activation reaches several targets, or none.</summary>
public enum EffectPlacement : byte
{
    /// <summary>On every entity the activation reaches, once each.</summary>
    OnEachTarget,

    /// <summary>Once per activation: on the marked entity if there is one, otherwise at the centre tile with no target entity (EffectContext.TargetLocation).</summary>
    OncePerActivation,

    /// <summary>Once per activation at the centre tile, with no target entity, whatever is marked -- for something placed on the ground (a trap, a summon, a terrain change).</summary>
    AtLocation,
}
