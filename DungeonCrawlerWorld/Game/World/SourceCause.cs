using Game.Blueprints;
using Game.Modules.Actions;
using Game.Modules.Inventory;
using Game.Tags;

namespace Game.World;

public enum SourceCauseKind : byte
{
    None,
    Race,
    Class,

    /// <summary>A blueprint part that is neither a race nor a class (Boss, a shrine's own build).</summary>
    Trait,

    Action,
    Spell,
    Item,
}

/// <summary>What an entity source applied an effect through -- its race, a class, a trait, or the action, spell or item it used -- named as it was when the source was created.</summary>
/// <remarks>Recorded on the source's interned identity (EntityIdentity.Cause), so ActionSource stays 8 bytes.</remarks>
public readonly record struct SourceCause(SourceCauseKind Kind, string? Name)
{
    public static readonly SourceCause None = default;

    public static SourceCause Of(BlueprintDefinition part) =>
        new(part.Race is not null ? SourceCauseKind.Race : part.Class is not null ? SourceCauseKind.Class : SourceCauseKind.Trait, part.Name);

    public static SourceCause Of(ActivatableDefinition definition) =>
        new(definition is ItemDefinition ? SourceCauseKind.Item : definition.Tags.Has(GameTags.ActionSpell) ? SourceCauseKind.Spell : SourceCauseKind.Action, definition.Name);
}
