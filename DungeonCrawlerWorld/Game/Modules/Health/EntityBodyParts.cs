using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Engine.ECS.Systems;
using Engine.Math;
using Game.Modules.Health.Components;
using Game.Modules.Race.Components;
using Game.Blueprints;

namespace Game.Modules.Health;

/// <summary>One body part of one entity, as EntityBodyParts hands it out: its race's template plus whatever has happened to it.</summary>
/// <remarks>CurrentHealth already includes the entity's MaximumHealth modifiers; BaseMaximumHealth does not. Compare current health only with the modified maximum (StatModifierMath.CalculateTotal over the entity's MaximumHealth sums), never with BaseMaximumHealth.</remarks>
/// <param name="PartId">The part's index in its entity's own body plan -- stable for the entity's lifetime, and what anything addressing one part names it by.</param>
/// <cleanupVersion>1</cleanupVersion>
public readonly record struct BodyPartView(int PartId, string Name, BodyPartType Type, byte VerticalPosition, float BaseMaximumHealth, bool IsVital, float CurrentHealth, bool IsDisabled, uint RegenLockedUntilFrame)
{
    /// <summary>True while this part is still inside its regen lockout as of now.</summary>
    public bool IsRegenLockedOut(long now) => !FrameDeadline.IsReached(RegenLockedUntilFrame, now);

    public override string ToString() =>
        BaseMaximumHealth > 0
            ? $"{Engine.Utilities.StringUtility.BuildPercentageBar(Name, (int)CurrentHealth, (int)BaseMaximumHealth, 20)} {(int)CurrentHealth}/{(int)BaseMaximumHealth}"
            : $"Invalid BaseMaximumHealth: {BaseMaximumHealth}";
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
    BlueprintRegistry creatures,
    PackedComponentPool<BodyPartStateComponent> states,
    PackedComponentPool<RaceSlotsComponent> raceSlots)
{
    private static readonly BodyPartTemplate[] NoParts = [];


    /// <summary>Builds one from a ComponentManager, for callers that hold the manager rather than the pools.</summary>
    public static EntityBodyParts For(ComponentManager componentManager, BlueprintRegistry creatures)
    {
        ArgumentNullException.ThrowIfNull(componentManager);

        return new EntityBodyParts(
            creatures,
            componentManager.GetPackedPool<BodyPartStateComponent>(),
            componentManager.GetPackedPool<RaceSlotsComponent>());
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
        part = ViewOf(partId, in template, states.GetDenseIndex(entityId));
        return true;
    }

    /// <summary>Every disabled part of the entity as a bit per part id, or 0 when nothing has happened to it -- the whole-body read selection needs, without building a view per part.</summary>
    public ushort DisabledMask(int entityId)
    {
        var stateDenseIndex = states.GetDenseIndex(entityId);
        return stateDenseIndex < 0 ? (ushort)0 : states.GetReadonlyByDenseIndex(stateDenseIndex).DisabledMask;
    }

    /// <summary>Walks the entity's parts in body-plan order, allocating nothing.</summary>
    public PartEnumerator Parts(int entityId)
    {
        var (first, second) = TemplatesOf(entityId);
        return new PartEnumerator(this, first, second, states.GetDenseIndex(entityId));
    }

    /// <summary>Takes amount off the part, clamped to its effective maximum, disabling it and locking it out of regen for lockoutFrames the moment it lands at 0.</summary>
    public void Damage(int entityId, int partId, float amount, float effectiveMaximumHealth, long now, ushort lockoutFrames) =>
        Update(entityId, new DamagePartUpdate(partId, amount, effectiveMaximumHealth, FrameDeadline.After(now, lockoutFrames)));

    /// <summary>Puts amount back onto the part, clamped to its effective maximum, re-enabling it once it is above 0.</summary>
    public void Heal(int entityId, int partId, float amount, float effectiveMaximumHealth) =>
        Update(entityId, new HealPartUpdate(partId, amount, effectiveMaximumHealth));

    /// <summary>Pushes the part's regen lockout out to lockoutFrames from now, whatever its health.</summary>
    public void LockOutOfRegen(int entityId, int partId, long now, ushort lockoutFrames) =>
        Update(entityId, new LockPartOutOfRegenUpdate(partId, FrameDeadline.After(now, lockoutFrames)));

    /// <summary>Writes the part's current health outright, touching nothing else -- for bookkeeping that is neither damage nor healing (MaximumHealthShift).</summary>
    public void SetCurrentHealth(int entityId, int partId, float value) =>
        Update(entityId, new SetPartHealthUpdate(partId, value));

    /// <summary>The entity's current health and its base maximum (before MaximumHealth modifiers), summed across its parts.</summary>
    /// <remarks>The two are in different units -- current health already includes the modifiers -- so never compare them: HealthQueries.TryGetTotals gives the modified maximum.</remarks>
    public bool TryGetCurrentAndBaseMaximumTotals(int entityId, out float current, out float baseMaximum)
    {
        current = 0f;
        baseMaximum = 0f;

        var any = false;
        foreach (var part in Parts(entityId))
        {
            current += part.CurrentHealth;
            baseMaximum += part.BaseMaximumHealth;
            any = true;
        }

        return any;
    }

    /// <summary>Creates the entity's state component if this is the first thing to happen to it, then applies updater to the part it names.</summary>
    private void Update<TUpdater>(int entityId, TUpdater updater) where TUpdater : unmanaged, IBodyPartUpdater
    {
        var partId = updater.PartId;
        var stateDenseIndex = states.GetDenseIndex(entityId);
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

        states.UpdateByDenseIndex(stateDenseIndex, updater);
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
            state.SetCurrentHealth(partId, partId < first.Length ? first[partId].BaseMaximumHealth : second[partId - first.Length].BaseMaximumHealth);
        }

        states.Add(entityId, state);
        return states.GetDenseIndex(entityId);
    }

    /// <summary>One part's view, reading its condition straight from the state component's dense slot -- -1 when the entity has none, which is every part at full health.</summary>
    private BodyPartView ViewOf(int partId, in BodyPartTemplate template, int stateDenseIndex)
    {
        if (stateDenseIndex < 0)
        {
            return new BodyPartView(partId, template.Name, template.Type, template.VerticalPosition, template.BaseMaximumHealth, template.IsVital, template.BaseMaximumHealth, IsDisabled: false, RegenLockedUntilFrame: 0);
        }

        ref readonly var state = ref states.GetReadonlyByDenseIndex(stateDenseIndex);
        return new BodyPartView(partId, template.Name, template.Type, template.VerticalPosition, template.BaseMaximumHealth, template.IsVital, state.CurrentHealthOf(partId), state.IsDisabled(partId), state.RegenLockedUntilFrameOf(partId));
    }

    /// <summary>The entity's body plan: its first race's templates, then its second's.</summary>
    private (BodyPartTemplate[] First, BodyPartTemplate[] Second) TemplatesOf(int entityId)
    {
        if (!raceSlots.TryGetReadonly(entityId, out var slots))
        {
            return (NoParts, NoParts);
        }

        return (
            creatures.Races.TryGet(slots.Race1, out var race1) ? race1.Race!.BodyParts : NoParts,
            creatures.Races.TryGet(slots.Race2, out var race2) ? race2.Race!.BodyParts : NoParts);
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

    /// <summary>A change to one body part, which Update checks against the entity's plan before applying.</summary>
    private interface IBodyPartUpdater : IComponentUpdater<BodyPartStateComponent>
    {
        int PartId { get; }
    }

    private readonly struct DamagePartUpdate(int partId, float amount, float effectiveMaximumHealth, uint regenLockedUntilFrame) : IBodyPartUpdater
    {
        public int PartId => partId;

        public void Update(ref BodyPartStateComponent state)
        {
            var current = MathUtility.Clamp(state.CurrentHealthOf(partId) - amount, 0f, effectiveMaximumHealth);
            state.SetCurrentHealth(partId, current);

            if (current == 0)
            {
                state.SetDisabled(partId, true);
                state.SetRegenLockedUntilFrame(partId, regenLockedUntilFrame);
            }
        }
    }

    private readonly struct HealPartUpdate(int partId, float amount, float effectiveMaximumHealth) : IBodyPartUpdater
    {
        public int PartId => partId;

        public void Update(ref BodyPartStateComponent state)
        {
            var current = MathUtility.Clamp(state.CurrentHealthOf(partId) + amount, 0f, effectiveMaximumHealth);
            state.SetCurrentHealth(partId, current);

            if (current > 0)
            {
                state.SetDisabled(partId, false);
            }
        }
    }

    private readonly struct LockPartOutOfRegenUpdate(int partId, uint regenLockedUntilFrame) : IBodyPartUpdater
    {
        public int PartId => partId;

        public void Update(ref BodyPartStateComponent state) => state.SetRegenLockedUntilFrame(partId, regenLockedUntilFrame);
    }

    private readonly struct SetPartHealthUpdate(int partId, float health) : IBodyPartUpdater
    {
        public int PartId => partId;

        public void Update(ref BodyPartStateComponent state) => state.SetCurrentHealth(partId, health);
    }
}
