using Game.Modules.Actions.Components;

namespace Game.Modules.Actions;

/// <summary>Actions as an owner of toggles: an active toggle names an action its holder has, and switching it off is all there is to it.</summary>
/// <remarks>The ActiveToggleComponent is the whole on/off state of a toggle action, so nothing else has to change when one goes off. An entity's toggle actions end when it dies.</remarks>
internal sealed class ActionToggleOwner(EntityActions entityActions, Toggles toggles) : IToggleOwner
{
    public bool TryResolveDefinition(int holderEntityId, in ActiveToggleComponent toggle, out ActivatableDefinition definition)
    {
        var found = entityActions.TryGetEffectiveAction(holderEntityId, toggle.Owner.ActionId, out var action);
        definition = action;
        return found;
    }

    public void SwitchOff(int holderEntityId, in ActiveToggleComponent toggle, ActivatableDefinition definition, long now) =>
        toggles.TurnOff(holderEntityId, toggle.Key, definition, now);

    public bool EndsWhenHolderDies(ActivatableDefinition definition) => true;
}
