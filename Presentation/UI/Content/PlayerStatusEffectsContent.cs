using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using FontStashSharp;
using Game.Modules.Burning;
using Game.Modules.Poison;
using Game.Modules.StatusEffects;
using Game.Modules.StatusEffects.Components;
using Game.World;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Presentation.Fonts;
using Presentation.Rendering;

namespace Presentation.UI.Content;

/// <summary>
/// Permanent top-right HUD readout underneath the player health bar: one icon per distinct
/// status effect type the player currently has any stacks of, drawn left to right -- each its
/// own square white tile with a 1px black border.
/// </summary>
public sealed class PlayerStatusEffectsContent(World world, ComponentManager componentManager, FontService fontService) : IWindowContent
{
    public static readonly Vector2 Size = new(PlayerHealthBarContent.Size.X, HudMetrics.EntrySize.Y / 2f * 1.5f);

    private const float IconSpacing = 1f;

    // Glyph drawn smaller than its background square -- a glyph sized to exactly fill the
    // square read as cramped/touching the border.
    private const float GlyphSizeFraction = 0.75f;

    private readonly MultiComponentPool<StatusEffectStack> _statusEffectStacks = componentManager.GetMultiPool<StatusEffectStack>();
    private readonly GlyphRenderer _glyphRenderer = new();
    private readonly List<StatusEffectType> _activeEffectTypes = [];

    private Window _hostWindow = null!;
    private SpriteFontBase _font = null!;

    public void Initialize(Window hostWindow)
    {
        _hostWindow = hostWindow;
        _font = fontService.GetFont((int)(Size.Y * GlyphSizeFraction));
    }

    public void Update(GameTime gameTime) { }

    public void DrawContent(GameTime gameTime, SpriteBatch spriteBatch, Texture2D unitRectangle)
    {
        StatusEffectQueries.GetActiveEffectTypes(_statusEffectStacks, world.PlayerEntityId, _activeEffectTypes);
        if (_activeEffectTypes.Count == 0)
        {
            return;
        }

        var origin = _hostWindow.ContentAbsolutePosition;
        var iconSize = new Vector2(_hostWindow.ContentSize.Y, _hostWindow.ContentSize.Y);

        for (var i = 0; i < _activeEffectTypes.Count; i++)
        {
            var iconOrigin = origin + new Vector2((iconSize.X + IconSpacing) * i, 0);
            DrawIcon(spriteBatch, unitRectangle, iconOrigin, iconSize, _activeEffectTypes[i]);
        }
    }

    private void DrawIcon(SpriteBatch spriteBatch, Texture2D unitRectangle, Vector2 origin, Vector2 size, StatusEffectType effectType)
    {
        var outerRectangle = new Rectangle((int)origin.X, (int)origin.Y, (int)size.X, (int)size.Y);
        spriteBatch.Draw(unitRectangle, outerRectangle, Color.Black);
        spriteBatch.Draw(unitRectangle, new Rectangle(outerRectangle.X + 1, outerRectangle.Y + 1, outerRectangle.Width - 2, outerRectangle.Height - 2), Color.White);

        _glyphRenderer.DrawCentered(spriteBatch, _font, GetGlyph(effectType), origin, size, GetColor(effectType));
    }

    /// <summary>Presentation's own type -> glyph mapping (rendering knowledge belongs here, not in the shared StatusEffects core module, which stays ignorant of individual effects -- see StatusEffectsModule's own doc comment). "?" is an intentionally-visible fallback for any future effect type added here without a mapping yet, rather than throwing mid-draw.</summary>
    private static string GetGlyph(StatusEffectType effectType) => effectType switch
    {
        StatusEffectType.Burning => BurningEffects.Glyph,
        StatusEffectType.Poison => PoisonEffects.Glyph,
        _ => "?",
    };

    /// <summary>Same reasoning/fallback as GetGlyph above -- kept as its own switch rather than folded into GetGlyph so each is a single, simple type -> value mapping.</summary>
    private static Color GetColor(StatusEffectType effectType) => effectType switch
    {
        StatusEffectType.Burning => Color.Red,
        StatusEffectType.Poison => Color.DarkGreen,
        _ => Color.Black,
    };
}
