using Engine.ECS.Components;
using Engine.ECS.Entities;
using Engine.ECS.Systems;
using Engine.Tags;

namespace Engine.ECS.Properties;

/// <summary>Why a table write was refused, as shown to whoever asked for it.</summary>
public static class ComponentPropertyRefusals
{
    public const string NoSuchInstance = "The entity doesn't hold that component any more.";
    public const string Undescribed = "That component has no property table.";
    public const string ValueRefused = "That value can't be written there: it is read-only, out of range, or not one of its choices.";
    public const string EntityDestroyed = "That entity was destroyed.";
    public const string DuringSimulation = "Edits apply between frames, never while the simulation is updating.";
    public const string InstancesChanged = "That component instance changed or went since it was read; inspect it again.";
}

/// <summary>Which component instance an edit names: the entity by key (never its id, which a destroyed entity's successor may hold), the type, and the instance -- by its keyed-timer key where it has one, else, where the entity may hold several, by its value; otherwise it is the entity's one instance.</summary>
/// <param name="Instance">Its position when it was named: where its value is looked for first, the one way to choose between equal instances.</param>
/// <param name="Value">The instance as it was when named, boxed, for one of several with no keyed-timer key; null otherwise.</param>
public readonly record struct ComponentPropertyTarget(EntityKey Entity, Type ComponentType, int Instance, int? TimerKey = null, object? Value = null);

/// <summary>One edit that was applied: where, the value it replaced and the value it wrote, and the frame it was made on.</summary>
public sealed record ComponentPropertyEdit(ComponentPropertyTarget Target, int[] Path, string PropertyName, ComponentPropertyValue Previous, ComponentPropertyValue Value, long Frame)
{
    /// <summary>The instance it was written into, shared with every other edit of that instance, so an edit of another of its properties doesn't lose it.</summary>
    internal ComponentPropertyInstanceIdentity Identity { get; init; } = null!;
}

/// <summary>What names one edited instance among several with no keyed-timer key: its value as the last edit or revert of it left it, and where it was then; a null value for an instance named by key or the entity's only one.</summary>
/// <remarks>Shared by every edit of the instance. Once anything else changes it, no edit of it matches any more: what was edited isn't there to revert. Its position tells it from an equal instance beside it.</remarks>
internal sealed class ComponentPropertyInstanceIdentity(object? value, int instance)
{
    public object? Value { get; set; } = value;

    /// <summary>Its position when last written: where it is looked for first, so of several equal instances it is the one edited.</summary>
    public int Instance { get; set; } = instance;
}

/// <summary>What an edit or a revert did: the edit applied, or why nothing was written.</summary>
public readonly record struct ComponentPropertyEditResult(ComponentPropertyEdit? Edit, string? Refusal)
{
    public bool Applied => Refusal is null;
}

/// <summary>Writes one property of one entity's component through its table, between frames, and keeps the edits it applied so each can be reverted.</summary>
/// <remarks>
/// Admin tooling, never gameplay: a raw write raises no event, credits nobody and follows no rule. A destroyed entity is
/// refused (its key no longer resolves); a dead one is an entity like any other. The write is the pool's own table write
/// (IDescribedComponentPool.WriteProperty), so it is versioned and observed -- a deadline is rescheduled, a relationship's
/// link checked before it is stored. A revert writes the edit's previous value back the same way: it restores the value,
/// not the world, and is refused once the entity is gone or, for one of several instances, once anything else changed it.
/// </remarks>
public sealed class ComponentPropertyEdits(ComponentManager componentManager, SystemManager systemManager, SimulationClock clock)
{
    private readonly List<ComponentPropertyEdit> _applied = [];
    private readonly Dictionary<Type, List<Action<int>>> _refreshesAfterWrite = [];

    /// <summary>Names how to recompute what is cached from TComponent's values (an ability score's total from its base and modifiers): run, by entity id, after every edit or revert written into one.</summary>
    /// <remarks>Recomputing a cache keeps the component as gameplay would leave it; it raises no event and follows no rule beyond the cache's own.</remarks>
    public void RefreshAfterWrite<TComponent>(Action<int> refresh)
    {
        if (!_refreshesAfterWrite.TryGetValue(typeof(TComponent), out var refreshes))
        {
            refreshes = [];
            _refreshesAfterWrite.Add(typeof(TComponent), refreshes);
        }

        refreshes.Add(refresh);
    }

    /// <summary>Every edit applied and not yet reverted, oldest first.</summary>
    public IReadOnlyList<ComponentPropertyEdit> Applied => _applied;

    /// <summary>Raised after an edit is applied or reverted.</summary>
    public event Action? Changed;

    /// <summary>Names the entity's <paramref name="instance"/>-th <paramref name="componentType"/> as it is now, so an edit or a revert finds it however its position moves: by keyed-timer key, by value where the entity may hold several, else as its only instance.</summary>
    public ComponentPropertyTarget TargetOf(EntityKey entity, Type componentType, int instance)
    {
        if (!componentManager.Keys.TryGetEntityId(entity, out var entityId) || !TryGetPool(componentType, out var pool))
        {
            return new ComponentPropertyTarget(entity, componentType, instance);
        }

        if (pool.TimerKeyOf(entityId, instance) is { } timerKey)
        {
            return new ComponentPropertyTarget(entity, componentType, instance, timerKey);
        }

        return new ComponentPropertyTarget(entity, componentType, instance, Value: pool.HoldsSeveralPerEntity ? pool.ValueOf(entityId, instance) : null);
    }

    /// <summary>Writes value at path into the target instance, keeping the edit; or says why it wrote nothing.</summary>
    public ComponentPropertyEditResult Apply(ComponentPropertyTarget target, int[] path, in ComponentPropertyValue value)
    {
        ForgetEditsOfDestroyedEntities();
        var result = Write(target, IdentityOf(target), path, in value);
        if (result.Edit is { } edit)
        {
            _applied.Add(edit);
            Changed?.Invoke();
        }

        return result;
    }

    /// <summary>Writes the edit's previous value back and forgets the edit; refused when its entity is gone (forgetting it, as every edit of a destroyed entity is), its instance can't be found, or the write is refused (keeping it).</summary>
    public ComponentPropertyEditResult Revert(ComponentPropertyEdit edit)
    {
        ForgetEditsOfDestroyedEntities();
        var result = Write(edit.Target, edit.Identity, edit.Path, edit.Previous);
        if (result.Applied)
        {
            _applied.Remove(edit);
            Changed?.Invoke();
        }

        return result;
    }

    /// <summary>The latest edit still applied, or null: what "Revert last edit" offers. Edits of destroyed entities are forgotten first, since none of them can ever be reverted.</summary>
    public ComponentPropertyEdit? Latest()
    {
        ForgetEditsOfDestroyedEntities();
        return _applied.Count == 0 ? null : _applied[^1];
    }

    /// <summary>Why reverting the edit would write nothing -- its entity gone, the simulation updating, its instance changed or gone -- or null when its instance is there to write into.</summary>
    /// <remarks>The checks Revert makes before it writes: what a revert's "can I" answers with. The table may still refuse the value itself.</remarks>
    public string? RevertRefusal(ComponentPropertyEdit edit) => Locate(edit.Target, edit.Identity, edit.Path, out _, out _, out _, out _);

    /// <summary>Where the edit's instance is now, named as TargetOf would name it: its position and, for one of several, its value as the edit left it.</summary>
    /// <remarks>What a view holding the instance names it by after writing it, so its next edit or change marker finds the same instance.</remarks>
    public ComponentPropertyTarget CurrentTargetOf(ComponentPropertyEdit edit) =>
        edit.Target with { Instance = edit.Identity.Instance, Value = edit.Identity.Value };

    /// <summary>The latest edit still applied to that instance at path, or null.</summary>
    public ComponentPropertyEdit? LatestFor(ComponentPropertyTarget target, int[] path)
    {
        for (var index = _applied.Count - 1; index >= 0; index--)
        {
            var edit = _applied[index];
            if (NamesTheSameInstance(edit, target) && edit.Path.AsSpan().SequenceEqual(path))
            {
                return edit;
            }
        }

        return null;
    }

    /// <summary>Whether frames have passed since the edit: then a revert restores the value, while the simulation has moved on around it.</summary>
    public bool FramesHavePassedSince(ComponentPropertyEdit edit) => clock.CurrentFrame != edit.Frame;

    /// <summary>Lets go of every edit whose entity was destroyed: it can never be reverted, and would hold its values for the session.</summary>
    private void ForgetEditsOfDestroyedEntities()
    {
        for (var index = _applied.Count - 1; index >= 0; index--)
        {
            if (!componentManager.Keys.TryGetEntityId(_applied[index].Target.Entity, out _))
            {
                _applied.RemoveAt(index);
            }
        }
    }

    private bool NamesTheSameInstance(ComponentPropertyEdit edit, in ComponentPropertyTarget target)
    {
        if (edit.Target.Entity != target.Entity || edit.Target.ComponentType != target.ComponentType)
        {
            return false;
        }

        if (edit.Target.TimerKey is { } timerKey)
        {
            return timerKey == target.TimerKey;
        }

        if (edit.Identity.Value is not { } value)
        {
            return true;
        }

        return Equals(value, target.Value) && IsAt(edit.Target.Entity, target.ComponentType, edit.Identity, target.Instance);
    }

    /// <summary>Whether the instance identity names is now at position instance: of several equal instances, only the one edited.</summary>
    private bool IsAt(EntityKey entity, Type componentType, ComponentPropertyInstanceIdentity identity, int instance) =>
        componentManager.Keys.TryGetEntityId(entity, out var entityId)
            && TryGetPool(componentType, out var pool)
            && pool.TryFindInstanceHolding(entityId, identity.Instance, identity.Value!, out var found)
            && found == instance;

    /// <summary>The identity an earlier edit of the same instance holds, so every edit of it follows it; a new one otherwise.</summary>
    private ComponentPropertyInstanceIdentity IdentityOf(in ComponentPropertyTarget target)
    {
        foreach (var edit in _applied)
        {
            if (NamesTheSameInstance(edit, target))
            {
                return edit.Identity;
            }
        }

        return new ComponentPropertyInstanceIdentity(target.TimerKey is null ? target.Value : null, target.Instance);
    }

    private ComponentPropertyEditResult Write(in ComponentPropertyTarget target, ComponentPropertyInstanceIdentity identity, int[] path, in ComponentPropertyValue value)
    {
        if (Locate(target, identity, path, out var entityId, out var pool, out var instance, out var leaf) is { } locateRefusal)
        {
            return new ComponentPropertyEditResult(null, locateRefusal);
        }

        if (!pool.WriteProperty(entityId, instance, path, in value, out var refusal))
        {
            return new ComponentPropertyEditResult(null, refusal ?? ComponentPropertyRefusals.ValueRefused);
        }

        if (_refreshesAfterWrite.TryGetValue(target.ComponentType, out var refreshes))
        {
            foreach (var refresh in refreshes)
            {
                refresh(entityId);
            }
        }

        if (identity.Value is not null)
        {
            identity.Value = pool.ValueOf(entityId, instance);
        }

        identity.Instance = instance;
        var edit = new ComponentPropertyEdit(target, path, leaf.Property.Name, leaf.Value, value, clock.CurrentFrame) { Identity = identity };
        return new ComponentPropertyEditResult(edit, null);
    }

    /// <summary>Finds the instance target names now, and the value at path in it: null when found, else why nothing can be written.</summary>
    private string? Locate(in ComponentPropertyTarget target, ComponentPropertyInstanceIdentity identity, int[] path, out int entityId, out IDescribedComponentPool pool, out int instance, out (ComponentPropertyDescriptor Property, ComponentPropertyValue Value) leaf)
    {
        pool = null!;
        instance = -1;
        leaf = default;
        entityId = -1;
        if (systemManager.IsUpdating)
        {
            return ComponentPropertyRefusals.DuringSimulation;
        }

        if (!componentManager.Keys.TryGetEntityId(target.Entity, out entityId))
        {
            return ComponentPropertyRefusals.EntityDestroyed;
        }

        if (!TryGetPool(target.ComponentType, out pool))
        {
            return ComponentPropertyRefusals.Undescribed;
        }

        if (!TryResolveInstance(pool, entityId, target, identity, out instance))
        {
            return ComponentPropertyRefusals.InstancesChanged;
        }

        var reader = new LeafReader(path);
        pool.ReadInstanceProperties(entityId, instance, reader);
        if (reader.Leaf is not { } found)
        {
            return ComponentPropertyRefusals.ValueRefused;
        }

        leaf = found;
        return null;
    }

    private bool TryGetPool(Type componentType, out IDescribedComponentPool pool)
    {
        if (componentManager.TryGetPool(componentType, out var candidate) && candidate is IDescribedComponentPool { PropertyTable: not null } described)
        {
            pool = described;
            return true;
        }

        pool = null!;
        return false;
    }

    /// <summary>A keyed timer by its key and one of several instances by its value, wherever each now sits; otherwise the entity's one instance.</summary>
    private static bool TryResolveInstance(IDescribedComponentPool pool, int entityId, in ComponentPropertyTarget target, ComponentPropertyInstanceIdentity identity, out int instance)
    {
        if (target.TimerKey is { } timerKey)
        {
            return pool.TryFindInstanceByTimerKey(entityId, timerKey, out instance);
        }

        if (identity.Value is { } value)
        {
            return pool.TryFindInstanceHolding(entityId, identity.Instance, value, out instance);
        }

        instance = 0;
        return !pool.HoldsSeveralPerEntity && target.Instance == 0 && pool.Has(entityId);
    }

    /// <summary>The value at one path of one instance, as it would be written back.</summary>
    private sealed class LeafReader(int[] wantedPath) : ComponentPropertyVisitor
    {
        private readonly List<int> _path = [];
        private readonly Stack<int> _nextElement = new();

        public (ComponentPropertyDescriptor Property, ComponentPropertyValue Value)? Leaf { get; private set; }

        public override void Integer(ComponentPropertyDescriptor property, long value) => Take(property, ComponentPropertyValue.FromInteger(value));

        public override void Number(ComponentPropertyDescriptor property, double value) => Take(property, ComponentPropertyValue.FromNumber(value));

        public override void Boolean(ComponentPropertyDescriptor property, bool value) => Take(property, ComponentPropertyValue.FromBoolean(value));

        public override void Text(ComponentPropertyDescriptor property, string? value) => Take(property, ComponentPropertyValue.FromText(value));

        public override void Enum(ComponentPropertyDescriptor property, long value) => Take(property, ComponentPropertyValue.FromEnum(value));

        public override void Guid(ComponentPropertyDescriptor property, Guid value) => Take(property, ComponentPropertyValue.FromGuid(value));

        public override void Color(ComponentPropertyDescriptor property, uint packedValue) => Take(property, ComponentPropertyValue.FromColor(packedValue));

        public override void Tag(ComponentPropertyDescriptor property, GameplayTag value) => Take(property, ComponentPropertyValue.FromTag(value.IsNone ? "" : value.Name));

        public override void TagQuery(ComponentPropertyDescriptor property, GameplayTagQuery? value) => Take(property, ComponentPropertyValue.FromText(value?.ToString()));

        public override void Definition(ComponentPropertyDescriptor property, object? value) => Take(property, ComponentPropertyValue.FromText(value?.ToString()));

        public override void Absent(ComponentPropertyDescriptor property) => Take(property, ComponentPropertyValue.Absent);

        public override void BeginNested(ComponentPropertyDescriptor property)
        {
            _path.Add(NextStep(property));
            _nextElement.Push(-1);
        }

        public override void EndNested(ComponentPropertyDescriptor property) => End();

        public override void BeginIndexed(ComponentPropertyDescriptor property, int count)
        {
            _path.Add(NextStep(property));
            _nextElement.Push(0);
        }

        public override void EndIndexed(ComponentPropertyDescriptor property) => End();

        private void End()
        {
            if (_nextElement.Count > 0)
            {
                _nextElement.Pop();
                _path.RemoveAt(_path.Count - 1);
            }
        }

        /// <summary>The path step for a value at this level: an indexed level's next row, taken; otherwise the property's own index.</summary>
        private int NextStep(ComponentPropertyDescriptor property)
        {
            if (_nextElement.Count > 0 && _nextElement.Peek() >= 0)
            {
                var row = _nextElement.Pop();
                _nextElement.Push(row + 1);
                return row;
            }

            return property.Index;
        }

        private void Take(ComponentPropertyDescriptor property, ComponentPropertyValue value)
        {
            var index = NextStep(property);
            if (_path.Count + 1 != wantedPath.Length || index != wantedPath[^1])
            {
                return;
            }

            for (var level = 0; level < _path.Count; level++)
            {
                if (_path[level] != wantedPath[level])
                {
                    return;
                }
            }

            Leaf = (property, value);
        }
    }
}
