using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Engine.ECS.Systems;
using Engine.Events;
using Engine.Math;
using Game.Modules.Core.Components;
using Game.Modules.StatusEffectAura.Components;
using Game.World;
using Microsoft.Xna.Framework;

namespace Presentation.UI;

/// <summary>
/// Incrementally maintained (constructor scatters every source once, then AddSource/RemoveSource
/// keep it in sync one source at a time), not scanned live per background-cache rebuild: every
/// visible-tile cache rebuild used to iterate every StatusEffectAuraSourceComponent in the
/// whole game (fine for a handful of sources, catastrophic with TestMapBuilder's real lava
/// density -- tens of thousands of sources scanned on every player move tanked FPS to ~1).
/// Sparse (only cells actually within some source's radius have an entry). Where multiple
/// sources overlap a cell, their colors are blended by a falloff-weighted average rather than
/// the sequential per-source Color.Lerp chain an earlier, unscalable version of this used.
/// Not to be confused with Game.Modules.StatusEffectAura.AuraGrid -- that's the gameplay-side,
/// incrementally-updated per-effect-type stack index; this is the presentation-only, blended-
/// color equivalent. Both share the same chain-walk-and-splat mechanics (initial bulk scatter,
/// and per-move remove-old/add-new) via Engine.ECS.Systems.SourceSplatting -- only the
/// accumulation itself (an int stack total there vs. a weighted RGB sum here) stays separate,
/// since that math has nothing in common between the two. Reacts to the same
/// AuraSourceAddedEvent/AuraSourceRemovedEvent StatusEffectAuraSystem does, so a source that
/// appears/disappears outside of blueprint-time population -- see AuraSourceEffects.Toggle --
/// shows up here too, not just in the gameplay grid. Deliberately does NOT mirror
/// StatusEffectAuraSystem's own ProcessingTier-gated deferral (see that class's own
/// ResyncSourceIfStale) -- this is a purely cosmetic overlay with no per-move O(radius^2) cost
/// concern anywhere near the scale that tiering exists to bound, so every resync here stays
/// immediate regardless of the source's distance from the player.
///
/// Also reacts to EntityMovedEvent, needed the moment a player carries a
/// toggled-on item (e.g. Toxic Idol): without this, OnSourceRemoved's later retraction would
/// read the entity's then-current position, not wherever the glow was actually last splatted,
/// leaving a permanent ghost tint at the toggle-on position and no tint at all following the
/// player. EntityMovedEvent is published on the shared EventBus for the player's own move and,
/// as of MovementSystem's own auraSources soft dependency, for any OTHER mover that itself
/// carries a StatusEffectAuraSourceComponent too (see MovementSystem's own doc comment) --
/// every other entity's move only reaches StatusEffectAuraSystem via the separate
/// FrameEventBuffer it drains itself, never this class, since a plain mover carrying no source
/// of its own has nothing here to chain-walk. Consumed directly by MapWindow.DrawGlowOverlay as a translucent rect drawn on top of terrain/
/// occupant sprites, not blended into MapBackgroundCache's background color -- an opaque sprite
/// drawn over that background would otherwise hide the tint entirely.
/// </summary>
public sealed class MapTintGrid
{
    // Normalizes DistanceFalloff.ValueAtDistance(source.Strength, distance) into a 0-1 Color.Lerp
    // factor -- a source at distance 0 with Strength >= this shows its TintColor at full strength.
    private const int MaxTintStrength = 8;

    // Raw falloff-weighted RGB sum plus total weight per cell -- (Color, Factor) is derived from
    // this on every TryGetTint call rather than cached separately, since deriving it is a cheap
    // O(1) division/cast (the perf hazard this class exists to avoid was iterating every SOURCE
    // per query, not the trivial per-cell finalization math) and keeping only one dictionary
    // means AddSource/RemoveSource never risk drifting out of sync with a second, derived cache.
    private readonly Dictionary<int, (float R, float G, float B, float Weight)> _weightedSumsByCellIndex = [];
    private readonly Vector3Int _mapSize;
    private readonly DirectComponentPool<TransformComponent> _transforms;
    private readonly MultiComponentPool<StatusEffectAuraSourceComponent> _sources;

    public MapTintGrid(ComponentManager componentManager, Vector3Int mapSize, EventBus eventBus)
    {
        _mapSize = mapSize;
        _transforms = componentManager.GetDirectPool<TransformComponent>();
        _sources = componentManager.GetMultiPool<StatusEffectAuraSourceComponent>();

        SourceSplatting.ScatterAll(_sources, TryGetTransformPosition, (_, source, position) => AddSource(position, source));

        eventBus.Subscribe<AuraSourceAddedEvent>(OnSourceAdded);
        eventBus.Subscribe<AuraSourceRemovedEvent>(OnSourceRemoved);
        eventBus.Subscribe<EntityMovedEvent>(OnEntityMoved);
    }

    public bool TryGetTint(int mapNodeX, int mapNodeY, int mapLayer, out (Color Color, float Factor) tint)
    {
        if (!_weightedSumsByCellIndex.TryGetValue(new Vector3Int(mapNodeX, mapNodeY, mapLayer).FlatIndex(_mapSize), out var accumulated))
        {
            tint = default;
            return false;
        }

        tint = (
            new Color((byte)(accumulated.R / accumulated.Weight), (byte)(accumulated.G / accumulated.Weight), (byte)(accumulated.B / accumulated.Weight)),
            MathUtility.ClampInt((int)accumulated.Weight, 0, MaxTintStrength) / (float)MaxTintStrength);
        return true;
    }

    private void OnSourceAdded(AuraSourceAddedEvent added)
    {
        if (_transforms.TryGetReadonly(added.EntityId, out var transform))
        {
            AddSource(transform.Position, added.Source);
        }
    }

    private void OnSourceRemoved(AuraSourceRemovedEvent removed)
    {
        if (_transforms.TryGetReadonly(removed.EntityId, out var transform))
        {
            RemoveSource(transform.Position, removed.Source);
        }
    }

    /// <summary>Chain-walks every source the mover carries -- see this class's own doc comment for why this exists and why it's currently only reachable for the player's own (or another aura-carrying entity's) move.</summary>
    private void OnEntityMoved(EntityMovedEvent moved) =>
        SourceSplatting.ResyncEntity(_sources, moved.EntityId, moved.OldPosition, moved.NewPosition,
            unsplat: (source, position) => RemoveSource(position, source),
            splat: (source, position) => AddSource(position, source));

    private Vector3Int? TryGetTransformPosition(int entityId) =>
        _transforms.TryGetReadonly(entityId, out var transform) ? transform.Position : null;

    private void AddSource(Vector3Int sourcePosition, StatusEffectAuraSourceComponent source) => Splat(sourcePosition, source, sign: 1);

    private void RemoveSource(Vector3Int sourcePosition, StatusEffectAuraSourceComponent source) => Splat(sourcePosition, source, sign: -1);

    /// <summary>Scatters (sign: 1) or unscatters (sign: -1) source's own falloff-weighted contribution through DistanceFalloff.ScatterManhattan -- the same falloff shape StatusEffectAuraSystem/AuraGrid use on the gameplay side, defined in exactly one place, so glow always visually matches actual aura reach (both read the same AuraAndGlowStrength).</summary>
    private void Splat(Vector3Int sourcePosition, StatusEffectAuraSourceComponent source, int sign)
    {
        DistanceFalloff.ScatterManhattan(sourcePosition, DistanceFalloff.MaxRadius(source.AuraAndGlowStrength), source.AuraAndGlowStrength, FalloffShape.Fading, _mapSize, (cellPosition, weight) =>
        {
            var index = cellPosition.FlatIndex(_mapSize);
            _weightedSumsByCellIndex.TryGetValue(index, out var accumulated);
            (float R, float G, float B, float Weight) updated = (
                accumulated.R + sign * source.GlowColor.R * weight,
                accumulated.G + sign * source.GlowColor.G * weight,
                accumulated.B + sign * source.GlowColor.B * weight,
                accumulated.Weight + sign * weight);

            // Remove rather than store a zero -- keeps the dictionary's size proportional to
            // cells actually under some source's influence right now, not to every cell any
            // source has ever touched (mirrors AuraGrid.Splat's identical reasoning).
            if (updated.Weight <= 0f)
            {
                _weightedSumsByCellIndex.Remove(index);
            }
            else
            {
                _weightedSumsByCellIndex[index] = updated;
            }
        });
    }
}
