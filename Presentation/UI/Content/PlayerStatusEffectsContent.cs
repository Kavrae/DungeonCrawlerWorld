using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using FontStashSharp;
using Game.Modules.Actions.Activators;
using Game.Modules.Inventory;
using Game.Modules.Inventory.Definitions;
using Game.Modules.StatusEffects;
using Game.World;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Presentation.Fonts;
using Presentation.Rendering;
using Presentation.UI.Chrome;

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
public sealed class PlayerStatusEffectsContent(World world, ComponentManager componentManager, ItemCatalog itemCatalog, FontService fontService, StatusEffectDisplayRegistry statusEffectDisplays) : IElementContent
{
    public static readonly Vector2 Size = new(PlayerHealthBarContent.Size.X, HudChrome.EntrySize.Y / 2f * 1.5f);

    private const float IconSpacing = 1f;

    private const float CountdownTextGap = 1f;

    private readonly PackedComponentPool<PotionCooldownComponent> _potionCooldowns = componentManager.GetPackedPool<PotionCooldownComponent>();
    private readonly LabelRenderer _labelRenderer = new();
    private readonly List<StatusEffectType> _activeEffectTypes = [];

    /// <summary>Poison/Burning's own current stack counts, keyed by type -- only ever holds entries for types that actually want a count shown (see DrawIcon's own Poison/Burning check, mirrored in RefreshStatusEffectState below).</summary>
    private readonly Dictionary<StatusEffectType, int> _stackCountsByType = [];

    private bool _hasPotionCooldown;
    private ushort _potionCooldownFramesRemaining;

    private Window _hostWindow = null!;
    private SpriteFontBase _font = null!;
    private SpriteFontBase _countdownFont = null!;

    public void Initialize(Window hostWindow)
    {
        _hostWindow = hostWindow;

        // Glyph drawn smaller than its background square -- a glyph sized to exactly fill the
        // square read as cramped/touching the border.
        _font = fontService.GetFont((int)(Size.Y * FontChrome.PlayerStatusGlyphFontFraction));
        _countdownFont = fontService.GetFont((int)(Size.Y * FontChrome.PlayerStatusCountdownFontFraction));
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

        StatusEffectQueries.GetActiveEffectTypes(statusEffectDisplays, componentManager, playerEntityId, _activeEffectTypes);

        _stackCountsByType.Clear();
        foreach (var effectType in _activeEffectTypes)
        {
            if (effectType is StatusEffectType.Poison or StatusEffectType.Burning)
            {
                _stackCountsByType[effectType] = StatusEffectQueries.CountStacks(statusEffectDisplays, componentManager, playerEntityId, effectType);
            }
        }

        _hasPotionCooldown = _potionCooldowns.TryGetReadonly(playerEntityId, out var potionCooldown) && potionCooldown.FramesRemaining > 0;
        _potionCooldownFramesRemaining = _hasPotionCooldown ? potionCooldown.FramesRemaining : (ushort)0;
    }

    public void DrawContent(GameTime gameTime)
    {
        if (_activeEffectTypes.Count == 0 && !_hasPotionCooldown)
        {
            return;
        }

        var spriteBatch = _hostWindow.ElementPoolService.SpriteBatch;
        var unitRectangle = _hostWindow.ElementPoolService.UnitRectangle;
        var origin = _hostWindow.ContentAbsolutePosition;
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

    private void DrawIcon(SpriteBatch spriteBatch, Texture2D unitRectangle, Vector2 origin, Vector2 size, StatusEffectType effectType)
    {
        DrawIconBackground(spriteBatch, unitRectangle, origin, size);

        _labelRenderer.DrawCentered(spriteBatch, _font, GetGlyph(effectType), origin, size, GetColor(effectType));

        if (_stackCountsByType.TryGetValue(effectType, out var stackCount))
        {
            DrawStackCount(spriteBatch, origin, size, stackCount);
        }
    }

    private void DrawPotionCooldownIcon(SpriteBatch spriteBatch, Texture2D unitRectangle, Vector2 origin, Vector2 size, ushort framesRemaining)
    {
        DrawIconBackground(spriteBatch, unitRectangle, origin, size);

        if (itemCatalog.TryGet(HealthPotion.Id, out var healthPotion))
        {
            _labelRenderer.DrawCentered(spriteBatch, _font, healthPotion.Glyph, origin, size, healthPotion.GlyphColor);
        }

        DrawTextBelowIcon(spriteBatch, origin, size, PotionCooldownEffects.RemainingSeconds(framesRemaining).ToString());
    }

    /// <summary>No-ops at count &lt;= 1 -- a lone stack doesn't need a number, the same convention ItemIconRenderer.DrawQuantityBadge already uses for item stacks.</summary>
    private void DrawStackCount(SpriteBatch spriteBatch, Vector2 origin, Vector2 size, int stackCount)
    {
        if (stackCount <= 1)
        {
            return;
        }

        DrawTextBelowIcon(spriteBatch, origin, size, stackCount.ToString());
    }

    private void DrawTextBelowIcon(SpriteBatch spriteBatch, Vector2 origin, Vector2 size, string text)
    {
        var textSize = _countdownFont.MeasureString(text);
        var textPosition = new Vector2(origin.X + (size.X - textSize.X) / 2f, origin.Y + size.Y + CountdownTextGap);

        ContrastTextRenderer.Draw(spriteBatch, _countdownFont, text, textPosition);
    }

    /// <summary>The square white tile with a 1px black border shared by every status/cooldown icon here -- DrawIcon and DrawPotionCooldownIcon otherwise only differ in what's drawn on top of it.</summary>
    private static void DrawIconBackground(SpriteBatch spriteBatch, Texture2D unitRectangle, Vector2 origin, Vector2 size)
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
