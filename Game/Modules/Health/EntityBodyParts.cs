using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Engine.ECS.Systems;
using Game.Modules.Health.Components;
using Game.Modules.Race.Components;
using Game.Blueprints;

namespace Game.Modules.Health;

/// <summary>One body part of one entity, as EntityBodyParts hands it out: its race's template plus whatever has happened to it.</summary>
/// <param name="PartId">The part's index in its entity's own body plan -- stable for the entity's lifetime, and what BodyPartBurningTimerComponent names.</param>
/// <cleanupVersion>1</cleanupVersion>
public readonly record struct BodyPartView(int PartId, string Name, BodyPartType Type, byte VerticalPosition, float MaximumHealth, bool IsVital, float CurrentHealth, bool IsDisabled, uint RegenLockedUntilFrame)
{
    /// <summary>True while this part is still inside its regen lockout as of now.</summary>
    public bool IsRegenLockedOut(long now) => !FrameDeadline.IsReached(RegenLockedUntilFrame, now);

    public override string ToString() =>
        MaximumHealth > 0
            ? $"{Engine.Utilities.StringUtility.BuildPercentageBar(Name, (int)CurrentHealth, (int)MaximumHealth, 20)} {(int)CurrentHealth}/{(int)MaximumHealth}"
            : $"Invalid MaximumHealth: {MaximumHealth}";
}

/// <summary>
/// The body parts an entity has and their condition: its races' BodyPartTemplates (shared) plus its
/// own BodyPartStateComponent (written the first time something happens to it). Every read and write
/// of a body part goes through here.
/// </summary>
/// <remarks>
/// A creature with two races has both body plans, first race first, and a part's id is its index in
/// that concatenation. An entity with no state component is simply undamaged -- reads fall back to
/// the templates, and the first write creates the component with every part at its template health,
/// so nothing else has to know whether it exists. A view over pools with no state of its own.
/// </remarks>
/// <cleanupVersion>1</cleanupVersion>
public sealed class EntityBodyParts(
    BlueprintRegistry? creatures,
    PackedComponentPool<BodyPartStateComponent> states,
    PackedComponentPool<RaceSlotsComponent>? raceSlots = null)
{
    private static readonly BodyPartTemplate[] NoParts = [];

    /// <summary>Empty when the module that built this was never configured -- a test module set with no races, where nothing has a body plan.</summary>
    private readonly BlueprintRegistry _creatures = creatures ?? new BlueprintRegistry();
    private readonly PackedComponentPool<BodyPartStateComponent> _states = states ?? throw new ArgumentNullException(nameof(states));

    /// <summary>Builds one from a ComponentManager, for callers that hold the manager rather than the pools.</summary>
    public static EntityBodyParts For(ComponentManager componentManager, BlueprintRegistry? creatures)
    {
        ArgumentNullException.ThrowIfNull(componentManager);

        return new EntityBodyParts(
            creatures,
            componentManager.GetPackedPool<BodyPartStateComponent>(),
            componentManager.IsRegistered<RaceSlotsComponent>() ? componentManager.GetPackedPool<RaceSlotsComponent>() : null);
    }

    /// <summary>True when the entity has a body plan at all -- what "this entity uses Complex health" means.</summary>
    public bool Has(int entityId) => Count(entityId) > 0;

    /// <summary>How many parts the entity has.</summary>
    public int Count(int entityId)
    {
        var (first, second) = TemplatesOf(entityId);
        return first.Length + second.Length;
    }

    /// <summary>The entity's part with this id, or false if it has no such part.</summary>
    public bool TryGet(int entityId, int partId, out BodyPartView part)
    {
        var (first, second) = TemplatesOf(entityId);
        if (partId < 0 || partId >= first.Length + second.Length)
        {
            part = default;
            return false;
        }

        ref readonly var template = ref partId < first.Length ? ref first[partId] : ref second[partId - first.Length];
        part = ViewOf(partId, in template, _states.GetDenseIndex(entityId));
        return true;
    }

    /// <summary>Every disabled part of the entity as a bit per part id, or 0 when nothing has happened to it -- the whole-body read selection needs, without building a view per part.</summary>
    public ushort DisabledMask(int entityId)
    {
        var stateDenseIndex = _states.GetDenseIndex(entityId);
        return stateDenseIndex < 0 ? (ushort)0 : _states.GetReadonlyByDenseIndex(stateDenseIndex).DisabledMask;
    }

    /// <summary>Walks the entity's parts in body-plan order, allocating nothing.</summary>
    public PartEnumerator Parts(int entityId)
    {
        var (first, second) = TemplatesOf(entityId);
        return new PartEnumerator(this, first, second, _states.GetDenseIndex(entityId));
    }

    /// <summary>Takes amount off the part, clamped to its effective maximum, disabling it and locking it out of regen for lockoutFrames the moment it lands at 0.</summary>
    public void Damage(int entityId, int partId, float amount, float effectiveMaximumHealth, long now, ushort lockoutFrames)
    {
        Update(entityId, partId, (amount, effectiveMaximumHealth, now, lockoutFrames), static (ref BodyPartStateComponent state, int id, (float Amount, float EffectiveMaximum, long Now, ushort LockoutFrames) change) =>
        {
            var current = Microsoft.Xna.Framework.MathHelper.Clamp(state.CurrentHealthOf(id) - change.Amount, 0f, change.EffectiveMaximum);
            state.SetCurrentHealth(id, current);

            if (current == 0)
            {
                state.SetDisabled(id, true);
                state.SetRegenLockedUntilFrame(id, FrameDeadline.After(change.Now, change.LockoutFrames));
            }
        });
    }

    /// <summary>Puts amount back onto the part, clamped to its effective maximum, re-enabling it once it is above 0.</summary>
    public void Heal(int entityId, int partId, float amount, float effectiveMaximumHealth)
    {
        Update(entityId, partId, (amount, effectiveMaximumHealth), static (ref BodyPartStateComponent state, int id, (float Amount, float EffectiveMaximum) change) =>
        {
            var current = Microsoft.Xna.Framework.MathHelper.Clamp(state.CurrentHealthOf(id) + change.Amount, 0f, change.EffectiveMaximum);
            state.SetCurrentHealth(id, current);

            if (current > 0)
            {
                state.SetDisabled(id, false);
            }
        });
    }

    /// <summary>Pushes the part's regen lockout out to lockoutFrames from now, whatever its health.</summary>
    public void LockOutOfRegen(int entityId, int partId, long now, ushort lockoutFrames) =>
        Update(entityId, partId, (now, lockoutFrames), static (ref BodyPartStateComponent state, int id, (long Now, ushort LockoutFrames) change) =>
            state.SetRegenLockedUntilFrame(id, FrameDeadline.After(change.Now, change.LockoutFrames)));

    /// <summary>Writes the part's current health outright, touching nothing else -- for bookkeeping that is neither damage nor healing (MaximumHealthShift).</summary>
    public void SetCurrentHealth(int entityId, int partId, float value) =>
        Update(entityId, partId, value, static (ref BodyPartStateComponent state, int id, float health) => state.SetCurrentHealth(id, health));

    /// <summary>The entity's current and maximum health, summed across its parts.</summary>
    public bool TryGetTotals(int entityId, out float current, out float maximum)
    {
        current = 0f;
        maximum = 0f;

        var any = false;
        foreach (var part in Parts(entityId))
        {
            current += part.CurrentHealth;
            maximum += part.MaximumHealth;
            any = true;
        }

        return any;
    }

    private delegate void StateChange<TState>(ref BodyPartStateComponent state, int partId, TState change);

    /// <summary>Creates the entity's state component if this is the first thing to happen to it, then applies change to one part.</summary>
    private void Update<TState>(int entityId, int partId, TState change, StateChange<TState> apply)
    {
        var stateDenseIndex = _states.GetDenseIndex(entityId);
        if (stateDenseIndex < 0)
        {
            if (partId < 0 || partId >= Count(entityId))
            {
                return;
            }

            stateDenseIndex = CreateState(entityId);
        }
        else if (partId < 0 || partId >= BodyPartStateComponent.MaximumParts)
        {
            return;
        }

        _states.UpdateByDenseIndex(stateDenseIndex, (partId, change, apply), static (ref BodyPartStateComponent state, (int PartId, TState Change, StateChange<TState> Apply) call) =>
            call.Apply(ref state, call.PartId, call.Change));
    }

    /// <summary>Gives the entity a state component holding every part at its template health, and returns its dense slot.</summary>
    private int CreateState(int entityId)
    {
        var (first, second) = TemplatesOf(entityId);
        var count = first.Length + second.Length;
        if (count > BodyPartStateComponent.MaximumParts)
        {
            throw new InvalidOperationException($"Entity {entityId} has {count} body parts, more than the {BodyPartStateComponent.MaximumParts} one entity can hold.");
        }

        var state = default(BodyPartStateComponent);
        for (var partId = 0; partId < count; partId++)
        {
            state.SetCurrentHealth(partId, partId < first.Length ? first[partId].MaximumHealth : second[partId - first.Length].MaximumHealth);
        }

        _states.Add(entityId, state);
        return _states.GetDenseIndex(entityId);
    }

    /// <summary>One part's view, reading its condition straight from the state component's dense slot -- -1 when the entity has none, which is every part at full health.</summary>
    private BodyPartView ViewOf(int partId, in BodyPartTemplate template, int stateDenseIndex)
    {
        if (stateDenseIndex < 0)
        {
            return new BodyPartView(partId, template.Name, template.Type, template.VerticalPosition, template.MaximumHealth, template.IsVital, template.MaximumHealth, IsDisabled: false, RegenLockedUntilFrame: 0);
        }

        ref readonly var state = ref _states.GetReadonlyByDenseIndex(stateDenseIndex);
        return new BodyPartView(partId, template.Name, template.Type, template.VerticalPosition, template.MaximumHealth, template.IsVital, state.CurrentHealthOf(partId), state.IsDisabled(partId), state.RegenLockedUntilFrameOf(partId));
    }

    /// <summary>The entity's body plan: its first race's templates, then its second's.</summary>
    private (BodyPartTemplate[] First, BodyPartTemplate[] Second) TemplatesOf(int entityId)
    {
        if (raceSlots is null || !raceSlots.TryGetReadonly(entityId, out var slots))
        {
            return (NoParts, NoParts);
        }

        return (
            _creatures.Races.TryGet(slots.Race1, out var race1) ? race1.Race!.BodyParts : NoParts,
            _creatures.Races.TryGet(slots.Race2, out var race2) ? race2.Race!.BodyParts : NoParts);
    }

    /// <summary>Walks one entity's parts without allocating -- templates from its races, condition from its state component.</summary>
    public struct PartEnumerator(EntityBodyParts owner, BodyPartTemplate[] first, BodyPartTemplate[] second, int stateDenseIndex)
    {
        private int _partId = -1;

        public readonly PartEnumerator GetEnumerator() => this;

        public BodyPartView Current { get; private set; }

        public bool MoveNext()
        {
            _partId++;
            if (_partId >= first.Length + second.Length)
            {
                return false;
            }

            ref readonly var template = ref _partId < first.Length ? ref first[_partId] : ref second[_partId - first.Length];
            Current = owner.ViewOf(_partId, in template, stateDenseIndex);
            return true;
        }
    }
}
