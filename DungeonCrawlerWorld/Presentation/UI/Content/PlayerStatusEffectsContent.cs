using Engine.ECS.Systems;
using FontStashSharp;
using Game.Modules.Actions.Activators;
using Game.Modules.Inventory;
using Game.Modules.StatusEffects;
using Game.Views;
using Game.World;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

using Presentation.UI.Chrome;
using UIEngine;
using UIEngine.Fonts;
using UIEngine.Input;
using UIEngine.Rendering;
using UIEngine.UI;
using UIEngine.UI.Chrome;

namespace Presentation.UI.Content;

/// <summary>
/// Permanent top-right HUD readout underneath the player health bar: one icon per distinct
/// status effect type the player currently has any stacks of, drawn left to right -- each its
/// own square white tile with a 1px black border. Poison/Burning additionally show their current
/// stack count (StatusEffectQueries.CountStacks) below the icon once it's above 1 -- Paralysis is
/// excluded since its own StackCount is always exactly 1 (see ParalysisTimerComponent), so a
/// count there would just be noise. PotionCooldownComponent isn't a stacking status effect at all
/// (see PotionCooldownEffects' own doc comment), so it gets its own trailing icon, keyed
/// to the Health Potion's glyph/color (the cooldown is shared across every PotionActivator --
/// revisit this fixed glyph/color choice if that ever reads as misleading for a non-Health
/// potion) with the remaining seconds in green below the icon
/// (not overlaid on top of it -- that read as visual clutter against the glyph) instead of a
/// stack count. Hovering a status effect's icon for PopupChrome.HoverTooltipDelayFrames shows the
/// popup the Health window shows for that effect's row (StatusEffectSourcePopup).
/// </summary>
/// <param name="simulationClock">"CurrentFrame" for the potion cooldown's remaining seconds -- it stores an absolute deadline.</param>
public sealed class PlayerStatusEffectsContent(World world, ActionStateView actionStateView, ItemCatalog itemCatalog, FontService fontService, StatusEffectDisplayRegistry statusEffectDisplays, SimulationClock simulationClock, ActionSourceNaming actionSourceNaming, TooltipController tooltipController, PointerState pointerState) : IElementContent
{

    public static readonly Vector2 Size = new(PlayerHealthBarContent.Size.X, HudChrome.EntrySize.Y / 2f * 1.5f);

    private const float IconSpacing = 1f;

    private const float CountdownTextGap = 1f;

    private readonly List<StatusEffectType> _activeEffectTypes = [];

    /// <summary>Poison/Burning's own current stack counts, keyed by type -- only ever holds entries for types that actually want a count shown (see DrawIcon's own Poison/Burning check, mirrored in RefreshStatusEffectState below).</summary>
    private readonly Dictionary<StatusEffectType, int> _stackCountsByType = [];

    private bool _hasPotionCooldown;
    private int _potionCooldownFramesRemaining;

    /// <summary>A number drawn below an icon, built and measured only when it changes.</summary>
    private sealed class BelowIconNumber
    {
        private readonly KeyedCache<int, string> _text = new();
        private readonly TextSizeCache _size = new();

        public (string Text, Vector2 Size) Get(SpriteFontBase font, int number)
        {
            var text = _text.Get(number, static value => value.ToString());
            return (text, _size.SizeOf(font, text));
        }
    }

    private readonly Dictionary<StatusEffectType, BelowIconNumber> _stackCountNumbers = [];
    private readonly BelowIconNumber _potionCooldownNumber = new();

    private StatusEffectType? _hoveredEffectType;
    private int _hoveredFrames;

    /// <summary>The hovered icon's popup -- see StatusEffectSourcePopup.Key.</summary>
    private readonly KeyedCache<StatusEffectSourcePopup.Key, (string Body, IReadOnlyList<TooltipRow> Rows)> _sourcePopup = new();

    private Window _hostWindow = null!;
    private FontStashTextMeasurer _glyphFontMeasurer = null!;
    private SpriteFontBase _countdownFont = null!;

    public void Initialize(Window hostWindow)
    {
        _hostWindow = hostWindow;

        // Glyph drawn smaller than its background square -- a glyph sized to exactly fill the
        // square read as cramped/touching the border.
        _glyphFontMeasurer = fontService.GetTextMeasurer(fontService.GetFont((int)(Size.Y * GameFontChrome.PlayerStatusGlyphFontFraction)));
        _countdownFont = fontService.GetFont((int)(Size.Y * GameFontChrome.PlayerStatusCountdownFontFraction));
    }

    /// <summary>Which status effect types are active, their stack counts, and the potion cooldown are all decided here -- Draw only reads the cached results and lays out icons/text.</summary>
    public void Update(GameTime gameTime)
    {
        var playerEntityId = world.PlayerEntityId;
        if (playerEntityId < 0)
        {
            _activeEffectTypes.Clear();
            _stackCountsByType.Clear();
            _hasPotionCooldown = false;
            UpdateHover(playerEntityId);
            return;
        }

        StatusEffectQueries.GetActiveEffectTypes(statusEffectDisplays, playerEntityId, _activeEffectTypes);

        _stackCountsByType.Clear();
        foreach (var effectType in _activeEffectTypes)
        {
            if (statusEffectDisplays.TryGet(effectType, out var display) && display.MaxStacks > 1)
            {
                _stackCountsByType[effectType] = StatusEffectQueries.CountStacks(statusEffectDisplays, playerEntityId, effectType);
            }
        }

        _potionCooldownFramesRemaining = actionStateView.TryGetPotionCooldown(playerEntityId, out var potionCooldown)
            ? PotionCooldownEffects.FramesRemaining(potionCooldown, simulationClock.CurrentFrame)
            : 0;
        _hasPotionCooldown = _potionCooldownFramesRemaining > 0;

        UpdateHover(playerEntityId);
    }

    /// <summary>Shows the hovered status effect icon's popup once it has been hovered for PopupChrome.HoverTooltipDelayFrames, and hides it as soon as the cursor leaves or the effect is gone.</summary>
    private void UpdateHover(int playerEntityId)
    {
        var hoveredIconIndex = HoveredIconIndex() is { } index && index < _activeEffectTypes.Count ? index : -1;
        StatusEffectType? hoveredEffectType = hoveredIconIndex >= 0 ? _activeEffectTypes[hoveredIconIndex] : null;

        if (hoveredEffectType == _hoveredEffectType)
        {
            _hoveredFrames++;
        }
        else
        {
            _hoveredEffectType = hoveredEffectType;
            _hoveredFrames = hoveredEffectType is null ? 0 : 1;
        }

        if (hoveredEffectType is not { } effectType
            || _hoveredFrames < PopupChrome.HoverTooltipDelayFrames
            || !StatusEffectSourcePopup.TryDescribe(statusEffectDisplays, actionSourceNaming, playerEntityId, effectType, simulationClock.CurrentFrame, out var popupKey))
        {
            tooltipController.Hide(this);
            return;
        }

        var (body, rows) = _sourcePopup.Get(popupKey, this, static (key, content) => content.BuildSourcePopup(key));
        tooltipController.Show(this, IconScreenRectangle(hoveredIconIndex), PopupAnchor.South, PopupChrome.AbilityScorePopupGap, ModifierDisplayFormatting.SourcePopupMaximumSize, body, rows: rows, useFixedWidth: true);
    }

    /// <summary>The index of the icon under the cursor, counting from the left, or null when the cursor is over none (between icons, below them, or off this window).</summary>
    private int? HoveredIconIndex()
    {
        if (!_hostWindow.IsPointerOver)
        {
            return null;
        }

        var iconSize = _hostWindow.ContentSize.Y;
        var cursor = _hostWindow.ToContentLocal(new Vector2(pointerState.CursorPosition.X, pointerState.CursorPosition.Y));
        if (cursor.X < 0 || cursor.Y < 0 || cursor.Y >= iconSize)
        {
            return null;
        }

        var stride = iconSize + IconSpacing;
        var index = (int)(cursor.X / stride);
        return cursor.X - index * stride < iconSize ? index : null;
    }

    private (string Body, IReadOnlyList<TooltipRow> Rows) BuildSourcePopup(StatusEffectSourcePopup.Key key) =>
        StatusEffectSourcePopup.Build(key, statusEffectDisplays, actionSourceNaming);

    private Rectangle IconScreenRectangle(int index)
    {
        var iconSize = _hostWindow.ContentSize.Y;
        var origin = _hostWindow.ContentDrawOrigin + new Vector2((iconSize + IconSpacing) * index, 0);
        return new Rectangle((int)origin.X, (int)origin.Y, (int)iconSize, (int)iconSize);
    }

    public void DrawContent(GameTime gameTime)
    {
        if (_activeEffectTypes.Count == 0 && !_hasPotionCooldown)
        {
            return;
        }

        var spriteBatch = _hostWindow.Render.Batch;
        var origin = Vector2.Zero;
        var iconSize = new Vector2(_hostWindow.ContentSize.Y, _hostWindow.ContentSize.Y);

        for (var i = 0; i < _activeEffectTypes.Count; i++)
        {
            var iconOrigin = origin + new Vector2((iconSize.X + IconSpacing) * i, 0);
            DrawIcon(spriteBatch, iconOrigin, iconSize, _activeEffectTypes[i]);
        }

        if (_hasPotionCooldown)
        {
            var iconOrigin = origin + new Vector2((iconSize.X + IconSpacing) * _activeEffectTypes.Count, 0);
            DrawPotionCooldownIcon(spriteBatch, iconOrigin, iconSize, _potionCooldownFramesRemaining);
        }
    }

    private void DrawIcon(SpriteEffectBatch spriteBatch, Vector2 origin, Vector2 size, StatusEffectType effectType)
    {
        DrawIconBackground(spriteBatch, origin, size);

        LabelRenderer.DrawCentered(spriteBatch, _glyphFontMeasurer, GetGlyph(effectType), origin, size, GetColor(effectType));

        if (_stackCountsByType.TryGetValue(effectType, out var stackCount))
        {
            DrawStackCount(spriteBatch, origin, size, effectType, stackCount);
        }
    }

    private void DrawPotionCooldownIcon(SpriteEffectBatch spriteBatch, Vector2 origin, Vector2 size, int framesRemaining)
    {
        DrawIconBackground(spriteBatch, origin, size);

        if (itemCatalog.TryGet(BuiltInItemIds.HealthPotion, out var healthPotion))
        {
            LabelRenderer.DrawCentered(spriteBatch, _glyphFontMeasurer, healthPotion.Glyph, origin, size, healthPotion.GlyphColor);
        }

        DrawTextBelowIcon(spriteBatch, origin, size, _potionCooldownNumber.Get(_countdownFont, PotionCooldownEffects.RemainingSeconds(framesRemaining)));
    }

    /// <summary>No-ops at count &lt;= 1 -- a lone stack doesn't need a number, the same convention ItemIconRenderer.DrawQuantityBadge already uses for item stacks.</summary>
    private void DrawStackCount(SpriteEffectBatch spriteBatch, Vector2 origin, Vector2 size, StatusEffectType effectType, int stackCount)
    {
        if (stackCount <= 1)
        {
            return;
        }

        if (!_stackCountNumbers.TryGetValue(effectType, out var stackCountNumber))
        {
            stackCountNumber = new BelowIconNumber();
            _stackCountNumbers[effectType] = stackCountNumber;
        }

        DrawTextBelowIcon(spriteBatch, origin, size, stackCountNumber.Get(_countdownFont, stackCount));
    }

    private void DrawTextBelowIcon(SpriteEffectBatch spriteBatch, Vector2 origin, Vector2 size, (string Text, Vector2 Size) number)
    {
        var textPosition = new Vector2(origin.X + (size.X - number.Size.X) / 2f, origin.Y + size.Y + CountdownTextGap);
        ContrastTextRenderer.Draw(spriteBatch, _countdownFont, number.Text, textPosition);
    }

    /// <summary>The square white tile with a 1px black border shared by every status/cooldown icon here -- DrawIcon and DrawPotionCooldownIcon otherwise only differ in what's drawn on top of it.</summary>
    private static void DrawIconBackground(SpriteEffectBatch spriteBatch, Vector2 origin, Vector2 size)
    {
        var outerRectangle = new Rectangle((int)origin.X, (int)origin.Y, (int)size.X, (int)size.Y);
        spriteBatch.FillRectangle(outerRectangle, Color.Black);
        spriteBatch.FillRectangle(new Rectangle(outerRectangle.X + 1, outerRectangle.Y + 1, outerRectangle.Width - 2, outerRectangle.Height - 2), Color.White);
    }

    /// <summary>Looks up each effect module's own registered glyph (see IStatusEffectDisplay). "?" is an intentionally-visible fallback for any active effect type with no registered display, rather than throwing mid-draw.</summary>
    private string GetGlyph(StatusEffectType effectType) => statusEffectDisplays.TryGet(effectType, out var display) ? display.Glyph : "?";

    /// <summary>Same reasoning/fallback as GetGlyph above -- kept as its own switch rather than folded into GetGlyph so each is a single, simple type -> value mapping.</summary>
    private static Color GetColor(StatusEffectType effectType) => effectType switch
    {
        StatusEffectType.Burning => Color.Red,
        StatusEffectType.Poison => Color.DarkGreen,
        StatusEffectType.Paralysis => Color.Yellow,
        _ => Color.Black,
    };
}
