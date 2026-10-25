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
using UIEngine.Rendering;
using UIEngine.UI;

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
/// stack count.
/// </summary>
/// <param name="simulationClock">"CurrentFrame" for the potion cooldown's remaining seconds -- it stores an absolute deadline.</param>
public sealed class PlayerStatusEffectsContent(World world, ActionStateView actionStateView, ItemCatalog itemCatalog, FontService fontService, StatusEffectDisplayRegistry statusEffectDisplays, SimulationClock simulationClock) : IElementContent
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
            return;
        }

        StatusEffectQueries.GetActiveEffectTypes(statusEffectDisplays, playerEntityId, _activeEffectTypes);

        _stackCountsByType.Clear();
        foreach (var effectType in _activeEffectTypes)
        {
            if (effectType is StatusEffectType.Poison or StatusEffectType.Burning)
            {
                _stackCountsByType[effectType] = StatusEffectQueries.CountStacks(statusEffectDisplays, playerEntityId, effectType);
            }
        }

        _potionCooldownFramesRemaining = actionStateView.TryGetPotionCooldown(playerEntityId, out var potionCooldown)
            ? PotionCooldownEffects.FramesRemaining(potionCooldown, simulationClock.CurrentFrame)
            : 0;
        _hasPotionCooldown = _potionCooldownFramesRemaining > 0;
    }

    public void DrawContent(GameTime gameTime)
    {
        if (_activeEffectTypes.Count == 0 && !_hasPotionCooldown)
        {
            return;
        }

        var spriteBatch = _hostWindow.Render.Batch;
        var unitRectangle = _hostWindow.Render.UnitRectangle;
        var origin = Vector2.Zero;
        var iconSize = new Vector2(_hostWindow.ContentSize.Y, _hostWindow.ContentSize.Y);

        for (var i = 0; i < _activeEffectTypes.Count; i++)
        {
            var iconOrigin = origin + new Vector2((iconSize.X + IconSpacing) * i, 0);
            DrawIcon(spriteBatch, unitRectangle, iconOrigin, iconSize, _activeEffectTypes[i]);
        }

        if (_hasPotionCooldown)
        {
            var iconOrigin = origin + new Vector2((iconSize.X + IconSpacing) * _activeEffectTypes.Count, 0);
            DrawPotionCooldownIcon(spriteBatch, unitRectangle, iconOrigin, iconSize, _potionCooldownFramesRemaining);
        }
    }

    private void DrawIcon(SpriteEffectBatch spriteBatch, Texture2D unitRectangle, Vector2 origin, Vector2 size, StatusEffectType effectType)
    {
        DrawIconBackground(spriteBatch, unitRectangle, origin, size);

        LabelRenderer.DrawCentered(spriteBatch, _glyphFontMeasurer, GetGlyph(effectType), origin, size, GetColor(effectType));

        if (_stackCountsByType.TryGetValue(effectType, out var stackCount))
        {
            DrawStackCount(spriteBatch, origin, size, effectType, stackCount);
        }
    }

    private void DrawPotionCooldownIcon(SpriteEffectBatch spriteBatch, Texture2D unitRectangle, Vector2 origin, Vector2 size, int framesRemaining)
    {
        DrawIconBackground(spriteBatch, unitRectangle, origin, size);

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
    private static void DrawIconBackground(SpriteEffectBatch spriteBatch, Texture2D unitRectangle, Vector2 origin, Vector2 size)
    {
        var outerRectangle = new Rectangle((int)origin.X, (int)origin.Y, (int)size.X, (int)size.Y);
        spriteBatch.Draw(unitRectangle, outerRectangle, Color.Black);
        spriteBatch.Draw(unitRectangle, new Rectangle(outerRectangle.X + 1, outerRectangle.Y + 1, outerRectangle.Width - 2, outerRectangle.Height - 2), Color.White);
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
