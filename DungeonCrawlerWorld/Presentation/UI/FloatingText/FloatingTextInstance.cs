using Game.Modules.StatusEffects;
using Game.World;
using Microsoft.Xna.Framework;

namespace Presentation.UI.FloatingText;

/// <summary>One floating text on screen.</summary>
/// <param name="SpawnTilePosition">Where it appeared, in map tile coordinates; FloatingTextMotion offsets it from here.</param>
/// <param name="HorizontalDirection">-1 drifts left, 1 drifts right.</param>
/// <param name="VerticalDirection">-1 rises, 1 falls.</param>
public record struct FloatingTextInstance(
    Vector2 SpawnTilePosition,
    int MapLayer,
    FloatingTextKind Kind,
    ushort Amount,
    StatusEffectType EffectType,
    FloatingTextFlags Flags,
    int AgeFrames,
    sbyte HorizontalDirection,
    sbyte VerticalDirection);
