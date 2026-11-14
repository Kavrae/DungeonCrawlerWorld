using Engine.Modules;

namespace Engine.Tags;

/// <summary>The gameplay tags one build's modules declared -- each one's display name and description -- and the domains they declared.</summary>
/// <remarks>
/// Built once from the module set and frozen. A tag outside it may still be interned in the process (another build, a
/// mod that failed validation), but is not usable in this build: code that accepts content checks it here, and a field
/// that holds tags of one role checks them against its domain (Accepts, ProblemWith).
/// </remarks>
/// <cleanupVersion>1</cleanupVersion>
public sealed class GameplayTagRegistry
{
    private readonly Dictionary<GameplayTag, (string DisplayName, string Description)> _tags;
    private readonly Dictionary<GameplayTagDomain, (string Description, GameplayTag[] Accepted)> _domains;

    private GameplayTagRegistry(Dictionary<GameplayTag, (string DisplayName, string Description)> tags, Dictionary<GameplayTagDomain, (string Description, GameplayTag[] Accepted)> domains)
    {
        _tags = tags;
        _domains = domains;
    }

    public IReadOnlyCollection<GameplayTag> DeclaredTags => _tags.Keys;

    public IReadOnlyCollection<GameplayTagDomain> Domains => _domains.Keys;

    /// <summary>Runs every module's DeclareTags, in order, then checks the declarations as a whole.</summary>
    /// <exception cref="InvalidOperationException">A declaration is invalid -- see GameplayTagDeclarations.</exception>
    public static GameplayTagRegistry Declare(IEnumerable<IModule> modules)
    {
        var declarations = new GameplayTagDeclarationSet();
        foreach (var module in modules)
        {
            module.DeclareTags(new GameplayTagDeclarations(module, declarations));
        }

        declarations.Validate();
        return new GameplayTagRegistry(
            declarations.Tags.ToDictionary(entry => entry.Key, entry => (entry.Value.DisplayName ?? entry.Key.LastSegment, entry.Value.Description!)),
            declarations.Domains.ToDictionary(entry => entry.Key, entry => (entry.Value.Description, entry.Value.Accepted.ToArray())));
    }

    public bool IsDeclared(GameplayTag tag) => _tags.ContainsKey(tag);

    public bool IsDeclared(GameplayTagDomain domain) => _domains.ContainsKey(domain);

    /// <summary>The name a player reads for tag: its declared display name, otherwise its last segment ("Fire" for "Damage.Fire").</summary>
    /// <exception cref="InvalidOperationException">tag is not declared in this build.</exception>
    public string GetDisplayName(GameplayTag tag) => Get(tag).DisplayName;

    /// <summary>What tag means, as its declaration describes it.</summary>
    /// <exception cref="InvalidOperationException">tag is not declared in this build.</exception>
    public string GetDescription(GameplayTag tag) => Get(tag).Description;

    /// <summary>What domain is for, as its declaration describes it.</summary>
    /// <exception cref="InvalidOperationException">domain is not declared in this build.</exception>
    public string GetDescription(GameplayTagDomain domain) => GetDomain(domain).Description;

    /// <summary>The tags whose subtrees domain accepts.</summary>
    /// <exception cref="InvalidOperationException">domain is not declared in this build.</exception>
    public IReadOnlyList<GameplayTag> AcceptedBy(GameplayTagDomain domain) => GetDomain(domain).Accepted;

    /// <summary>Whether tag is one domain accepts or under one.</summary>
    /// <exception cref="InvalidOperationException">domain is not declared in this build.</exception>
    public bool Accepts(GameplayTagDomain domain, GameplayTag tag)
    {
        foreach (var accepted in GetDomain(domain).Accepted)
        {
            if (tag.IsSelfOrDescendantOf(accepted))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether tag is under a tag domain accepts, and not one of those tags itself ("Item.Consumable" but not "Item").</summary>
    /// <exception cref="InvalidOperationException">domain is not declared in this build.</exception>
    public bool AcceptsBelowItsRoots(GameplayTagDomain domain, GameplayTag tag)
    {
        foreach (var accepted in GetDomain(domain).Accepted)
        {
            if (tag != accepted && tag.IsSelfOrDescendantOf(accepted))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Why domain doesn't accept tag in this build, as a sentence; null when it does.</summary>
    public string? ProblemWith(GameplayTagDomain domain, GameplayTag tag)
    {
        if (!_domains.TryGetValue(domain, out var entry))
        {
            return $"'{tag}' is used as {Article(domain)} {domain} tag, but no module in this build declares the {domain} domain.";
        }

        return Accepts(domain, tag)
            ? null
            : $"'{tag}' isn't {Article(domain)} {domain} tag: those are under {string.Join(", ", entry.Accepted.Select(accepted => accepted.Name))}.";
    }

    /// <summary>Throws unless every tag in tags is declared in this build.</summary>
    /// <param name="usedBy">What carries tags, for the error message (a definition's name).</param>
    /// <exception cref="InvalidOperationException">A tag in tags is not declared.</exception>
    public void EnsureDeclared(GameplayTagSet tags, string usedBy)
    {
        foreach (var tag in tags)
        {
            if (!IsDeclared(tag))
            {
                throw new InvalidOperationException($"{usedBy} uses gameplay tag '{tag}', which no module in this build declares.");
            }
        }
    }

    /// <summary>Throws unless domain accepts every tag in tags.</summary>
    /// <param name="usedBy">What carries tags, for the error message (a definition's name).</param>
    /// <exception cref="InvalidOperationException">domain doesn't accept a tag in tags, or isn't declared.</exception>
    public void EnsureAccepts(GameplayTagDomain domain, GameplayTagSet tags, string usedBy)
    {
        foreach (var tag in tags)
        {
            if (ProblemWith(domain, tag) is { } problem)
            {
                throw new InvalidOperationException($"{usedBy}: {problem}");
            }
        }
    }

    private (string DisplayName, string Description) Get(GameplayTag tag) =>
        _tags.TryGetValue(tag, out var entry)
            ? entry
            : throw new InvalidOperationException($"Gameplay tag '{tag}' is not declared by any module in this build.");

    private (string Description, GameplayTag[] Accepted) GetDomain(GameplayTagDomain domain) =>
        _domains.TryGetValue(domain, out var entry)
            ? entry
            : throw new InvalidOperationException($"The {domain} gameplay tag domain is not declared by any module in this build.");

    private static string Article(GameplayTagDomain domain) => "AEIOU".Contains(char.ToUpperInvariant(domain.Name[0])) ? "an" : "a";
}
