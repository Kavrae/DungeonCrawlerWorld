using Engine.Diagnostics;
using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Engine.ECS.Entities;
using FontStashSharp;
using Game.Modules.Movement.Components;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Presentation.Fonts;
using Presentation.UI.Chrome;

namespace Presentation.UI.Content;

/// <summary>
/// Admin-only entity-count/FPS-UPS debug readout. Rate counting is delegated to
/// Engine/Diagnostics's PerformanceCounter rather than rolled inline, and entity/component
/// counts are read live off EntityManager/ComponentManager. Also surfaces
/// diagnostics.TopFrameCostEntry (see GameLoop -- the DiagnosticsEngine instance
/// SystemManager/EventBus record every Update against), if diagnostics is non-null and the
/// FrameBudget feature is enabled, so a hotspot is visible while actually playing rather than
/// only in the console's periodic full-ranking dump.
/// </summary>
public sealed class DebugWindowContent(
    FontService fontService,
    EntityManager entityManager,
    ComponentManager componentManager,
    DiagnosticsEngine? diagnostics) : IElementContent
{
    private static readonly Vector2 DrawsPerSecondOffset = new(60, 0);
    private static readonly Vector2 EntityCountOffset = new(120, 0);
    private static readonly Vector2 MovingEntityCountOffset = new(200, 0);
    private static readonly Vector2 TopPhaseOffset = new(340, 0);

    /// <summary>Mirrors TextWindow.LinePadding -- the same small left inset every other text-bearing window in this codebase already uses.</summary>
    private const float LeftPadding = 3f;

    private readonly PerformanceCounter _updateCounter = new();
    private readonly PerformanceCounter _drawCounter = new();

    // Resolved once instead of via ComponentManager's dictionary lookup on every Update call.
    private readonly PackedComponentPool<MovementComponent> _movementPool = componentManager.GetPackedPool<MovementComponent>();

    private SpriteFontBase _font = null!;
    private Window _hostWindow = null!;

    private string _updatesPerSecondText = string.Empty;
    private string _drawsPerSecondText = string.Empty;
    private string _entityCountText = string.Empty;
    private string _movingEntityCountText = string.Empty;
    private string _topPhaseText = string.Empty;

    public void Initialize(Window hostWindow)
    {
        _hostWindow = hostWindow;
        _font = fontService.GetFont(FontChrome.DebugWindowFontSize);
    }

    public void Update(GameTime gameTime)
    {
        _updateCounter.Tick();
        _updatesPerSecondText = $"{_updateCounter.RatePerSecond:N1} ups";

        _entityCountText = $"Entities : {entityManager.LivingEntityCount:N0}";
        _movingEntityCountText = $"Moving Entities : {_movementPool.Count:N0}";

        _topPhaseText = diagnostics?.TopFrameCostEntry is { } topEntry
            ? $"Top: {topEntry.Name} {topEntry.MillisecondsPerSecond:N0}ms/s"
            : string.Empty;
    }

    public void DrawContent(GameTime gameTime)
    {
        var spriteBatch = _hostWindow.ElementPoolService.SpriteBatch;
        _drawCounter.Tick();
        _drawsPerSecondText = $"{_drawCounter.RatePerSecond:N1} fps";

        var verticalOffset = (_hostWindow.ContentSize.Y - _font.LineHeight) / 2f;
        var origin = _hostWindow.ContentAbsolutePosition + new Vector2(LeftPadding, verticalOffset);
        var rateColor = gameTime.IsRunningSlowly
            ? Color.Red
            : Color.Black;
        spriteBatch.DrawString(_font, _updatesPerSecondText, origin, rateColor);
        spriteBatch.DrawString(_font, _drawsPerSecondText, origin + DrawsPerSecondOffset, rateColor);
        spriteBatch.DrawString(_font, _entityCountText, origin + EntityCountOffset, Color.Black);
        spriteBatch.DrawString(_font, _movingEntityCountText, origin + MovingEntityCountOffset, Color.Black);
        spriteBatch.DrawString(_font, _topPhaseText, origin + TopPhaseOffset, Color.Black);
    }
}