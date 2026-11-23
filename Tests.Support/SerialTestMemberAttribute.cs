namespace TestSupport;

/// <summary>Names a member of a type the tests can't mark themselves as a process-wide resource: a test reaching it must be [DoNotParallelize] (CAD0004).</summary>
/// <param name="resourceName">The resource's name, shown in the build error.</param>
/// <param name="type">The type declaring the member; an open generic for a generic type.</param>
/// <param name="memberName">The member, with all its overloads.</param>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class SerialTestMemberAttribute(string resourceName, Type type, string memberName) : Attribute
{
    public string ResourceName { get; } = resourceName;

    public Type Type { get; } = type;

    public string MemberName { get; } = memberName;
}
