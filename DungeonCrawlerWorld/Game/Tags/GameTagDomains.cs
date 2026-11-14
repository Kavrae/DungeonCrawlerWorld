using Engine.Tags;

namespace Game.Tags;

/// <summary>The roles a built-in tag field plays: every field that holds tags names one, and holds only tags it accepts.</summary>
/// <remarks>
/// Activation, Item category and Ability score are CoreModule's, over the roots it owns (GameTags); Area is AreasModule's
/// (AreaTags), Behavior NpcBehaviorModule's (NpcDrives). A field that conditions something on an activation (a stat modifier's condition, a disable rule's query)
/// takes Activation tags too: a condition can only name something an activation can carry, so it can always match.
/// </remarks>
public static class GameTagDomains
{
    /// <summary>What a use of something is: an action's or item's tags, the tags an aura or a terrain contact applies its effects with, and every condition on them.</summary>
    public static readonly GameplayTagDomain Activation = new("Activation");

    /// <summary>The categories an item falls in: inventory tabs and what a shop accepts.</summary>
    public static readonly GameplayTagDomain ItemCategory = new("Item category");

    /// <summary>The ability scores a use scales with (AbilityScoreTagBonus).</summary>
    public static readonly GameplayTagDomain AbilityScore = new("Ability score");

    /// <summary>What a map area is.</summary>
    public static readonly GameplayTagDomain Area = new("Area");

    /// <summary>What a behavior is and what a behavior modifier targets: NpcBehaviorModule's, over its Behavior root (NpcDrives).</summary>
    public static readonly GameplayTagDomain Behavior = new("Behavior");
}
