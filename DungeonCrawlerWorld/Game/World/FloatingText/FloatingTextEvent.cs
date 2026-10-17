using Engine.Math;
using Game.Modules.StatusEffects;

namespace Game.World;

/// <summary>Published by FloatingTextFeed when something worth a floating text happens to an entity the player can see.</summary>
/// <remarks>Position and Size are the entity's footprint when the event was published, so the text stays where it happened even if the entity moves or is destroyed. EffectType only means something for StatusEffectStacksAdded and Immune.</remarks>
public readonly record struct FloatingTextEvent(
    int EntityId,
    FloatingTextKind Kind,
    ushort Amount,
    Vector3Int Position,
    Vector2Byte Size,
    StatusEffectType EffectType = default,
    FloatingTextFlags Flags = FloatingTextFlags.None);
