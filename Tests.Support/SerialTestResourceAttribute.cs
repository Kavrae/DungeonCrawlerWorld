namespace TestSupport;

/// <summary>Marks a type as a process-wide resource: a test reaching any of its members must be [DoNotParallelize] (CAD0004).</summary>
/// <param name="name">The resource's name, shown in the build error.</param>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
public sealed class SerialTestResourceAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}
