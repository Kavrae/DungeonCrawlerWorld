using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Engine.Math;
using Game.Modules.Core.Components;
using Game.Modules.StatusEffectAura.Components;
using Microsoft.Xna.Framework;

namespace Presentation.UI;

/// <summary>
/// Precomputed once (constructor), not scanned live per background-cache rebuild: every
/// visible-tile cache rebuild used to iterate every StatusEffectAuraSourceComponent in the
/// whole game (fine for a handful of sources, catastrophic with TestMapBuilder's real lava
/// density -- tens of thousands of sources scanned on every player move tanked FPS to ~1).
/// Sparse (only cells actually within some source's radius have an entry) since
/// StatusEffectAuraSourceComponent is terrain-anchored and static once placed. Where multiple
/// sources overlap a cell, their colors are blended by a falloff-weighted average rather than
/// the sequential per-source Color.Lerp chain an earlier, unscalable version of this used.
/// Not to be confused with Game.Modules.StatusEffectAura.AuraGrid -- that's the gameplay-side,
/// incrementally-updated per-effect-type stack index; this is a presentation-only, static-once
/// blended-color lookup for MapBackgroundCache's rendering.
/// </summary>
public sealed class MapTintGrid
{
    // Normalizes DistanceFalloff.ValueAtDistance(source.Strength, distance) into a 0-1 Color.Lerp
    // factor -- a source at distance 0 with Strength >= this shows its TintColor at full strength.
    private const int MaxTintStrength = 8;

    private readonly Dictionary<int, (Color Color, float Factor)> _tintByCellIndex;
    private readonly Vector3Int _mapSize;

    public MapTintGrid(ComponentManager componentManager, Vector3Int mapSize)
    {
        _mapSize = mapSize;
        _tintByCellIndex = Build(componentManager, mapSize);
    }

    public bool TryGetTint(int mapNodeX, int mapNodeY, int mapLayer, out (Color Color, float Factor) tint) =>
        _tintByCellIndex.TryGetValue(new Vector3Int(mapNodeX, mapNodeY, mapLayer).FlatIndex(_mapSize), out tint);

    /// <summary>
    /// One-time scatter over every StatusEffectAuraSourceComponent. Accumulates a
    /// falloff-weighted RGB sum plus total weight per affected cell, then finalizes each into a
    /// single (blended Color, 0-1 factor) pair. Scatters through DistanceFalloff.ScatterManhattan
    /// -- the same falloff shape StatusEffectAuraSystem/AuraGrid use on the gameplay side,
    /// defined in exactly one place, so glow always visually matches actual aura reach (both
    /// read the same AuraAndGlowStrength).
    /// </summary>
    private static Dictionary<int, (Color Color, float Factor)> Build(ComponentManager componentManager, Vector3Int mapSize)
    {
        var auraSourcePool = componentManager.GetPackedPool<StatusEffectAuraSourceComponent>();
        var transformPool = componentManager.GetDirectPool<TransformComponent>();

        var weightedSums = new Dictionary<int, (float R, float G, float B, float Weight)>();

        var entityIds = auraSourcePool.EntityIds;
        var auraSources = auraSourcePool.Components;
        for (var i = 0; i < entityIds.Length; i++)
        {
            if (!transformPool.TryGetReadonly(entityIds[i], out var transform))
            {
                continue;
            }

            var auraSource = auraSources[i];
            var sourcePosition = transform.Position;

            DistanceFalloff.ScatterManhattan(sourcePosition, auraSource.AuraAndGlowStrength, mapSize, (cellPosition, weight) =>
            {
                var index = cellPosition.FlatIndex(mapSize);
                weightedSums.TryGetValue(index, out var accumulated);
                weightedSums[index] = (
                    accumulated.R + auraSource.GlowColor.R * weight,
                    accumulated.G + auraSource.GlowColor.G * weight,
                    accumulated.B + auraSource.GlowColor.B * weight,
                    accumulated.Weight + weight);
            });
        }

        var tintGrid = new Dictionary<int, (Color Color, float Factor)>(weightedSums.Count);
        foreach (var (index, accumulated) in weightedSums)
        {
            var blendedColor = new Color(
                (byte)(accumulated.R / accumulated.Weight),
                (byte)(accumulated.G / accumulated.Weight),
                (byte)(accumulated.B / accumulated.Weight));
            var factor = MathUtility.ClampInt((int)accumulated.Weight, 0, MaxTintStrength) / (float)MaxTintStrength;

            tintGrid[index] = (blendedColor, factor);
        }

        return tintGrid;
    }
}
