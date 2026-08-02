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

    public void Initialize(Window hostWindow)
    {
        _hostWindow = hostWindow;
        _font = fontService.GetFont((int)(Size.Y * GlyphSizeFraction));
    }

    public void Update(GameTime gameTime) { }

    public void DrawContent(GameTime gameTime, SpriteBatch spriteBatch, Texture2D unitRectangle)
    {
        if (!_actionLocks.TryGetReadonly(world.PlayerEntityId, out var actionLock) || !_glyphs.TryGetReadonly(world.PlayerEntityId, out var glyphComponent))
        {
            return;
        }

        _radialFill.Glyph = glyphComponent.Glyph;
        _radialFill.GlyphColor = glyphComponent.GlyphColor;
        _radialFill.BackgroundColor = Color.Blue;
        _radialFill.FillPercentage = actionLock.TotalLockFrames > 0
            ? (float)actionLock.LockFramesRemaining / actionLock.TotalLockFrames
            : 0f;

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
