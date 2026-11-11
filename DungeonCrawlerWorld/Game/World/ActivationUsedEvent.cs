using Engine.Tags;
using Game.Modules.Actions;

namespace Game.World;

/// <summary>Published once for every use of an action or an item that goes ahead: when its effects land, or when a toggle flips.</summary>
/// <remarks>
/// One event for both, so a reader that cares about something being used never misses one kind. A reader filters on
/// what it means: an id (the Dodge step), a kind, or Tags (Spell Caster reads Spell). An Immediate or FreeCast use
/// publishes it as it activates, a Delayed one when its windup resolves. Immediate rather than IBufferedEvent: a reader
/// writes only to pools other than the one the publishing system is scanning.
/// </remarks>
/// <param name="EntityId">Who used it.</param>
/// <param name="Kind">An action or an item.</param>
/// <param name="DefinitionId">The action's id, or the item definition's id (an item's stack may already be gone).</param>
/// <param name="Tags">The definition's tags, implied ones included.</param>
public readonly record struct ActivationUsedEvent(int EntityId, ActivatableKind Kind, Guid DefinitionId, GameplayTagSet Tags);
