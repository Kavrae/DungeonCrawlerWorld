using Engine.Math;
using FontStashSharp;
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
/// <remarks>The game's overlay, not the map's: it reads the player's targeting and inspection state (ActionTargetingController, MapViewState). Every highlight is placed by the camera's tile rectangles and no-ops for a tile outside the visible grid.</remarks>
public sealed class MapGameOverlay
{
    private const float TargetSelectionMaskAlpha = 0.5f;

    /// <summary>Fraction of TargetSelectionMaskAlpha used for the always-visible full-tile backdrop drawn under the growing charge fill -- keeps a multi-tile Delayed action's whole target shape legible from the first frame of the windup, not just once each tile's own fill has grown enough to be seen.</summary>
    private const float ChargeBackdropAlphaFraction = 0.3f;

    private readonly IMapViewQuery _mapView;
    private readonly MapViewState _mapViewState;
    private readonly MapCamera _camera;
    private readonly ActionTargetingController _actionTargeting;
    private readonly FontStashTextMeasurer _badgeFontMeasurer;

    public MapGameOverlay(IMapViewQuery mapView, MapViewState mapViewState, MapCamera camera, ActionTargetingController actionTargeting, FontService fontService)
    {
        _mapView = mapView;
        _mapViewState = mapViewState;
        _camera = camera;
        _actionTargeting = actionTargeting;
        _badgeFontMeasurer = fontService.GetTextMeasurer(fontService.GetFont(GameFontChrome.MapBadgeFontSize));
    }

    /// <summary>Draws the targeting highlights, then the selection's: the followed entity's in Detail inspection, the selected tile's otherwise.</summary>
    public void Draw(SpriteEffectBatch spriteBatch, Texture2D unitRectangle)
    {
        DrawTargetingHighlights(spriteBatch, unitRectangle);

        if (_mapViewState.InspectionMode == InspectionMode.Detail)
        {
            DrawFollowedEntityHighlight(spriteBatch, unitRectangle);
        }
        else
        {
            DrawSelectedTileHighlight(spriteBatch, unitRectangle);
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
    /// recomputed every Update) actually covers right now. A separate, independent draw call
    /// from DrawSelectedTileHighlight below -- the two are conceptually distinct (ability
    /// targeting vs. the inspector's click-to-select), even though both now share the same
    /// border-plus-mask technique (see DrawMaskedTileHighlight).
    ///
    /// A second pass then draws whatever's left of HoveredFootprint that TargetableTiles didn't
    /// already cover -- needed for Burst: TargetableTiles is capped strictly at the action's own
    /// Range (ActionTargetingController.ComputeTargetableTiles' reachable-area scatter), but
    /// TargetShapeResolver.ResolveBurst's actual hit footprint is AreaSize-radius around the
    /// anchor tile once that anchor passes the Range check -- i.e. the real splash can (and
    /// often does) reach past Range even though the anchor itself never could. Without this pass
    /// the highlight understated what the ability actually hits (confirmed in-game: entities
    /// outside the drawn splat still took the effect). Safe to always run: ResolveBurst (and
    /// every other shape) only ever populates HoveredFootprint from an in-range anchor to begin
    /// with, so this never draws a tile that wouldn't actually be hit; it also naturally no-ops
    /// for every shape whose own footprint can't exceed Range in the first place (SingleTarget/
    /// Line/Cone), since TargetableTiles already covers all of those.
    ///
    /// Once a Delayed ability is actually queued, Disarm already clears TargetableTiles (there's
    /// nothing left to aim), but the player benefits from still seeing exactly which tiles are
    /// about to be hit once the windup ends -- so this falls back to highlighting
    /// ActionTargetingController.PendingWindupTargetTiles (the already-resolved,
    /// locked-in footprint) in the same dark green used for a confirmed hover target, for as long
    /// as that pending action exists. Separately (and unconditionally, every frame, regardless of
    /// the player's own armed/pending state above), every OTHER entity's own in-flight Delayed
    /// windup is telegraphed too, in red or yellow depending on whether it's Dodgeable -- see
    /// CombatTargetPalette and ActionTargetingController.AllPendingWindupTargets. This is
    /// the "delayed actions keep the target shape drawn on the map... until they activate" half of
    /// Combat Overhaul: Dodge (TODO.md).
    /// </summary>
    private void DrawTargetingHighlights(SpriteEffectBatch spriteBatch, Texture2D unitRectangle)
    {
        if (_mapViewState.TargetableTiles is { Count: > 0 } targetableTiles)
        {
            foreach (var tile in targetableTiles)
            {
                var borderColor = _actionTargeting.HoveredFootprintContains(tile) ? CombatTargetPalette.PlayerTargetColor : CombatTargetPalette.PlayerArmColor;
                DrawMaskedTileHighlight(spriteBatch, unitRectangle, tile.X, tile.Y, borderColor);
            }

            foreach (var tile in _actionTargeting.HoveredFootprint)
            {
                if (!targetableTiles.Contains(tile))
                {
                    DrawMaskedTileHighlight(spriteBatch, unitRectangle, tile.X, tile.Y, CombatTargetPalette.PlayerTargetColor);
                }
            }
        }
        else if (_actionTargeting.PendingWindupTargetTiles is { } pendingTargetTiles)
        {
            var fillFraction = _mapView.GetChargeFraction(_mapView.PlayerEntityId);
            foreach (var tile in pendingTargetTiles)
            {
                DrawChargeFillHighlight(spriteBatch, unitRectangle, tile.X, tile.Y, CombatTargetPalette.PlayerTargetColor, fillFraction);
            }
        }

        // Every OTHER entity's own Delayed-action telegraph -- red (undodgeable) or yellow
        // (Dodgeable), per Combat Overhaul: Dodge. The player's own is already drawn above (dark
        // green, "player target"), so it's skipped here to avoid drawing it twice.
        // AllPendingWindupTargets itself already scopes this to Local processing tier (see
        // that method's own doc comment) -- no second tier check is needed here.
        foreach (var (entityId, targetTiles, isDodgeable) in _actionTargeting.AllPendingWindupTargets())
        {
            if (entityId == _mapView.PlayerEntityId)
            {
                continue;
            }

            var borderColor = isDodgeable ? CombatTargetPalette.EnemyDodgeableColor : CombatTargetPalette.EnemyUndodgeableColor;
            var fillFraction = _mapView.GetChargeFraction(entityId);
            foreach (var tile in targetTiles)
            {
                DrawChargeFillHighlight(spriteBatch, unitRectangle, tile.X, tile.Y, borderColor, fillFraction);
            }
        }

        DrawDodgeDirectionalHints(spriteBatch);
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

    private void DrawSelectedTileHighlight(SpriteEffectBatch spriteBatch, Texture2D unitRectangle)
    {
        if (_mapViewState.SelectedMapNodePosition is not { } selectedPosition)
        {
            return;
        }

        DrawSelectedTileGlow(spriteBatch, unitRectangle, selectedPosition.X, selectedPosition.Y);
    }

    /// <summary>
    /// Detail/Admin inspection's own highlight -- tracks the followed entity's live
    /// TransformComponent.Position every frame instead of a frozen tile position (see
    /// DrawSelectedTileHighlight, Basic mode's equivalent), so the highlight moves with the
    /// entity as it walks. Highlights every tile of the entity's own footprint (TransformComponent.
    /// Size), not just its origin tile -- a multi-tile (Huge) entity should read as fully
    /// selected, the same footprint MapRenderer draws the entity's sprite across.
    /// DrawMaskedTileHighlight already no-ops cleanly per-tile when a given tile falls outside
    /// the camera's currently visible viewport, so an entity that walks off-screen (in whole or
    /// in part) simply stops drawing a highlight for whichever tiles are off-screen, rather than
    /// throwing or drawing garbage -- no extra guard needed here for that case.
    /// </summary>
    private void DrawFollowedEntityHighlight(SpriteEffectBatch spriteBatch, Texture2D unitRectangle)
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
                DrawSelectedTileGlow(spriteBatch, unitRectangle, followed.Position.X + offsetX, followed.Position.Y + offsetY);
            }
        }
    }

    /// <summary>
    /// A uniform translucent borderColor wash (TargetSelectionMaskAlpha alpha) over the whole
    /// tile -- shared by the inspector's single-tile Gold selection and DrawTargetingHighlights'
    /// per-tile ability-targeting colors. Drawn after MapRenderer's world (not before,
    /// like terrain) so it lands on top of whatever's actually on the tile --
    /// terrain/occupant sprite, glyph, or glow -- rather than getting hidden underneath an opaque
    /// sprite the way this used to. The whole tile is translucent (not just an inset "mask" with
    /// a solid opaque border ring, the earlier technique) specifically so the sprite stays
    /// visible through the border too, not just the interior.
    /// </summary>
    private void DrawMaskedTileHighlight(SpriteEffectBatch spriteBatch, Texture2D unitRectangle, int mapNodeX, int mapNodeY, Color borderColor)
    {
        if (!_camera.TryGetTileRectangle(mapNodeX, mapNodeY, out var tileRectangle))
        {
            return;
        }

        spriteBatch.Draw(unitRectangle, tileRectangle, borderColor * TargetSelectionMaskAlpha);
    }

    /// <summary>
    /// A charging Delayed action's own per-tile telegraph -- an always-visible, dim full-tile
    /// backdrop (ChargeBackdropAlphaFraction of DrawMaskedTileHighlight's own alpha, so a
    /// multi-tile target shape still reads as one coherent zone from the first frame of the
    /// windup) plus a brighter bottom-up fill on top that grows with fillFraction, reaching
    /// DrawMaskedTileHighlight's own full alpha -- and its own full-tile coverage -- exactly as
    /// the action activates.
    /// </summary>
    private void DrawChargeFillHighlight(SpriteEffectBatch spriteBatch, Texture2D unitRectangle, int mapNodeX, int mapNodeY, Color fillColor, float fillFraction)
    {
        if (!_camera.TryGetTileRectangle(mapNodeX, mapNodeY, out var tileRectangle))
        {
            return;
        }

        spriteBatch.Draw(unitRectangle, tileRectangle, fillColor * TargetSelectionMaskAlpha * ChargeBackdropAlphaFraction);
        TileFillRenderer.DrawFill(spriteBatch, unitRectangle, tileRectangle, fillFraction, TileFillDirection.BottomToTop, fillColor * TargetSelectionMaskAlpha);
    }

    /// <summary>The inspector's own "this tile is selected" highlight -- a light-blue interior-fade glow (see GridSquareRenderer's own use of the same GlowMode.InteriorFade for inventory cells, so the two read as the same visual language) rather than DrawMaskedTileHighlight's flat wash, so terrain/sprites underneath stay fully legible through the ring gaps instead of being tinted.</summary>
    private void DrawSelectedTileGlow(SpriteEffectBatch spriteBatch, Texture2D unitRectangle, int mapNodeX, int mapNodeY)
    {
        if (!_camera.TryGetTileRectangle(mapNodeX, mapNodeY, out var tileRectangle))
        {
            return;
        }

        GlowRenderer.Draw(spriteBatch, unitRectangle, tileRectangle, GameWindowPalette.Selected, GlowMode.InteriorFade);
    }
}
