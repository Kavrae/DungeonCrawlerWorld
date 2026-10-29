namespace UIEngine.UI;

/// <summary>A set of open elements that belong together -- the windows a light-dismissable counts as inside it.</summary>
/// <remarks>
/// An element joins when it opens and leaves when it closes, so whoever asks (Contains) never lists the members
/// itself and a new member needs nothing from it. Leaving on close also keeps a pooled element from still being a
/// member when it is reused.
/// </remarks>
public sealed class ElementGroup
{
    private readonly HashSet<Element> _members = new(ReferenceEqualityComparer.Instance);

    public void Join(Element element) => _members.Add(element);

    /// <summary>Removes element from the group. Safe/no-op if it never joined.</summary>
    public void Leave(Element element) => _members.Remove(element);

    public bool Contains(Element element) => _members.Contains(element);
}
