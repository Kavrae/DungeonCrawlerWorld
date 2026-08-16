using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using FontStashSharp;
using Game.Modules.Core.Components;
using Game.World;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Presentation.Fonts;
using Presentation.Rendering;

namespace Presentation.UI.Content;

public sealed class ActionLockContent(World world, ComponentManager componentManager, FontService fontService) : IElementContent
{
    public static readonly Vector2 Size = new(HudMetrics.EntrySize.Y * 1.5f, HudMetrics.EntrySize.Y * 1.5f);

    private const float GlyphSizeFraction = 0.75f;
    private const int ContentInset = 2;

    private readonly PackedComponentPool<ActionLockComponent> _actionLocks = componentManager.GetPackedPool<ActionLockComponent>();
    private readonly DirectComponentPool<GlyphComponent> _glyphs = componentManager.GetDirectPool<GlyphComponent>();
    private readonly RadialFillRenderer _radialFill = new(new GlyphRenderer());

    private Window _hostWindow = null!;
    private SpriteFontBase _font = null!;

    private bool _hasActionLock;
    private string _glyph = string.Empty;
    private Color _glyphColor;
    private float _fillPercentage;

    public void Initialize(Window hostWindow)
    {
        _hostWindow = hostWindow;
        _font = fontService.GetFont((int)(Size.Y * GlyphSizeFraction));
    }

    /// <summary>Whether the player currently has an action lock to show, and its fill fraction, is decided here -- Draw only reads the cached result and turns it into pixels.</summary>
    public void Update(GameTime gameTime)
    {
        var playerEntityId = world.PlayerEntityId;
        if (playerEntityId < 0 || !_actionLocks.TryGetReadonly(playerEntityId, out var actionLock) || !_glyphs.TryGetReadonly(playerEntityId, out var glyphComponent))
        {
            _hasActionLock = false;
            return;
        }

        _hasActionLock = true;
        _glyph = glyphComponent.Glyph;
        _glyphColor = glyphComponent.GlyphColor;
        _fillPercentage = actionLock.CurrentLockTotalFrames > 0
            ? (float)actionLock.CurrentLockFramesRemaining / actionLock.CurrentLockTotalFrames
            : 0f;
    }

    public void DrawContent(GameTime gameTime, SpriteBatch spriteBatch, Texture2D unitRectangle)
    {
        if (!_hasActionLock)
        {
            return;
        }

        _radialFill.Glyph = _glyph;
        _radialFill.GlyphColor = _glyphColor;
        _radialFill.BackgroundColor = Color.Blue;
        _radialFill.FillPercentage = _fillPercentage;

        var origin = _hostWindow.ContentAbsolutePosition;
        var contentSize = _hostWindow.ContentSize;
        var bounds = new Rectangle(
            (int)origin.X + ContentInset,
            (int)origin.Y + ContentInset,
            (int)contentSize.X - ContentInset * 2,
            (int)contentSize.Y - ContentInset * 2);

        _radialFill.Draw(spriteBatch, unitRectangle, _font, bounds);
    }
}
