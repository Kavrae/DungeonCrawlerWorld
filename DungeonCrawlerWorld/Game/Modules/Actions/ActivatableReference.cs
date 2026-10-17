namespace Game.Modules.Actions;

/// <summary>Which kind of thing an ActivatableReference names, and so which feature owns it.</summary>
public enum ActivatableKind : byte
{
    /// <summary>One of the entity's actions, named by action id.</summary>
    Action,

    /// <summary>An item stack the entity holds, named by stack instance id.</summary>
    Item,
}

/// <summary>Something an entity uses: one of its actions, or an item stack it holds.</summary>
/// <remarks>
/// What a windup resolves into (PendingWindupComponent) and what a toggle that is on belongs to
/// (ActiveToggleComponent). An action is named by its catalog id; an item by the stack holding it, since
/// a stack can diverge from its catalog definition. Turning a reference into its definition is the
/// owning feature's: the entity's effective action (EntityActions), or the stack's effective item.
/// </remarks>
/// <param name="ActionId">The action, for Kind Action; empty otherwise.</param>
/// <param name="StackInstanceId">The stack, for Kind Item; 0 otherwise.</param>
public readonly record struct ActivatableReference(ActivatableKind Kind, Guid ActionId, uint StackInstanceId)
{
    public static ActivatableReference Action(Guid actionId) => new(ActivatableKind.Action, actionId, 0);

    public static ActivatableReference ItemStack(uint stackInstanceId) => new(ActivatableKind.Item, Guid.Empty, stackInstanceId);

    public override string ToString() => Kind == ActivatableKind.Action ? $"Action {ActionId}" : $"Item stack {StackInstanceId}";
}
