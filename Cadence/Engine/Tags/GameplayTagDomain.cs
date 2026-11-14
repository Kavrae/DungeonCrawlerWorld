namespace Engine.Tags;

/// <summary>One role a gameplay tag plays -- what a tag-holding field accepts, such as "what a use of something is" or "an item category".</summary>
/// <remarks>
/// An identity only, kept in a static readonly field and named by every field that holds tags of this role (a content
/// codec's ContentField, a check over code-declared definitions). Which subtrees it accepts is declared per build: the
/// module that owns it declares it (GameplayTagDeclarations.DeclareDomain) and, when it is extensible, others add to it
/// (AddToDomain). GameplayTagRegistry answers whether a tag belongs to it.
/// </remarks>
public sealed class GameplayTagDomain(string name)
{
    public string Name { get; } = name;

    public override string ToString() => Name;
}
