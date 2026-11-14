using Game.Modules.NpcBehavior.Behaviors;
using Game.Modules.NpcBehavior.Considerations;

namespace Game.Blueprints.Races;

/// <summary>The decisions every race makes, to start with: one list each race grants, with its own weights where they differ.</summary>
/// <remarks>
/// Ranks keep the order a creature cares in: heal (4), then fight or flee (3), then loot (2), then wander or idle (1). A
/// behavior a creature has nothing to do with costs it only a failed check, reached rarely -- a race with no ranged attack
/// never finds one to use. Every race is a race like the others, Human included: the player is never decided for by its
/// entity, not by its race. A race whose weights differ builds its own list from these grants (GoblinGrants).
/// </remarks>
public static class StandardBehaviors
{
    /// <summary>The share of its maximum health below which a creature heals itself.</summary>
    public const float SelfHealBelowHealth = 0.5f;

    /// <summary>Flee's baseline panic: what it weighs at full health, before missing health adds to it.</summary>
    public const float FleeBaselinePanic = 0.1f;

    /// <summary>How long a creature that starts to flee keeps fleeing from the same hostile, unless something ranked higher interrupts it or no hostile is in sight.</summary>
    public const ushort FleeCommitFrames = 3 * GameFrameRate.FramesPerSecond;

    /// <summary>How far a creature counts its allies, for pack courage and pack fear.</summary>
    public const byte PackRadius = 8;

    /// <summary>The most allies pack courage and pack fear count.</summary>
    public const byte PackMaximumAllies = 8;

    public static NpcBehaviorGrant SelfHeal { get; } = new(BuiltInBehaviorIds.SelfHeal, Rank: 4, Parameter: SelfHealBelowHealth);

    public static NpcBehaviorGrant EngageMelee { get; } = new(BuiltInBehaviorIds.EngageMelee, Rank: 3);

    public static NpcBehaviorGrant RangedAttack { get; } = new(BuiltInBehaviorIds.RangedAttack, Rank: 3);

    public static NpcBehaviorGrant Flee { get; } = new(BuiltInBehaviorIds.Flee, Rank: 3, Parameter: FleeBaselinePanic, CommitFrames: FleeCommitFrames);

    public static NpcBehaviorGrant LootNearby { get; } = new(BuiltInBehaviorIds.LootNearby, Rank: 2);

    public static NpcBehaviorGrant WanderStep { get; } = new(BuiltInBehaviorIds.WanderStep, Rank: 1);

    public static NpcBehaviorGrant Idle { get; } = new(BuiltInBehaviorIds.Idle, Rank: 1);

    public static IReadOnlyList<NpcBehaviorGrant> Grants { get; } = [SelfHeal, EngageMelee, RangedAttack, Flee, LootNearby, WanderStep, Idle];

    /// <summary>Goblins: cowardly alone, brave in numbers -- quick to flee, with every ally within PackRadius making them likelier to fight and less likely to run.</summary>
    public static IReadOnlyList<NpcBehaviorGrant> GoblinGrants { get; } =
    [
        SelfHeal,
        EngageMelee with { Considerations = [new NearbyAlliesFactor(PackRadius, PerAlly: 0.25f, PackMaximumAllies, Inverse: false)] },
        RangedAttack,
        Flee with { Weight = 3f, Considerations = [new NearbyAlliesFactor(PackRadius, PerAlly: 0.5f, PackMaximumAllies, Inverse: true)] },
        LootNearby,
        WanderStep,
        Idle,
    ];
}
