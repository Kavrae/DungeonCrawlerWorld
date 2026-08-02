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
/// category bar. An Element in its own right (not a Window subclass) -- reuses Element's
/// generic header/Minimized/WrapContent machinery directly: the icon lives in the header
/// (drawn regardless of display mode -- see DrawHeader), and the child stack lives in the
/// content area, which Element's own Measure already zeroes out while Minimized -- collapsing
/// already hides *and* zero-sizes the children with no extra bookkeeping.
/// </summary>
public sealed class Folder : Element
{
    private static readonly Vector2 DefaultIconSize = new(32, 32);

    private readonly SpriteSheetService _spriteSheetService;
    private readonly SpriteRenderer _spriteRenderer;

    private Vector2 _iconSize;
    private string? _spriteName;
    private string _fallbackGlyph = string.Empty;
    private SpriteFontBase _fallbackGlyphFont = null!;
    private Color _backgroundColor;

    public Folder(FontService fontService, ElementPoolService elementPoolService, GlyphRenderer glyphRenderer, SpriteSheetService spriteSheetService, SpriteRenderer spriteRenderer)
        : base(fontService, elementPoolService, glyphRenderer)
    {
        ArgumentNullException.ThrowIfNull(spriteSheetService);
        ArgumentNullException.ThrowIfNull(spriteRenderer);

        _spriteSheetService = spriteSheetService;
        _spriteRenderer = spriteRenderer;
    }

    public override void Build(Element? parent, ElementOptions options)
    {
        base.Build(parent, options);

        var folderOptions = options.Folder;
        _iconSize = folderOptions?.IconSize ?? DefaultIconSize;
        _spriteName = folderOptions?.SpriteName;
        _fallbackGlyph = folderOptions?.FallbackGlyph ?? string.Empty;
        _fallbackGlyphFont = FontService.GetFont((int)(_iconSize.Y * 0.6f));
        _backgroundColor = folderOptions?.BackgroundColor ?? Color.LightBlue;

        _canContainChildren = true;
        _childrenTileMode = ChildElementTileMode.Vertical;

        // The header is this control's icon button, always shown (including while Minimized)
        // so the icon stays visible whether the folder is expanded or collapsed.
        // BorderStyle.Outset (set via Chrome options by callers, same as Button's own default)
        // gives it the "standard button border" look.
        _headerState.ShowHeader = true;
        _headerState.ShowHeaderWhenMinimized = true;
        _headerState.OriginalSize = _iconSize;
        _headerState.Size = _iconSize;
    }

    public override void Initialize()
    {
        base.Initialize();

        // Starts collapsed -- a HUD-persistent control defaults to its smallest footprint,
        // expanding only once the user actually asks to see its contents.
        SetDisplayMode(ElementDisplayMode.Minimized);
    }

    /// <summary>
    /// Width matches the expanded (WrapContent) content width rather than shrinking to just
    /// the icon -- only the height collapses. Computed from the children's own already-measured
    /// CurrentSize, which is still valid here: Measure() calls this before MeasureChildren, so
    /// children's sizes still reflect the last time they were actually measured (the last
    /// WrapContent pass), not yet zeroed for this one.
    /// </summary>
    protected override void RecalculateMinimizedSize()
    {
        var width = _children.Count > 0 ? ContentWidthFromChildren() : _iconSize.X;

        _headerState.Size = new Vector2(width, _iconSize.Y);
        _contentState.Size = Vector2.Zero;
        _geometry.CurrentSize = new Vector2(width, _iconSize.Y) + BorderInsetDoubled;
    }

    /// <summary>Mirrors RecalculateWrapContentWindowSize's own maxRight+ContentPadding.X computation, so the collapsed and expanded widths match exactly.</summary>
    private float ContentWidthFromChildren()
    {
        var maxRight = 0f;
        foreach (var child in _children)
        {
            maxRight = System.Math.Max(maxRight, child.RelativePosition.X + child.CurrentSize.X);
        }

        return maxRight + ContentPadding.X;
    }

    /// <summary>
    /// The icon lives in the header rect, not the content rect, so toggling here (not
    /// OnContentClickAction/Clicked, which only fire for content-rect clicks) is what makes
    /// clicking the folder itself -- in either display mode -- expand/collapse it.
    /// </summary>
    protected override void OnHeaderClickAction(Point mousePosition)
    {
        SetDisplayMode(DisplayMode == ElementDisplayMode.Minimized
            ? ElementDisplayMode.WrapContent
            : ElementDisplayMode.Minimized);
    }

    /// <summary>Background fill, then sprite-else-glyph icon, mirroring MapWindow.TryDrawEntityVisual's fallback for entities with no manifest entry.</summary>
    protected override void DrawHeader(GameTime gameTime, SpriteBatch spriteBatch, Texture2D unitRectangle)
    {
        if (!IsTransparent)
        {
            spriteBatch.Draw(unitRectangle, HeaderRectangle, _backgroundColor);
        }

        // The header grows wider than the icon once expanded (see
        // RecalculateWrapContentWindowSize, which widens _header.Size to match content) -- the
        // icon itself stays fixed at _iconSize regardless, centered within that wider header
        // rect rather than stretched to fill it.
        var iconTopLeft = HeaderAbsolutePosition + (HeaderSize - _iconSize) / 2f;

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
