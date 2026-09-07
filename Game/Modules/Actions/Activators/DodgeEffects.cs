using Engine.Utilities;
using Game.Modules.AbilityScores;

namespace Game.Modules.Actions.Activators;

/// <summary>Provides the Dexterity-scaled duration of Dodge's own protection window -- how long the dodging entity is immune to Dodgeable effects after confirming Dodge (see DodgingComponent's own doc comment), not a windup delay.</summary>
public static class DodgeEffects
{
    /// <summary>0.5s @ GameTiming.FramesPerSecond -- the window at Dexterity total 1, and ComputeWindowFrames' fallback when no AbilityScoreComponent is available.</summary>
    public const ushort WindowFrames = (ushort)(GameTiming.FramesPerSecond * 0.5f);

    /// <summary>1.0s @ GameTiming.FramesPerSecond -- the window at Dexterity total 300 (AbilityScoreMath's own clamp range).</summary>
    public const ushort MaxWindowFrames = GameTiming.FramesPerSecond * 1;

    /// <summary>Linear ramp from WindowFrames at Dexterity total 1 up to MaxWindowFrames at total 300 -- endpoints passed low-to-high since more Dexterity is the benefit here (a longer, more forgiving dodge), the inverse argument order from PotionCooldownEffects.ComputeDurationFrames, where more Constitution shortens its duration instead.</summary>
    public static ushort ComputeWindowFrames(ushort dexterityTotal) =>
        (ushort)AbilityScoreMath.Lerp(dexterityTotal, WindowFrames, MaxWindowFrames);
}
