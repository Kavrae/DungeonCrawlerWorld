using FontStashSharp;
using Game.Views;
using Game.World;
using Microsoft.Xna.Framework;
using Presentation.Rendering;
using Presentation.UI.Chrome;
using Presentation.UI.ColorPalettes;
using UIEngine;
using UIEngine.Fonts;
using UIEngine.Rendering;
using UIEngine.UI;

namespace Presentation.UI.Content;

/// <summary>PlayerManaBarContent's hover popup: the player's natural mana regeneration rate.</summary>
public sealed class PlayerManaHoverContent(World world, ActionStateView actionStateView, FontService fontService) : IElementContent
{
    private const float RowHeight = 16f;
    private const float RowContentWidth = 170f;

    public static readonly Vector2 ContentSize = new(RowContentWidth, RowHeight);

    /// <summary>The regeneration row's text, rebuilt only when the rate shown changes.</summary>
    private readonly KeyedCache<int, string> _regenerationTextCache = new();

    private Window _hostWindow = null!;
    private SpriteFontBase _font = null!;

    public void Initialize(Window hostWindow)
    {
        _hostWindow = hostWindow;
        _font = fontService.GetFont(GameFontChrome.PlayerHealthHoverFontSize);
    }

    public void Update(GameTime gameTime) { }

    /// <summary>The regeneration row's text, or null when there is no player.</summary>
    internal string? RegenerationText()
    {
        var playerEntityId = world.PlayerEntityId;
        if (playerEntityId < 0)
        {
            return null;
        }

        var tenths = ResourceRegenerationText.DisplayedTenths(actionStateView.ManaRegenerationPerSecond(playerEntityId));
        return _regenerationTextCache.Get(tenths, static key => ResourceRegenerationText.Format(key));
    }

    public void DrawContent(GameTime gameTime)
    {
        var regenerationText = RegenerationText();
        if (regenerationText is null)
        {
            return;
        }

        LabelRenderer.DrawLeftAligned(_hostWindow.Render.Batch, _font, regenerationText, Vector2.Zero, new Vector2(_hostWindow.ContentSize.X, RowHeight), ManaBarPalette.RegenerationTextColor);
    }
}
