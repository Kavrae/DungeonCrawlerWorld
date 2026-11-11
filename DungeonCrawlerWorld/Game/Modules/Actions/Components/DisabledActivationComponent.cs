using Engine.ECS.Components;
using Engine.Tags;

namespace Game.Modules.Actions.Components;

/// <summary>One rule disabling the actions and items whose tags match Query for the entity holding it, until ExpiresAtFrame.</summary>
/// <remarks>
/// Disabled is the user's, not each action's or item's, so one rule covers an action and an item alike (DisabledActivations).
/// Query is the granting entry's own shared instance, so a second grant from the same entry refreshes this rule rather than
/// adding another. A scheduled timer, removed when it ends (DisabledActivationSystem). Multi pool: several rules at once.
/// </remarks>
public struct DisabledActivationComponent(GameplayTagQuery query, uint expiresAtFrame, int instanceKey) : IKeyedScheduledTimer
{
    /// <summary>Which actions and items this rule disables, by their tags.</summary>
    public GameplayTagQuery Query { get; } = query;

    /// <summary>The frame this rule ends.</summary>
    public uint ExpiresAtFrame { get; set; } = expiresAtFrame;

    /// <summary>A key of its own, telling this rule apart from the entity's others.</summary>
    public int InstanceKey { get; } = instanceKey;

    private uint _timerWheelMark;

    readonly int IKeyedScheduledTimer.TimerKey => InstanceKey;

    uint IScheduledTimer.NextTickFrame { readonly get => ExpiresAtFrame; set => ExpiresAtFrame = value; }

    uint IScheduledTimer.TimerWheelMark { readonly get => _timerWheelMark; set => _timerWheelMark = value; }

    public override readonly string ToString() => $"Query : {Query}\nExpiresAtFrame : {ExpiresAtFrame}";
}
