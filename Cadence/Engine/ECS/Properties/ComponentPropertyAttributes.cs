namespace Engine.ECS.Properties;

/// <summary>Has the property-table generator describe this partial struct as a component: a table, a traversal, and registration in ComponentPropertyAccessOf.</summary>
/// <remarks>Every public instance property and field is a property; a member whose type the generator can't describe fails the build (CAD0002).</remarks>
[AttributeUsage(AttributeTargets.Struct, Inherited = false)]
public sealed class DescribedComponentAttribute : Attribute;

/// <summary>Has the property-table generator describe this partial struct as a value another described type holds: a table and a traversal, read and written through its holder.</summary>
[AttributeUsage(AttributeTargets.Struct, Inherited = false)]
public sealed class DescribedValueAttribute : Attribute;

/// <summary>Names the hand-written access for a struct the generator can't describe: a component's access registers through its static Instance, a value's is called through its static Description, ReadProperties and WriteProperty.</summary>
[AttributeUsage(AttributeTargets.Struct, Inherited = false)]
public sealed class DescribedByHandAttribute(Type accessType) : Attribute
{
    public Type AccessType { get; } = accessType;
}

/// <summary>Marks a shared reference type a component may hold: shown as a summary, never written through a table, saved through its own codec.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = true)]
public sealed class DescribedDefinitionAttribute : Attribute;

/// <summary>Leaves a public member out of its type's table: derived convenience or engine bookkeeping.</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public sealed class NotAPropertyAttribute : Attribute;

/// <summary>Brings a private member into its type's table.</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public sealed class ComponentPropertyAttribute : Attribute;

/// <summary>Keeps a property out of saves; it is still shown.</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public sealed class NotSavedAttribute : Attribute;

/// <summary>Refuses writes through the table to a settable property something else is indexed by or rebuilt from; it changes only through its owner's API.</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public sealed class ReadOnlyPropertyAttribute : Attribute;

/// <summary>Names what a property's value stands for, which the game resolves for display, choices and saves (ComponentPropertyMeaningNames, or a game's own).</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public sealed class PropertyMeaningAttribute(string meaning) : Attribute
{
    public string Meaning { get; } = meaning;
}
