using Engine.ECS.Components;

namespace Engine.ECS.Properties;

/// <summary>A pool whose components can be read and written by their property table.</summary>
public interface IDescribedComponentPool : IComponentPool
{
    /// <summary>The component type's table; null when the type isn't described.</summary>
    ComponentPropertyTable? PropertyTable { get; }

    /// <summary>Reads every instance the entity holds, in order, each between the visitor's BeginInstance and EndInstance; returns how many. None when the type isn't described.</summary>
    int ReadProperties(int entityId, ComponentPropertyVisitor visitor);

    /// <summary>Reads only the entity's <paramref name="instance"/>-th instance, between BeginInstance and EndInstance; false when it holds no such instance or the type isn't described.</summary>
    bool ReadInstanceProperties(int entityId, int instance, ComponentPropertyVisitor visitor);

    /// <summary>Writes one value into the entity's <paramref name="instance"/>-th instance through a write scope, so it is versioned and observed; false, with nothing written and the reason in <paramref name="refusal"/>, when there is no such instance, the table refuses the value, or the pool's owner refuses the result (a relationship's link).</summary>
    /// <remarks>Between frames only. Not for gameplay: a raw write raises no event and follows no rule. Reached through ComponentPropertyEdits.</remarks>
    bool WriteProperty(int entityId, int instance, ReadOnlySpan<int> path, in ComponentPropertyValue value, out string? refusal);

    /// <summary>Whether an entity may hold several instances (a Multi pool): then an instance with no keyed-timer key is named by its value, since positions move as instances come and go.</summary>
    bool HoldsSeveralPerEntity { get; }

    /// <summary>Appends each instance's version, in order, and returns how many; reads no property.</summary>
    int CopyInstanceVersions(int entityId, List<uint> destination);

    /// <summary>The instance's keyed-timer key (IKeyedScheduledTimer.TimerKey), which names it however its position moves; null for a component that isn't one, or whose pool no MultiTimerWheel drives.</summary>
    int? TimerKeyOf(int entityId, int instance);

    /// <summary>The position of the entity's instance with this keyed-timer key, now.</summary>
    bool TryFindInstanceByTimerKey(int entityId, int timerKey, out int instance);

    /// <summary>The entity's instance-th instance, boxed: what names it among several when it has no keyed-timer key; null when it holds no such instance.</summary>
    object? ValueOf(int entityId, int instance);

    /// <summary>The position of the entity's instance equal to <paramref name="value"/> now, trying <paramref name="preferredInstance"/> first; equal instances can't be told apart, so any of them is it.</summary>
    bool TryFindInstanceHolding(int entityId, int preferredInstance, object value, out int instance);

    /// <summary>Adds the entity's instances of this component, each built from its saved values (ComponentPropertyAccess.Restore) and added through the pool's Add, so timers schedule and observers hear of it; null when every one was added, else why none was.</summary>
    /// <remarks>The entity holds none yet. A Multi pool's instances end in the order given, as ReadProperties will read them back; a Direct or Packed pool takes exactly one. Every instance is built and its owner asked (a relationship's link) before any is added. For a snapshot restore (EntitySnapshot), between frames.</remarks>
    string? Restore(int entityId, IReadOnlyList<IReadOnlyList<ComponentPropertyLeaf>> instances);
}

/// <summary>One saved value of a component instance: where it goes (a property index, then an index per nested or indexed level) and what it is.</summary>
public readonly record struct ComponentPropertyLeaf(int[] Path, ComponentPropertyValue Value);

/// <summary>What every pool's table read and write share, whatever its storage: the access lookup, the instance bracket and the edit on a copy.</summary>
internal static class ComponentPropertyPoolAccess
{
    /// <summary>Reads one instance between the visitor's BeginInstance and EndInstance; false when the type isn't described.</summary>
    public static bool ReadInstance<T>(in T component, int instance, uint version, ComponentPropertyVisitor visitor)
        where T : struct
    {
        if (ComponentPropertyAccessOf<T>.Access is not { } access)
        {
            return false;
        }

        visitor.BeginInstance(access.Table, instance, version);
        access.Read(in component, visitor);
        visitor.EndInstance(access.Table, instance);
        return true;
    }

    /// <summary>The component with value written at path, built on a copy of current so nothing is stored; null when it was, else why not.</summary>
    public static string? TryEdit<T>(in T current, ReadOnlySpan<int> path, in ComponentPropertyValue value, out T edited)
        where T : struct
    {
        edited = current;
        if (ComponentPropertyAccessOf<T>.Access is not { } access)
        {
            return ComponentPropertyRefusals.Undescribed;
        }

        return access.Write(ref edited, path, in value) ? null : ComponentPropertyRefusals.ValueRefused;
    }
}

/// <summary>What a pool's owner says about a component a table write or restore would store, and is told once it is stored: a relationship checks a link once and resolves it while it is written.</summary>
internal interface IComponentPropertyWriteOwner<T>
    where T : struct
{
    /// <summary>Null to store component for entityId, else why not; a null answer is always followed by EndWrite once it is stored.</summary>
    string? BeginWrite(int entityId, in T component);

    void EndWrite();
}

/// <summary>Builds a component from its saved values, for a pool's Restore.</summary>
internal static class ComponentPropertyRestoring
{
    public static string? TryBuildEach<T>(IReadOnlyList<IReadOnlyList<ComponentPropertyLeaf>> instances, T[] built)
        where T : struct
    {
        if (ComponentPropertyAccessOf<T>.Access is not { } access)
        {
            return ComponentPropertyRefusals.Undescribed;
        }

        for (var instance = 0; instance < instances.Count; instance++)
        {
            if (TryBuild(access, instances[instance], out built[instance]) is { } refusal)
            {
                return refusal;
            }
        }

        return null;
    }

    /// <summary>Builds the one instance a Direct or Packed pool holds: refused unless exactly one was saved.</summary>
    public static string? TryBuildOne<T>(IReadOnlyList<IReadOnlyList<ComponentPropertyLeaf>> instances, out T built)
        where T : struct
    {
        built = default;
        if (instances.Count != 1)
        {
            return $"{typeof(T).Name} holds one instance an entity, and {instances.Count} were saved.";
        }

        return ComponentPropertyAccessOf<T>.Access is { } access ? TryBuild(access, instances[0], out built) : ComponentPropertyRefusals.Undescribed;
    }

    private static string? TryBuild<T>(ComponentPropertyAccess<T> access, IReadOnlyList<ComponentPropertyLeaf> leaves, out T built)
        where T : struct
    {
        built = default;
        foreach (var leaf in leaves)
        {
            if (!access.Restore(ref built, leaf.Path, leaf.Value))
            {
                return $"{typeof(T).Name}.{PathText(access.Table, leaf.Path)} can't be restored to {leaf.Value}.";
            }
        }

        return null;
    }

    /// <summary>A path as property names, an indexed level's row as [n].</summary>
    public static string PathText(ComponentPropertyTable table, ReadOnlySpan<int> path)
    {
        var text = new System.Text.StringBuilder();
        IReadOnlyList<ComponentPropertyDescriptor>? properties = table.Properties;
        ComponentPropertyDescriptor? indexed = null;
        foreach (var step in path)
        {
            if (indexed is { } row)
            {
                text.Append('[').Append(step).Append(']');
                properties = row.Element?.Nested?.Properties;
                indexed = null;
                continue;
            }

            if (properties is null || (uint)step >= (uint)properties.Count)
            {
                text.Append('.').Append(step);
                continue;
            }

            var property = properties[step];
            if (text.Length > 0)
            {
                text.Append('.');
            }

            text.Append(property.Name);
            indexed = property.Kind == ComponentPropertyKind.Indexed ? property : null;
            properties = property.Nested?.Properties;
        }

        return text.ToString();
    }
}
