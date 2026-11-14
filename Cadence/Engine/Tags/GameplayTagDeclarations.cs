using Engine.Modules;

namespace Engine.Tags;

/// <summary>Where one module declares the gameplay tags it uses, the roots it owns and the domains it holds, during IModule.DeclareTags.</summary>
/// <remarks>
/// A root (a one-segment tag) belongs to the module that declares it with DeclareRoot, and so does everything under it:
/// another module may declare a tag under it only below a tag its owner made extensible. Ownership is by module Id, so a
/// module that replaces another takes over its roots and domains. Every tag needs a description. What can be checked
/// on one call is checked there; what depends on every module's declarations (ownership, descriptions of implied
/// parents, extensibility, naming and depth) is checked once all have declared (GameplayTagRegistry.Declare).
/// </remarks>
/// <cleanupVersion>1</cleanupVersion>
public sealed class GameplayTagDeclarations
{
    private readonly IModule _declaringModule;
    private readonly GameplayTagDeclarationSet _declarations;

    internal GameplayTagDeclarations(IModule declaringModule, GameplayTagDeclarationSet declarations)
    {
        _declaringModule = declaringModule;
        _declarations = declarations;
    }

    /// <summary>Declares root as a top-level tag this module owns.</summary>
    /// <param name="extensible">Whether other modules may declare tags under it.</param>
    /// <exception cref="InvalidOperationException">root isn't a single segment; this module has no Id; another module already owns root; or root's description or display name is refused (see Declare).</exception>
    public void DeclareRoot(GameplayTag root, string description, bool extensible = false, string? displayName = null)
    {
        var moduleName = _declaringModule.Name;
        if (root.IsNone || root.Depth != 1)
        {
            throw new InvalidOperationException($"{moduleName} declares '{root}' as a root, but a root is a single segment.");
        }

        if (_declaringModule.Id == Guid.Empty)
        {
            throw new InvalidOperationException($"{moduleName} declares the root '{root}' but has no Id; a module that owns tags needs one.");
        }

        if (_declarations.RootOwners.TryGetValue(root, out var owner) && owner.Id != _declaringModule.Id)
        {
            throw new InvalidOperationException($"{moduleName} declares the root '{root}', which {owner.Name} already owns.");
        }

        _declarations.RootOwners[root] = _declaringModule;
        DeclareTag(root, description, displayName, extensible);
    }

    /// <summary>Declares tag, below a root, as usable in this build; a parent it implies that no module declares fails the build.</summary>
    /// <remarks>
    /// Several modules may declare the same tag, each where its root's owner allows. A null displayName leaves the display
    /// name to whichever declaration gives one, or to the tag's last segment.
    /// </remarks>
    /// <param name="extensible">Whether modules other than its root's owner may declare tags under it; only the owner may set it.</param>
    /// <exception cref="InvalidOperationException">tag is None or a root; description is empty or differs from another declaration's; displayName is empty or differs from another declaration's; or a declared tag's name differs from another's only by case.</exception>
    public void Declare(GameplayTag tag, string description, string? displayName = null, bool extensible = false)
    {
        if (tag.IsNone)
        {
            throw new InvalidOperationException($"{_declaringModule.Name} declares GameplayTag.None, which is not a tag.");
        }

        if (tag.Depth == 1)
        {
            throw new InvalidOperationException($"{_declaringModule.Name} declares '{tag}' with Declare, but it is a root: a module declares its roots with DeclareRoot.");
        }

        DeclareTag(tag, description, displayName, extensible);
    }

    /// <summary>Declares domain, which this module owns, accepting the tags under each of accepted.</summary>
    /// <param name="extensible">Whether other modules may add to what it accepts (AddToDomain).</param>
    /// <exception cref="InvalidOperationException">This module has no Id, the description is empty, or domain is already declared.</exception>
    public void DeclareDomain(GameplayTagDomain domain, string description, bool extensible, IReadOnlyList<GameplayTag> accepted)
    {
        var moduleName = _declaringModule.Name;
        if (_declaringModule.Id == Guid.Empty)
        {
            throw new InvalidOperationException($"{moduleName} declares the {domain} domain but has no Id; a module that owns a domain needs one.");
        }

        if (string.IsNullOrWhiteSpace(description))
        {
            throw new InvalidOperationException($"{moduleName} declares the {domain} domain without a description.");
        }

        if (_declarations.Domains.TryGetValue(domain, out var existing))
        {
            throw new InvalidOperationException($"{moduleName} declares the {domain} domain, which {existing.Owner.Name} already declares.");
        }

        _declarations.Domains.Add(domain, new GameplayTagDeclarationSet.DomainEntry(_declaringModule, description, extensible, [.. accepted]));
    }

    /// <summary>Makes domain also accept the tags under accepted; domain must be this module's, or extensible.</summary>
    public void AddToDomain(GameplayTagDomain domain, GameplayTag accepted) =>
        _declarations.DomainAdditions.Add((domain, accepted, _declaringModule));

    private void DeclareTag(GameplayTag tag, string description, string? displayName, bool extensible)
    {
        var moduleName = _declaringModule.Name;
        if (string.IsNullOrWhiteSpace(description))
        {
            throw new InvalidOperationException($"{moduleName} declares tag '{tag}' without a description.");
        }

        if (displayName is not null && string.IsNullOrWhiteSpace(displayName))
        {
            throw new InvalidOperationException($"{moduleName} declares tag '{tag}' with an empty display name.");
        }

        for (var current = tag; !current.IsNone; current = current.Parent)
        {
            if (_declarations.Tags.ContainsKey(current))
            {
                continue;
            }

            if (_declarations.Tags.Keys.FirstOrDefault(existingTag => string.Equals(existingTag.Name, current.Name, StringComparison.OrdinalIgnoreCase)) is { IsNone: false } caseVariant)
            {
                throw new InvalidOperationException($"{moduleName} declares tag '{current}', which differs from the declared tag '{caseVariant}' only by case.");
            }

            _declarations.Tags.Add(current, new GameplayTagDeclarationSet.TagEntry(_declaringModule));
        }

        var entry = _declarations.Tags[tag];
        if (entry.Description is { } existingDescription && existingDescription != description)
        {
            throw new InvalidOperationException($"{moduleName} declares tag '{tag}' with a description that differs from {entry.DescribedBy!.Name}'s.");
        }

        if (displayName is not null && entry.DisplayName is { } existingDisplayName && existingDisplayName != displayName)
        {
            throw new InvalidOperationException($"{moduleName} declares tag '{tag}' with display name '{displayName}', but it was already declared as '{existingDisplayName}'.");
        }

        entry.Description = description;
        entry.DescribedBy ??= _declaringModule;
        entry.DisplayName ??= displayName;
        if (extensible)
        {
            entry.MadeExtensibleBy = _declaringModule;
        }

        _declarations.Declarations.Add((tag, _declaringModule));
    }
}

/// <summary>Everything a build's modules declared in DeclareTags, before it is checked as a whole and frozen into a GameplayTagRegistry.</summary>
internal sealed class GameplayTagDeclarationSet
{
    public const int MaximumDepth = 4;

    internal sealed class TagEntry(IModule firstDeclaredBy)
    {
        public IModule FirstDeclaredBy { get; } = firstDeclaredBy;

        public string? Description { get; set; }

        public IModule? DescribedBy { get; set; }

        public string? DisplayName { get; set; }

        public IModule? MadeExtensibleBy { get; set; }
    }

    internal sealed record DomainEntry(IModule Owner, string Description, bool Extensible, List<GameplayTag> Accepted);

    public Dictionary<GameplayTag, TagEntry> Tags { get; } = [];

    public List<(GameplayTag Tag, IModule Module)> Declarations { get; } = [];

    public Dictionary<GameplayTag, IModule> RootOwners { get; } = [];

    public Dictionary<GameplayTagDomain, DomainEntry> Domains { get; } = [];

    public List<(GameplayTagDomain Domain, GameplayTag Accepted, IModule Module)> DomainAdditions { get; } = [];

    /// <summary>Checks what can only be checked once every module has declared, and adds every domain addition.</summary>
    /// <exception cref="InvalidOperationException">The first rule broken, naming the module and tag.</exception>
    public void Validate()
    {
        foreach (var (tag, entry) in Tags)
        {
            if (entry.Description is null)
            {
                throw new InvalidOperationException($"{entry.FirstDeclaredBy.Name} declares a tag under '{tag}', but no module declares '{tag}' itself, with a description.");
            }

            if (tag.Depth > MaximumDepth)
            {
                throw new InvalidOperationException($"{entry.FirstDeclaredBy.Name} declares '{tag}', {tag.Depth} segments deep; a tag is at most {MaximumDepth}.");
            }

            if (NonPascalCaseSegment(tag) is { } segment)
            {
                throw new InvalidOperationException($"{entry.FirstDeclaredBy.Name} declares '{tag}', whose segment '{segment}' isn't PascalCase (a capital letter, then letters and digits).");
            }

            if (entry.MadeExtensibleBy is { } opener && OwnerOf(tag, entry.FirstDeclaredBy) is var owner && owner.Id != opener.Id)
            {
                throw new InvalidOperationException($"{opener.Name} makes '{tag}' extensible, but only {owner.Name}, which owns '{RootOf(tag)}', may.");
            }
        }

        foreach (var (tag, module) in Declarations)
        {
            var owner = OwnerOf(tag, module);
            if (owner.Id != module.Id && !IsOpenBelow(tag))
            {
                throw new InvalidOperationException($"{module.Name} declares '{tag}' under '{RootOf(tag)}', which {owner.Name} owns and doesn't open to other modules there.");
            }
        }

        foreach (var (domain, accepted, module) in DomainAdditions)
        {
            if (!Domains.TryGetValue(domain, out var entry))
            {
                throw new InvalidOperationException($"{module.Name} adds '{accepted}' to the {domain} domain, which no module declares.");
            }

            if (entry.Owner.Id != module.Id && !entry.Extensible)
            {
                throw new InvalidOperationException($"{module.Name} adds '{accepted}' to the {domain} domain, which {entry.Owner.Name} owns and doesn't let other modules extend.");
            }

            entry.Accepted.Add(accepted);
        }

        foreach (var (domain, entry) in Domains)
        {
            foreach (var accepted in entry.Accepted)
            {
                if (!Tags.ContainsKey(accepted))
                {
                    throw new InvalidOperationException($"The {domain} domain accepts '{accepted}', which no module declares.");
                }
            }
        }
    }

    private IModule OwnerOf(GameplayTag tag, IModule declaredBy)
    {
        var root = RootOf(tag);
        return RootOwners.TryGetValue(root, out var owner)
            ? owner
            : throw new InvalidOperationException($"{declaredBy.Name} declares '{tag}', but no module declares its root '{root}' (DeclareRoot).");
    }

    /// <summary>Whether a proper ancestor of tag -- its root included -- was made extensible.</summary>
    private bool IsOpenBelow(GameplayTag tag)
    {
        for (var ancestor = tag.Parent; !ancestor.IsNone; ancestor = ancestor.Parent)
        {
            if (Tags.TryGetValue(ancestor, out var entry) && entry.MadeExtensibleBy is not null)
            {
                return true;
            }
        }

        return false;
    }

    private static GameplayTag RootOf(GameplayTag tag)
    {
        var root = tag;
        while (root.Depth > 1)
        {
            root = root.Parent;
        }

        return root;
    }

    private static string? NonPascalCaseSegment(GameplayTag tag)
    {
        foreach (var segment in tag.Name.Split('.'))
        {
            if (!char.IsAsciiLetterUpper(segment[0]) || !segment.All(char.IsAsciiLetterOrDigit))
            {
                return segment;
            }
        }

        return null;
    }
}
