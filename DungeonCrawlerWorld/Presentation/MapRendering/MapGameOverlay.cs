using Engine.Math;
using Game.Modules.Actions;
using Game.Views;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

using Presentation.UI;
using Presentation.UI.Chrome;
using Presentation.UI.ColorPalettes;
using UIEngine.Fonts;
using UIEngine.Rendering;
using UIEngine.UI;

namespace Presentation.MapRendering;

/// <summary>Draws what the player is doing on the map over the world MapRenderer drew: targeting and footprint highlights, windup charge fills, Dodge's WASD hints, and the selected tile's or followed entity's glow.</summary>
/// <remarks>
/// The game's overlay, not the map's: it reads the player's targeting and inspection state (ActionTargetingController,
/// MapViewState). Everything shown on tiles is written into MapOverlayTexels each frame and drawn as one quad over the visible
/// grid through the sprite effect's tile overlay; a tile outside the grid is ignored. Dodge's letters are text, drawn after.
/// </remarks>
public sealed class MapGameOverlay : IDisposable
{
    private const float TargetSelectionMaskAlpha = 0.5f;

    /// <summary>Fraction of TargetSelectionMaskAlpha used for the always-visible full-tile backdrop drawn under the growing charge fill -- keeps a multi-tile Delayed action's whole target shape legible from the first frame of the windup, not just once each tile's own fill has grown enough to be seen.</summary>
    private const float ChargeBackdropAlphaFraction = 0.3f;

    private readonly IMapViewQuery _mapView;
    private readonly MapViewState _mapViewState;
    private readonly MapCamera _camera;
    private readonly ActionTargetingController _actionTargeting;
    private readonly FontStashTextMeasurer _badgeFontMeasurer;
    /// <summary>How many textures the overlay rotates through, so a frame's upload never writes a texture an earlier frame still in flight on the GPU reads.</summary>
    private const int TexturesInFlight = 3;

    private readonly MapOverlayTexels _texels = new();
    private readonly Texture2D?[] _textures = new Texture2D?[TexturesInFlight];
    private int _nextTextureIndex;

    public MapGameOverlay(IMapViewQuery mapView, MapViewState mapViewState, MapCamera camera, ActionTargetingController actionTargeting, FontService fontService)
    {
        _mapView = mapView;
        _mapViewState = mapViewState;
        _camera = camera;
        _actionTargeting = actionTargeting;
        _badgeFontMeasurer = fontService.GetTextMeasurer(fontService.GetFont(GameFontChrome.MapBadgeFontSize));
    }

    /// <summary>Draws the targeting highlights and the selection's -- the followed entity's in Detail inspection, the selected tile's otherwise -- as one tile overlay, then Dodge's letters.</summary>
    public void Draw(SpriteEffectBatch spriteBatch, Texture2D unitRectangle)
    {
        _texels.Begin(_camera.CurrentScrollPosition, _camera.TileColumns, _camera.TileRows);
        AddTargetingHighlights();

        if (_mapViewState.InspectionMode == InspectionMode.Detail)
        {
            AddFollowedEntityHighlight();
        }
        else
        {
            AddSelectedTileHighlight();
        }

        if (!_texels.IsEmpty)
        {
            DrawTileOverlay(spriteBatch, unitRectangle);
        }

        DrawDodgeDirectionalHints(spriteBatch);
    }

    public void Dispose()
    {
        for (var index = 0; index < _textures.Length; index++)
        {
            _textures[index]?.Dispose();
            _textures[index] = null;
        }
    }

    private void DrawTileOverlay(SpriteEffectBatch spriteBatch, Texture2D unitRectangle)
    {
        var texture = _textures[_nextTextureIndex];
        if (texture is null || texture.Width != _texels.Width || texture.Height != _texels.Height)
        {
            texture?.Dispose();
            texture = new Texture2D(spriteBatch.GraphicsDevice, _texels.Width, _texels.Height);
            _textures[_nextTextureIndex] = texture;
        }

        _nextTextureIndex = (_nextTextureIndex + 1) % TexturesInFlight;
        texture.SetData(_texels.Texels, 0, _texels.Width * _texels.Height);

        var tileSize = _camera.CurrentTileSize;
        var gridOrigin = _camera.TileOrigin(0, 0);
        var gridSize = new Vector2(_texels.Columns * tileSize.X, _texels.Rows * tileSize.Y);
        spriteBatch.TileOverlay = SpriteEffectTileOverlay.OverTileGrid(texture, tileSize, gridOrigin);
        try
        {
            spriteBatch.Draw(unitRectangle, gridOrigin, null, Color.White, 0f, Vector2.Zero, gridSize, SpriteEffects.None, new SpriteDrawStyle(SpriteDrawMode.TileOverlay));
        }
        finally
        {
            spriteBatch.TileOverlay = SpriteEffectTileOverlay.None;
        }
    }

    private bool TryGetPlayerPosition(out Vector3Int position)
    {
        var found = _mapView.TryGetOccupant(_mapView.PlayerEntityId, out var player);
        position = player.Position;
        return found;
    }

    /// <summary>
    /// Every tile the currently-armed ability could be aimed at (see MapViewState.TargetableTiles,
    /// computed once at arm time) -- a 50% CombatTargetPalette.PlayerArmColor wash for "targetable, not
    /// currently hovered," a 50% PlayerTargetColor wash for whichever of those tiles the armed
    /// shape's hover-resolved footprint (see ActionTargetingController.HoveredFootprint,
    /// recomputed every Update) actually covers right now. Conceptually distinct from
    /// AddSelectedTileHighlight below (ability targeting vs. the inspector's click-to-select).
    ///
    /// A second pass then adds whatever's left of HoveredFootprint that TargetableTiles didn't
    /// already cover -- needed for Burst: TargetableTiles is capped strictly at the action's own
    /// Range (ActionTargetingController.ComputeTargetableTiles' reachable-area scatter), but
    /// TargetShapeResolver.ResolveBurst's actual hit footprint is AreaSize-radius around the
    /// anchor tile once that anchor passes the Range check -- i.e. the real splash can (and
    /// often does) reach past Range even though the anchor itself never could. Without this pass
    /// the highlight understated what the ability actually hits. Safe to always run: every shape
    /// only ever populates HoveredFootprint from an in-range anchor, so this never marks a tile
    /// that wouldn't actually be hit.
    ///
    /// Once a Delayed ability is actually queued, Disarm already clears TargetableTiles (there's
    /// nothing left to aim), but the player benefits from still seeing exactly which tiles are
    /// about to be hit once the windup ends -- so this falls back to
    /// ActionTargetingController.PendingWindupTargetTiles (the already-resolved, locked-in
    /// footprint) as a charge fill in the confirmed-target green, for as long as that pending action
    /// exists. Separately (and unconditionally, every frame), every OTHER entity's own in-flight
    /// Delayed windup is telegraphed too, in red or yellow depending on whether it's Dodgeable --
    /// see CombatTargetPalette and ActionTargetingController.AllPendingWindupTargets.
    /// </summary>
    private void AddTargetingHighlights()
    {
        if (_mapViewState.TargetableTiles is { Count: > 0 } targetableTiles)
        {
            foreach (var tile in targetableTiles)
            {
                var washColor = _actionTargeting.HoveredFootprintContains(tile) ? CombatTargetPalette.PlayerTargetColor : CombatTargetPalette.PlayerArmColor;
                _texels.AddWash(tile.X, tile.Y, washColor * TargetSelectionMaskAlpha);
            }

            foreach (var tile in _actionTargeting.HoveredFootprint)
            {
                if (!targetableTiles.Contains(tile))
                {
                    _texels.AddWash(tile.X, tile.Y, CombatTargetPalette.PlayerTargetColor * TargetSelectionMaskAlpha);
                }
            }
        }
        else if (_actionTargeting.PendingWindupTargetTiles is { } pendingTargetTiles)
        {
            var fillFraction = _mapView.GetChargeFraction(_mapView.PlayerEntityId);
            foreach (var tile in pendingTargetTiles)
            {
                AddChargeFill(tile.X, tile.Y, CombatTargetPalette.PlayerTargetColor, fillFraction);
            }
        }

        // The player's own telegraph is already added above (dark green), so it's skipped here.
        // AllPendingWindupTargets itself already scopes this to the Local processing tier.
        foreach (var (entityId, targetTiles, isDodgeable) in _actionTargeting.AllPendingWindupTargets())
        {
            if (entityId == _mapView.PlayerEntityId)
            {
                continue;
            }

            var fillColor = isDodgeable ? CombatTargetPalette.EnemyDodgeableColor : CombatTargetPalette.EnemyUndodgeableColor;
            var fillFraction = _mapView.GetChargeFraction(entityId);
            foreach (var tile in targetTiles)
            {
                AddChargeFill(tile.X, tile.Y, fillColor, fillFraction);
            }
        }
    }

    /// <summary>WASD's own screen-direction offsets from the player -- matches ActionTargetingController.TryClaimDodgeDirectionalKey's identical mapping.</summary>
    private static readonly (Vector3Int Offset, string Label)[] DodgeDirectionalHints =
    [
        (new Vector3Int(0, -1, 0), "W"),
        (new Vector3Int(0, 1, 0), "S"),
        (new Vector3Int(-1, 0, 0), "A"),
        (new Vector3Int(1, 0, 0), "D"),
    ];

    /// <summary>
    /// While Dodge is armed, labels each of its four cardinal reachable tiles with the WASD key
    /// that confirms toward it -- the only action whose confirm can come from a movement key
    /// instead of its own hotkey (ActionTargetingController.TryClaimDodgeDirectionalKey), so it
    /// needs its own affordance the player wouldn't otherwise expect. Diagonal neighbors are still
    /// reachable (click, or same-key-then-click), just not via a single key -- no hint drawn there.
    /// </summary>
    private void DrawDodgeDirectionalHints(SpriteEffectBatch spriteBatch)
    {
        if (_mapViewState.ArmedActionId != BuiltInActionIds.Dodge || !TryGetPlayerPosition(out var playerPosition))
        {
            return;
        }

        foreach (var (offset, label) in DodgeDirectionalHints)
        {
            var tile = playerPosition + offset;
            if (!_camera.TryGetTileRectangle(tile.X, tile.Y, out var tileRectangle))
            {
                continue;
            }

            LabelRenderer.DrawCentered(spriteBatch, _badgeFontMeasurer, label, new Vector2(tileRectangle.X, tileRectangle.Y), new Vector2(tileRectangle.Width, tileRectangle.Height), Color.White, outline: true);
        }
    }

    private void AddSelectedTileHighlight()
    {
        if (_mapViewState.SelectedMapNodePosition is not { } selectedPosition)
        {
            return;
        }

        AddSelectedTileGlow(selectedPosition.X, selectedPosition.Y);
    }

    /// <summary>
    /// Detail/Admin inspection's own highlight -- tracks the followed entity's live
    /// TransformComponent.Position every frame instead of a frozen tile position (see
    /// AddSelectedTileHighlight, Basic mode's equivalent), so the highlight moves with the
    /// entity as it walks. Highlights every tile of the entity's own footprint (TransformComponent.
    /// Size), not just its origin tile -- a multi-tile (Huge) entity should read as fully
    /// selected, the same footprint MapRenderer draws the entity's sprite across.
    /// </summary>
    private void AddFollowedEntityHighlight()
    {
        var entityId = _mapViewState.InspectedEntityId;
        if (entityId == -1 || !_mapView.TryGetOccupant(entityId, out var followed))
        {
            return;
        }

        for (var offsetX = 0; offsetX < followed.Size.X; offsetX++)
        {
            for (var offsetY = 0; offsetY < followed.Size.Y; offsetY++)
            {
                AddSelectedTileGlow(followed.Position.X + offsetX, followed.Position.Y + offsetY);
            }
        }
    }

    /// <summary>
    /// A charging Delayed action's own per-tile telegraph -- an always-visible, dim full-tile
    /// backdrop (ChargeBackdropAlphaFraction of the targeting wash's alpha, so a multi-tile target
    /// shape still reads as one coherent zone from the first frame of the windup) plus a brighter
    /// bottom-up fill on top that grows with fillFraction, reaching the targeting wash's full alpha
    /// -- and its own full-tile coverage -- exactly as the action activates.
    /// </summary>
    private void AddChargeFill(int mapNodeX, int mapNodeY, Color fillColor, float fillFraction)
    {
        _texels.AddWash(mapNodeX, mapNodeY, fillColor * (TargetSelectionMaskAlpha * ChargeBackdropAlphaFraction));
        _texels.AddFill(mapNodeX, mapNodeY, fillColor * TargetSelectionMaskAlpha, fillFraction);
    }

    /// <summary>The inspector's own "this tile is selected" highlight -- a light-blue inward glow, the same as GlowRenderer's InteriorFade (inventory cells use it too, so the two read as the same visual language), so terrain and sprites underneath stay legible rather than tinted.</summary>
    private void AddSelectedTileGlow(int mapNodeX, int mapNodeY) =>
        _texels.SetRing(mapNodeX, mapNodeY, GameWindowPalette.Selected * GlowRenderer.FadeMaximumAlpha, GlowRenderer.FadeRingCount);
}
