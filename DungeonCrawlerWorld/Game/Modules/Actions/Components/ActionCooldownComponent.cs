using Engine.ECS.Properties;

namespace Game.Modules.Actions.Components;

/// <summary>When one of an entity's actions can be used again.</summary>
/// <remarks>
/// Written the first time an action with a cooldown is actually used, and only then: an entity that
/// never used one holds nothing. Kept apart from the action itself because most actions come from a
/// blueprint definition and are shared by every entity of it (see ActionGrant), so there is no
/// per-entity component to write a deadline into -- and because a cooldown is the one part of an
/// action that really is per entity.
///
/// A deadline (FrameDeadline), not a countdown: nothing ticks it, it is compared against the current
/// frame by EntityActions. See ActionInstanceComponent's own doc comment for the countdown system
/// this replaced.
/// </remarks>
[DescribedComponent]
public partial struct ActionCooldownComponent(Guid actionId, uint readyAtFrame)
{
    [PropertyMeaning(GamePropertyMeanings.ActionId)]
    [ReadOnlyProperty]
    public Guid ActionId { get; init; } = actionId;

    /// <summary>The simulation frame from which the action can be used again.</summary>
    [PropertyMeaning(ComponentPropertyMeaningNames.Frame)]
    public uint ReadyAtFrame { get; set; } = readyAtFrame;
}
