namespace Game.Modules.StatModifiers;

/// <summary>
/// Published by StatModifierExpirySystem for an entity immediately before it removes any of that
/// entity's due modifiers -- the counterpart to StatModifierExpiredEvent, which reports each one
/// after the fact.
/// </summary>
/// <remarks>
/// For a subscriber that needs the entity's modifier state as it was, which the after-the-fact
/// event cannot give it: HealthModule snapshots the entity's MaximumHealth sums here so that the
/// matching StatModifierExpiredEvent can work out how far the effective maximum moved. Published
/// once per entity per sweep regardless of how many modifiers are due, so a snapshot taken here
/// covers the whole batch rather than one modifier of it.
/// </remarks>
public readonly record struct StatModifierExpiringEvent(int EntityId);
