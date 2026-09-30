using Engine.Modules;

namespace Engine.Tags;

/// <summary>Where one module declares the gameplay tags it uses during IModule.DeclareTags.</summary>
/// <cleanupVersion>1</cleanupVersion>
public sealed class GameplayTagDeclarations
{
    private readonly IModule _declaringModule;
    private readonly Dictionary<GameplayTag, string?> _displayNamesByTag;

    internal GameplayTagDeclarations(IModule declaringModule, Dictionary<GameplayTag, string?> displayNamesByTag)
    {
        _declaringModule = declaringModule;
        _displayNamesByTag = displayNamesByTag;
    }

    /// <summary>Declares tag, and each of its parents that isn't declared yet, as usable in this build.</summary>
    /// <remarks>
    /// Several modules may declare the same tag. A null displayName leaves the display name to whichever declaration
    /// gives one, or to the tag's last segment.
    /// </remarks>
    /// <exception cref="InvalidOperationException">tag is None; displayName is empty or whitespace; tag already has a different display name; or a declared tag's name differs from another's only by case.</exception>
    public void Declare(GameplayTag tag, string? displayName = null)
    {
        var moduleName = _declaringModule.Name;

        if (tag.IsNone)
        {
            throw new InvalidOperationException($"{moduleName} declares GameplayTag.None, which is not a tag.");
        }

        if (displayName is not null && string.IsNullOrWhiteSpace(displayName))
        {
            throw new InvalidOperationException($"{moduleName} declares tag '{tag}' with an empty display name.");
        }

        for (var current = tag; !current.IsNone; current = current.Parent)
        {
            if (_displayNamesByTag.ContainsKey(current))
            {
                continue;
            }

            if (_displayNamesByTag.Keys.FirstOrDefault(existing => string.Equals(existing.Name, current.Name, StringComparison.OrdinalIgnoreCase)) is { IsNone: false } caseVariant)
            {
                throw new InvalidOperationException($"{moduleName} declares tag '{current}', which differs from the declared tag '{caseVariant}' only by case.");
            }

            _displayNamesByTag.Add(current, null);
        }

        if (displayName is null)
        {
            return;
        }

        if (_displayNamesByTag[tag] is { } existingDisplayName && existingDisplayName != displayName)
        {
            throw new InvalidOperationException($"{moduleName} declares tag '{tag}' with display name '{displayName}', but it was already declared as '{existingDisplayName}'.");
        }

        _displayNamesByTag[tag] = displayName;
    }
}
