using FontStashSharp;
using Game.Blueprints;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Presentation.Fonts;
using Presentation.Rendering;

namespace Presentation.UI;

/// <summary>
/// A clickable header button that shows/hides a stack of child windows tiled vertically
/// beneath it -- e.g. NotificationCenter's single folder replacing its old always-visible
/// category bar. Reuses Window's own title/content split and Minimized/WrapContent sizing
/// instead of inventing new state: the icon lives in the title bar (drawn regardless of
/// display mode -- see Draw), and the child stack lives in the content area, which Window's
/// own Measure already zeroes out while Minimized -- collapsing already hides *and*
/// zero-sizes the children with no extra bookkeeping.
/// </summary>
public sealed class Folder : Window
{
    private static readonly Vector2 DefaultIconSize = new(32, 32);

    private readonly SpriteSheetService _spriteSheetService;
    private readonly SpriteRenderer _spriteRenderer;

    private Vector2 _iconSize;
    private string? _spriteName;
    private string _fallbackGlyph = string.Empty;
    private SpriteFontBase _fallbackGlyphFont = null!;

    public Folder(FontService fontService, WindowService windowService, GlyphRenderer glyphRenderer, SpriteSheetService spriteSheetService, SpriteRenderer spriteRenderer)
        : base(fontService, windowService, glyphRenderer)
    {
        ArgumentNullException.ThrowIfNull(spriteSheetService);
        ArgumentNullException.ThrowIfNull(spriteRenderer);

        _spriteSheetService = spriteSheetService;
        _spriteRenderer = spriteRenderer;
    }

    public override void BuildWindow(Window? parentWindow, WindowOptions windowOptions)
    {
        base.BuildWindow(parentWindow, windowOptions);

        var folderOptions = windowOptions.Folder;
        _iconSize = folderOptions?.IconSize ?? DefaultIconSize;
        _spriteName = folderOptions?.SpriteName;
        _fallbackGlyph = folderOptions?.FallbackGlyph ?? string.Empty;
        _fallbackGlyphFont = FontService.GetFont((int)(_iconSize.Y * 0.6f));

        _canContainChildWindows = true;
        _childWindowTileMode = WindowTileMode.Vertical;

        // The title bar is this control's icon-button header, never text -- always shown
        // (including while Minimized) so the icon stays visible whether the folder is
        // expanded or collapsed. BorderStyle.Outset (set via Chrome options by callers, same
        // as Button's own default) gives it the "standard button border" look.
        _title.ShowTitle = true;
        _title.ShowWhenMinimized = true;
        _title.Text = string.Empty;
        _title.OriginalSize = _iconSize;
        _title.Size = _iconSize;
    }

    public override void Initialize()
    {
        base.Initialize();

        // Starts collapsed -- a HUD-persistent control defaults to its smallest footprint,
        // expanding only once the user actually asks to see its contents.
        SetWindowDisplayMode(WindowDisplayMode.Minimized);
    }

    /// <summary>
    /// Base's version measures _title.Text -- always empty here, since this title only ever
    /// shows an icon, never text. Width matches the expanded (WrapContent) content width
    /// rather than shrinking to just the icon -- only the height collapses. Computed from the
    /// children's own already-measured CurrentSize, which is still valid here: Measure() calls
    /// this before MeasureChildren, so children's sizes still reflect the last time they were
    /// actually measured (the last WrapContent pass), not yet zeroed for this one.
    /// </summary>
    protected override void RecalculateMinimizedWindowSize()
    {
        var width = _childWindows.Count > 0 ? ContentWidthFromChildren() : _iconSize.X;

        _title.Size = new Vector2(width, _iconSize.Y);
        _contentState.Size = Vector2.Zero;
        _geometry.CurrentSize = new Vector2(width, _iconSize.Y) + BorderInsetDoubled;
    }

    /// <summary>Mirrors RecalculateWrapContentWindowSize's own maxRight+ContentPadding.X computation, so the collapsed and expanded widths match exactly.</summary>
    private float ContentWidthFromChildren()
    {
        var maxRight = 0f;
        foreach (var child in _childWindows)
        {
            maxRight = System.Math.Max(maxRight, child.WindowRelativePosition.X + child.WindowCurrentSize.X);
        }

        return maxRight + ContentPadding.X;
    }

    /// <summary>
    /// The icon lives in the title rect, not the content rect, so toggling here (not
    /// OnContentClickAction/Clicked, which only fire for content-rect clicks) is what makes
    /// clicking the folder itself -- in either display mode -- expand/collapse it.
    /// </summary>
    protected override void OnTitleClickAction(Point mousePosition)
    {
        base.OnTitleClickAction(mousePosition);

        SetWindowDisplayMode(WindowDisplay == WindowDisplayMode.Minimized
            ? WindowDisplayMode.WrapContent
            : WindowDisplayMode.Minimized);
    }

    public override void Draw(GameTime gameTime, GraphicsDevice graphicsDevice, SpriteBatch spriteBatch, Texture2D unitRectangle)
    {
        base.Draw(gameTime, graphicsDevice, spriteBatch, unitRectangle);

        DrawIcon(spriteBatch);
    }

    /// <summary>Sprite-else-glyph, mirroring MapWindow.TryDrawEntityVisual's fallback for entities with no manifest entry.</summary>
    private void DrawIcon(SpriteBatch spriteBatch)
    {
        // The title bar grows wider than the icon once expanded (see
        // RecalculateWrapContentWindowSize, which widens _title.Size to match content) -- the
        // icon itself stays fixed at _iconSize regardless, centered within that wider title
        // rect rather than stretched to fill it.
        var iconTopLeft = TitleAbsolutePosition + (TitleSize - _iconSize) / 2f;

        if (_spriteName is not null && SpriteManifest.TryGet(_spriteName, out var spriteComponent))
        {
            var texture = _spriteSheetService.GetTexture(spriteComponent.SheetPath);
            _spriteRenderer.Draw(spriteBatch, texture, spriteComponent.SourceRectangle, iconTopLeft, _iconSize, Color.White);
            return;
        }

        if (!string.IsNullOrEmpty(_fallbackGlyph))
        {
            GlyphRenderer.DrawCentered(spriteBatch, _fallbackGlyphFont, _fallbackGlyph, iconTopLeft, _iconSize, Color.Black);
        }
    }
}
