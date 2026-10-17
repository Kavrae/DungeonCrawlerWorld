using Engine.Modules;

namespace Engine.Tags;

/// <summary>The gameplay tags one build's modules declared, and each one's display name.</summary>
/// <remarks>
/// Built once from the module set and frozen. A tag outside it may still be interned in the process (another build, a
/// mod that failed validation), but is not usable in this build: code that accepts content checks it here.
/// </remarks>
/// <cleanupVersion>1</cleanupVersion>
public sealed class GameplayTagRegistry
{
    private readonly Dictionary<GameplayTag, string> _displayNamesByTag;

    private GameplayTagRegistry(Dictionary<GameplayTag, string> displayNamesByTag) => _displayNamesByTag = displayNamesByTag;

    public IReadOnlyCollection<GameplayTag> DeclaredTags => _displayNamesByTag.Keys;

    /// <summary>Runs every module's DeclareTags, in order.</summary>
    /// <exception cref="InvalidOperationException">A declaration is invalid -- see GameplayTagDeclarations.Declare.</exception>
    public static GameplayTagRegistry Declare(IEnumerable<IModule> modules)
    {
        var declaredDisplayNamesByTag = new Dictionary<GameplayTag, string?>();

        foreach (var module in modules)
        {
            module.DeclareTags(new GameplayTagDeclarations(module, declaredDisplayNamesByTag));
        }

        return new GameplayTagRegistry(declaredDisplayNamesByTag.ToDictionary(entry => entry.Key, entry => entry.Value ?? entry.Key.LastSegment));
    }

    public bool IsDeclared(GameplayTag tag) => _displayNamesByTag.ContainsKey(tag);

    /// <summary>The name a player reads for tag: its declared display name, otherwise its last segment ("Fire" for "Damage.Fire").</summary>
    /// <exception cref="InvalidOperationException">tag is not declared in this build.</exception>
    public string GetDisplayName(GameplayTag tag) =>
        _displayNamesByTag.TryGetValue(tag, out var displayName)
            ? displayName
            : throw new InvalidOperationException($"Gameplay tag '{tag}' is not declared by any module in this build.");

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
}
