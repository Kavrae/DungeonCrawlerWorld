namespace Engine.ECS.Properties;

/// <summary>Reads and writes one described component type's properties; one instance per type, reached through ComponentPropertyAccessOf.</summary>
/// <remarks>Generated for a [DescribedComponent] struct, hand-written for a [DescribedByHand] one. Never used by gameplay: see the namespace's architecture test.</remarks>
public abstract class ComponentPropertyAccess<T>
    where T : struct
{
    public abstract ComponentPropertyTable Table { get; }

    public abstract void Read(in T component, ComponentPropertyVisitor visitor);

    /// <summary>Writes the value at <paramref name="path"/> -- a property index, then an index into each nested or indexed level -- and says whether it did.</summary>
    /// <remarks>Refused, with the component unchanged, when the property is read-only, the value is out of its range or not a declared enum or flag value, or the path doesn't name a value.</remarks>
    public abstract bool Write(ref T component, ReadOnlySpan<int> path, in ComponentPropertyValue value);

    /// <summary>Writes a saved value back at <paramref name="path"/> as a snapshot restore does: like Write, but a read-only member, a definition and a tag query are written too, and a value under an absent optional starts that optional from its default.</summary>
    /// <remarks>Still refused for a value out of range or not declared, a path that names nothing, and a derived member, which is never saved.</remarks>
    public abstract bool Restore(ref T component, ReadOnlySpan<int> path, in ComponentPropertyValue value);
}

/// <summary>The slot a described type's access fills: the generated module initializer of the type's assembly, or the engine for a type it builds (a relationship's sources). Null for an undescribed type.</summary>
public static class ComponentPropertyAccessOf<T>
    where T : struct
{
    private static ComponentPropertyAccess<T>? _access;

    public static ComponentPropertyAccess<T>? Access => _access;

    /// <summary>Fills the slot once; registering the same access again changes nothing, and a different one throws, so no build or test can swap a type's table under another.</summary>
    public static void Register(ComponentPropertyAccess<T> access)
    {
        var registered = Interlocked.CompareExchange(ref _access, access, null);
        if (registered is not null && !ReferenceEquals(registered, access))
        {
            throw new InvalidOperationException($"{typeof(T).Name} already has a property table ({registered.GetType().Name}); a type is described once.");
        }
    }
}
