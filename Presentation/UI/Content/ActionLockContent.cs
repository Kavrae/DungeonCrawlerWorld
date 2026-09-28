using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Engine.ECS.Systems;
using FontStashSharp;
using Game.Modules.Core.Components;
using Game.Views;
using Game.World;
using Microsoft.Xna.Framework;
using Presentation.Fonts;
using Presentation.Rendering;
using Presentation.UI.Chrome;

namespace Presentation.UI.Content;

/// <param name="simulationClock">"CurrentFrame" for the lock's remaining frames -- the lock is a deadline (see ActionLockGate).</param>
public sealed class ActionLockContent(World world, ComponentManager componentManager, IMapViewQuery mapView, FontService fontService, SimulationClock simulationClock) : IElementContent
{

    public static readonly Vector2 Size = new(HudChrome.EntrySize.Y * 1.5f, HudChrome.EntrySize.Y * 1.5f);

    private const int ContentInset = 2;

    private readonly PackedComponentPool<ActionLockComponent> _actionLocks = componentManager.GetPackedPool<ActionLockComponent>();
    private readonly RadialFillRenderer _radialFill = new(new LabelRenderer());

    private Window _hostWindow = null!;
    private SpriteFontBase _font = null!;

    private bool _hasActionLock;
    private string _glyph = string.Empty;
    private Color _glyphColor;
    private float _fillPercentage;

    public void Initialize(Window hostWindow)
    {
        _hostWindow = hostWindow;
        _font = fontService.GetFont((int)(Size.Y * FontChrome.ActionLockGlyphFontFraction));
    }

    /// <summary>Whether the player currently has an action lock to show, and its fill fraction, is decided here -- Draw only reads the cached result and turns it into pixels.</summary>
    public void Update(GameTime gameTime)
    {
        var playerEntityId = world.PlayerEntityId;
        if (playerEntityId < 0 || !_actionLocks.TryGetReadonly(playerEntityId, out var actionLock) || !mapView.TryGetVisual(playerEntityId, out var visual))
        {
            _hasActionLock = false;
            return;
        }

        _hasActionLock = true;
        _glyph = visual.Glyph;
        _glyphColor = visual.GlyphColor;
        _fillPercentage = actionLock.CurrentLockTotalFrames > 0
            ? (float)ActionLockGate.FramesRemaining(actionLock, simulationClock.CurrentFrame) / actionLock.CurrentLockTotalFrames
            : 0f;
    }

    public void DrawContent(GameTime gameTime)
    {
        if (!_hasActionLock)
        {
            return;
        }

        var spriteBatch = _hostWindow.ElementPoolService.SpriteBatch;
        var unitRectangle = _hostWindow.ElementPoolService.UnitRectangle;

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
