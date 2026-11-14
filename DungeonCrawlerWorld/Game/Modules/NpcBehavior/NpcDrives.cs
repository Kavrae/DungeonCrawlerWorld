using Engine.Tags;
using Game.Modules.StatModifiers;

namespace Game.Modules.NpcBehavior;

/// <summary>The Behavior tags, declared by NpcBehaviorModule: the root, and the drives a behavior serves.</summary>
/// <remarks>
/// A drive is what a modifier or a runtime multiplier scales (a Boss's Aggression, the Dread Aura's Caution), so every
/// behavior serving it is scaled alike, a mod's included. Behavior.Drive is open to other modules, so a mod can add a drive
/// of its own. Every behavior's tags and every modifier's tag are Behavior tags (GameTagDomains.Behavior).
/// </remarks>
public static class NpcDrives
{
    public static readonly GameplayTag Behavior = GameplayTag.Get("Behavior");

    public static readonly GameplayTag Drive = GameplayTag.Get("Behavior.Drive");

    /// <summary>Fighting: engaging in melee, attacking from range.</summary>
    public static readonly GameplayTag Aggression = GameplayTag.Get("Behavior.Drive.Aggression");

    /// <summary>Self-preservation: healing, running away.</summary>
    public static readonly GameplayTag Caution = GameplayTag.Get("Behavior.Drive.Caution");

    /// <summary>Each drive with the stat modifier target that scales it while the game runs (NpcDecisionContext.RuntimeWeightMultiplier).</summary>
    /// <remarks>A behavior serving several drives is scaled by each. Any effect can grant one of these targets -- an aura, an item, an action -- timed or permanent; the decision framework knows nothing of them.</remarks>
    public static readonly (GameplayTag Drive, StatModifierTarget Target)[] RuntimeTargets =
    [
        (Aggression, StatModifierTarget.BehaviorAggression),
        (Caution, StatModifierTarget.BehaviorCaution),
    ];
}
