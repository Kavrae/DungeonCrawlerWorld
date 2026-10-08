using Engine.ECS.Components.Stores;
using Engine.ECS.Systems;
using Engine.Math;
using Game.Modules.Core.Components;
using Game.Effects;
using Game.Modules.Actions.Components;

namespace Game.Modules.Actions;

/// <summary>What a feature that owns a kind of toggle (an action, an item) tells Toggles about its own.</summary>
/// <remarks>Registered with Toggles.RegisterOwner in the feature's RegisterBehavior, so whatever ends a toggle without the holder asking -- its periodic effects failing, its holder dying -- can do it without knowing what the toggle is.</remarks>
public interface IToggleOwner
{
    /// <summary>The definition of holderEntityId's active toggle; false when it can no longer be resolved.</summary>
    bool TryResolveDefinition(int holderEntityId, in ActiveToggleComponent toggle, out ActivatableDefinition definition);

    /// <summary>Switches holderEntityId's active toggle off, with no timing and nothing taken: everything the owner keeps about it being on goes with it.</summary>
    void SwitchOff(int holderEntityId, in ActiveToggleComponent toggle, ActivatableDefinition definition, long now);

    /// <summary>Whether a toggle with this definition is switched off when its holder dies.</summary>
    bool EndsWhenHolderDies(ActivatableDefinition definition);
}

/// <summary>Switches an entity's toggles on and off: the one place a toggle's held effects are applied and taken back.</summary>
/// <remarks>
/// Each toggle that is on is an ActiveToggleComponent with a key of its own, and its definition's
/// Effects are applied under that key and reverted under it, so any number of toggles with the same
/// effect can be on at once on one entity and each ends only what it granted. Neither TurnOn nor
/// TurnOff applies activation effects (ActivationEffectsApplier), checks a lock or changes an item's
/// lit state: whatever decides a toggle goes on or off has done that, and calls here for the holder's side.
/// </remarks>
public sealed class Toggles(MultiComponentPool<ActiveToggleComponent> activeToggles, EffectServices effectServices)
{
    private readonly IToggleOwner?[] _owners = new IToggleOwner?[Enum.GetValues<ActivatableKind>().Length];

    private uint _lastKey;

    /// <summary>Registers the feature that owns toggles of this kind. One owner per kind.</summary>
    public void RegisterOwner(ActivatableKind kind, IToggleOwner owner)
    {
        if (_owners[(int)kind] is not null)
        {
            throw new InvalidOperationException($"Toggles of kind {kind} already have an owner.");
        }

        _owners[(int)kind] = owner;
    }

    /// <summary>The feature that owns toggles of this kind; false when the build has none.</summary>
    public bool TryGetOwner(ActivatableKind kind, out IToggleOwner owner)
    {
        owner = _owners[(int)kind]!;
        return owner is not null;
    }

    /// <summary>Switches a toggle on for holderEntityId and applies definition's held effects under its new key, which is returned.</summary>
    /// <remarks>
    /// The first periodic effects are due one interval later. On a holder that is already dead they
    /// are due at once instead, so a toggle that ends with its holder is ended by its first tick.
    /// </remarks>
    /// <param name="definition">The toggle's definition; its Effects are what is held.</param>
    /// <param name="now">The simulation frame this happens on.</param>
    /// <param name="placesAtHolderTile">For a toggle switched on in Ground mode: its entries placed once per activation go to the tile the holder stands on now (an aura it holds is anchored there), not on the holder.</param>
    public uint TurnOn(int holderEntityId, ActivatableReference owner, ActivatableDefinition definition, long now, bool placesAtHolderTile = false)
    {
        var key = ++_lastKey;
        var nextTickFrame = definition.Toggle?.Periodic is not { } periodic
            ? FrameDeadline.Never
            : effectServices.DeadEntities.Has(holderEntityId) ? FrameDeadline.After(now, 0) : FrameDeadline.After(now, periodic.IntervalFrames);

        activeToggles.Add(holderEntityId, new ActiveToggleComponent(key, owner, nextTickFrame));

        var context = HeldContext(holderEntityId, key, definition, now);
        EffectSequence.ApplyOnEachTarget(definition.Effects, in context);
        EffectSequence.ApplyOnce(definition.Effects, in context, placesAtHolderTile ? null : holderEntityId, HolderTile(holderEntityId));
        return key;
    }

    /// <summary>The tile holderEntityId stands on, for an entry placed at a location that nothing aimed: the holder's own.</summary>
    private Vector3Int HolderTile(int holderEntityId) =>
        effectServices.ComponentManager.GetDirectPool<TransformComponent>().TryGetReadonly(holderEntityId, out var transform) ? transform.Position : default;

    /// <summary>Switches off holderEntityId's toggle with this key: reverts definition's held effects under the key and removes the toggle. Returns false, changing nothing, when the holder has no such toggle.</summary>
    /// <param name="definition">The toggle's definition, the same one it was switched on with.</param>
    public bool TurnOff(int holderEntityId, uint key, ActivatableDefinition definition, long now)
    {
        if (!activeToggles.RemoveFirst(holderEntityId, key, static (ref readonly ActiveToggleComponent toggle, uint key) => toggle.Key == key))
        {
            return false;
        }

        EffectSequence.Revert(definition.Effects, HeldContext(holderEntityId, key, definition, now));
        return true;
    }

    /// <summary>Asks the feature owning holderEntityId's toggle with this key to switch it off, as if it had ended by itself (its upkeep failing). Does nothing when the holder has no such toggle or its definition can't be resolved.</summary>
    /// <remarks>For something the toggle holds ending without the toggle asking: an aura anchor it placed being destroyed.</remarks>
    public void SwitchOff(int holderEntityId, uint key, long now)
    {
        if (!activeToggles.TryGetFirst(holderEntityId, key, static (ref readonly ActiveToggleComponent toggle, uint key) => toggle.Key == key, out var toggle) ||
            !TryGetOwner(toggle.Owner.Kind, out var owner) ||
            !owner.TryResolveDefinition(holderEntityId, in toggle, out var definition))
        {
            return;
        }

        owner.SwitchOff(holderEntityId, in toggle, definition, now);
    }

    /// <summary>How many toggles belonging to owner holderEntityId has on.</summary>
    public int CountOn(int holderEntityId, ActivatableReference owner) =>
        activeToggles.CountMatching(holderEntityId, owner, static (ref readonly ActiveToggleComponent toggle, ActivatableReference owner) => toggle.Owner == owner);

    /// <summary>Whether holderEntityId has a toggle belonging to owner on.</summary>
    public bool IsOn(int holderEntityId, ActivatableReference owner) => TryGetKey(holderEntityId, owner, out _);

    /// <summary>The key of one of holderEntityId's toggles belonging to owner that is on; false when it has none.</summary>
    public bool TryGetKey(int holderEntityId, ActivatableReference owner, out uint key)
    {
        var found = activeToggles.TryGetFirst(holderEntityId, owner, static (ref readonly ActiveToggleComponent toggle, ActivatableReference owner) => toggle.Owner == owner, out var toggle);
        key = toggle.Key;
        return found;
    }

    /// <summary>Applies periodic's effects to holderEntityId if every one of them can be applied, and none otherwise. Returns whether they were applied.</summary>
    public bool TryApplyPeriodicEffects(int holderEntityId, ActivatableDefinition definition, TogglePeriodicEffects periodic, long now)
    {
        var context = HolderContext(holderEntityId, definition, now);
        if (EffectSequence.CanApply(periodic.Effects, in context) != EffectRefusal.None)
        {
            return false;
        }

        EffectSequence.Apply(periodic.Effects, in context);
        return true;
    }

    /// <summary>Source and target both the holder: a toggle applies to whoever has it on.</summary>
    private EffectContext HolderContext(int holderEntityId, ActivatableDefinition definition, long now) =>
        EffectContext.FromEntity(effectServices, holderEntityId, holderEntityId, definition.Name, definition.Tags, now);

    private EffectContext HeldContext(int holderEntityId, uint key, ActivatableDefinition definition, long now) =>
        HolderContext(holderEntityId, definition, now) with { HeldGrantKey = key };
}
